"""
Bộ kiểm thử bảo mật tự động (pytest) cho Telemetry Security Proxy (v0.9).
Kiểm tra tối thiểu 10 tình huống gồm: 200 hợp lệ (khớp chính xác phong bì Unity),
401 thiếu chữ ký / sai chữ ký / timestamp hết hạn / body bị sửa,
400 vi phạm bất biến T=H+D+M / Formula Injection / experimentGroup giả / surveyAnswer giả / vượt ngưỡng,
và 429 vượt Rate Limit.
"""

from __future__ import annotations

import json
import sys
import time
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

SERVICE_ROOT = Path(__file__).resolve().parents[1]
if str(SERVICE_ROOT) not in sys.path:
    sys.path.insert(0, str(SERVICE_ROOT))

from app.main import create_app
from app.security import (
    CLIENT_VERSION,
    build_canonical_payload,
    compute_record_hmac_sha256,
    create_signed_envelope_json,
)

TEST_SECRET = "death-relapse-ci-test-hmac-secret-v09"


def make_valid_match_report(**overrides) -> dict:
    base = {
        "timestamp": "26/11/2026 - 12h30p00s",
        "sessionId": "ab12cd34",
        "experimentGroup": "GroupB_WithTelegraph",
        "matchResult": "Defeat",
        "elapsedSeconds": 42.5,
        "playerHP": 0.0,
        "bossHP": 1200.0,
        "totalBossAttacks": 8,
        "totalHitsTaken": 3,
        "totalDodgedIFrame": 3,
        "attackBreakdown": "RedAttack(T:5,H:2,D:2,M:1);StrongRedAttack(T:3,H:1,D:1,M:1)",
        "surveyAnswer": "PlayerSkill_Timing",
    }
    base.update(overrides)
    return base


def sign_unity_payload(payload: dict, ts_unix: int | None = None, secret: str = TEST_SECRET) -> tuple[str, dict[str, str]]:
    now_ts = int(time.time()) if ts_unix is None else ts_unix
    # Serialize compact giống Unity JsonUtility.ToJson
    raw_json = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
    signed_body, signature = create_signed_envelope_json(raw_json, now_ts, secret)
    headers = {
        "Content-Type": "application/json",
        "X-DR-Signature": signature,
        "X-DR-Timestamp": str(now_ts),
        "X-DR-Client-Version": CLIENT_VERSION,
    }
    return signed_body, headers


@pytest.fixture()
def client(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> TestClient:
    monkeypatch.setenv("HMAC_SECRET", TEST_SECRET)
    monkeypatch.setenv("DRY_RUN", "true")
    monkeypatch.setenv("APPS_SCRIPT_URL", "")
    monkeypatch.setenv("RATE_LIMIT_PER_MIN", "20")
    monkeypatch.setenv("AUDIT_LOG_PATH", str(tmp_path / "audit.jsonl"))
    app = create_app()
    return TestClient(app)


def test_health_endpoint_returns_200(client: TestClient) -> None:
    resp = client.get("/health")
    assert resp.status_code == 200
    body = resp.json()
    assert body["status"] == "ok"
    assert body["version"] == CLIENT_VERSION
    assert body["hmacConfigured"] is True


def test_valid_unity_signed_payload_returns_200(client: TestClient) -> None:
    body, headers = sign_unity_payload(make_valid_match_report())
    resp = client.post("/api/telemetry", content=body.encode("utf-8"), headers=headers)
    assert resp.status_code == 200
    data = resp.json()
    assert data["status"] == "accepted"
    assert data["forwarded"] is False
    assert data["sessionId"] == "ab12cd34"


def test_missing_signature_header_returns_401(client: TestClient) -> None:
    body, headers = sign_unity_payload(make_valid_match_report())
    headers.pop("X-DR-Signature")
    resp = client.post("/api/telemetry", content=body.encode("utf-8"), headers=headers)
    assert resp.status_code == 401
    assert resp.json()["code"] == "MissingSignature"


def test_wrong_signature_returns_401(client: TestClient) -> None:
    body, headers = sign_unity_payload(make_valid_match_report(), secret="wrong-attacker-key")
    resp = client.post("/api/telemetry", content=body.encode("utf-8"), headers=headers)
    assert resp.status_code == 401
    assert resp.json()["code"] == "InvalidSignature"


def test_expired_timestamp_replay_attack_returns_401(client: TestClient) -> None:
    expired_ts = int(time.time()) - 600
    body, headers = sign_unity_payload(make_valid_match_report(), ts_unix=expired_ts)
    resp = client.post("/api/telemetry", content=body.encode("utf-8"), headers=headers)
    assert resp.status_code == 401
    assert resp.json()["code"] == "TimestampExpired"


def test_tampered_body_after_signing_returns_401(client: TestClient) -> None:
    body, headers = sign_unity_payload(make_valid_match_report())
    tampered_body = body.replace('"bossHP":1200.0', '"bossHP":1201.0')
    resp = client.post("/api/telemetry", content=tampered_body.encode("utf-8"), headers=headers)
    assert resp.status_code == 401
    assert resp.json()["code"] == "InvalidSignature"


def test_invariant_violation_total_mismatch_returns_400(client: TestClient) -> None:
    bad_payload = make_valid_match_report(
        totalBossAttacks=5,
        totalHitsTaken=4,
        totalDodgedIFrame=3,
        attackBreakdown="RedAttack(T:5,H:4,D:3,M:0)",
    )
    body, headers = sign_unity_payload(bad_payload)
    resp = client.post("/api/telemetry", content=body.encode("utf-8"), headers=headers)
    assert resp.status_code == 400
    assert resp.json()["code"] == "InvariantViolation"


def test_formula_injection_blocked_returns_400(client: TestClient) -> None:
    evil_payload = make_valid_match_report(
        attackBreakdown='=HYPERLINK("http://evil.example","Click")',
    )
    body, headers = sign_unity_payload(evil_payload)
    resp = client.post("/api/telemetry", content=body.encode("utf-8"), headers=headers)
    assert resp.status_code == 400
    assert resp.json()["code"] == "FormulaInjectionBlocked"


def test_invalid_experiment_group_returns_400(client: TestClient) -> None:
    fake_group_payload = make_valid_match_report(experimentGroup="GroupC_CheatedMode")
    body, headers = sign_unity_payload(fake_group_payload)
    resp = client.post("/api/telemetry", content=body.encode("utf-8"), headers=headers)
    assert resp.status_code == 400
    assert resp.json()["code"] == "InvalidExperimentGroup"


def test_invalid_survey_answer_returns_400(client: TestClient) -> None:
    fake_survey_payload = make_valid_match_report(surveyAnswer="FakeReason_Injection")
    body, headers = sign_unity_payload(fake_survey_payload)
    resp = client.post("/api/telemetry", content=body.encode("utf-8"), headers=headers)
    assert resp.status_code == 400
    assert resp.json()["code"] == "InvalidSurveyAnswer"


def test_total_attacks_over_upper_limit_returns_400(client: TestClient) -> None:
    huge_payload = make_valid_match_report(
        totalBossAttacks=501,
        totalHitsTaken=100,
        totalDodgedIFrame=100,
        attackBreakdown="",
    )
    body, headers = sign_unity_payload(huge_payload)
    resp = client.post("/api/telemetry", content=body.encode("utf-8"), headers=headers)
    assert resp.status_code == 400
    assert resp.json()["code"] == "OutOfRangeValue"


def test_rate_limit_exceeded_returns_429(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    monkeypatch.setenv("HMAC_SECRET", TEST_SECRET)
    monkeypatch.setenv("DRY_RUN", "true")
    monkeypatch.setenv("RATE_LIMIT_PER_MIN", "3")
    monkeypatch.setenv("AUDIT_LOG_PATH", str(tmp_path / "audit_rl.jsonl"))

    limited_client = TestClient(create_app())
    body, headers = sign_unity_payload(make_valid_match_report())

    for _ in range(3):
        ok_resp = limited_client.post("/api/telemetry", content=body.encode("utf-8"), headers=headers)
        assert ok_resp.status_code == 200

    blocked_resp = limited_client.post("/api/telemetry", content=body.encode("utf-8"), headers=headers)
    assert blocked_resp.status_code == 429
    assert blocked_resp.json()["code"] == "RateLimitExceeded"


def test_cross_language_canonical_hmac_vector_matches_csharp() -> None:
    """
    Vector đối chiếu chéo 100% với TelemetrySecurityCoreTests.cs (C# NUnit):
    cùng rawJson, cùng timestampUnix, cùng secretKey -> phải ra cùng một mã HMAC-SHA256.
    """
    raw_json = (
        '{"timestamp":"26/11/2026 - 12h00p00s","sessionId":"sync0001",'
        '"experimentGroup":"GroupA_NoTelegraph","matchResult":"Defeat",'
        '"elapsedSeconds":30.5,"playerHP":0,"bossHP":1500,'
        '"totalBossAttacks":6,"totalHitsTaken":2,"totalDodgedIFrame":3,'
        '"attackBreakdown":"RedAttack(T:6,H:2,D:3,M:1)","surveyAnswer":"BossTelegraph_Unclear"}'
    )
    ts = 1795600000
    secret = "cross-lang-vector-secret-v09"
    canonical = build_canonical_payload(raw_json, ts)
    digest = compute_record_hmac_sha256(canonical, secret)
    # Kiểm tra chữ ký cố định trùng khớp tuyệt đối từng ký tự với C# TelemetrySecurityCoreTests.cs
    assert digest == "583e1fc7e0d44bb978bf08a83f1d05314125dcfbdb32e59b00ca1ef4cf07d1c9"
