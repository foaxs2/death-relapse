"""
Lõi kiểm tra bảo mật & toàn vẹn dữ liệu cho Telemetry Security Proxy (v0.9).

Hợp đồng dữ liệu khớp 100% từng ký tự với:
- Assets/Scripts/Core/TelemetrySecurityCore.cs
- Assets/Scripts/Core/TelemetryLogger.cs
"""

from __future__ import annotations

import hashlib
import hmac
import json
import math
import re
import time
from collections import deque
from dataclasses import dataclass
from threading import Lock
from typing import Any

CLIENT_VERSION = "v0.9"
DEFAULT_MAX_TIMESTAMP_SKEW_SECONDS = 300
MAX_TOTAL_BOSS_ATTACKS = 500
MAX_ELAPSED_SECONDS = 86400.0
MAX_PLAYER_HP = 10000.0
MAX_BOSS_HP = 100000.0

ALLOWED_EXPERIMENT_GROUPS = frozenset({
    "GroupA_NoTelegraph",
    "GroupB_WithTelegraph",
})

ALLOWED_SURVEY_ANSWERS = frozenset({
    "PlayerWon",
    "PlayerSkill_Timing",
    "BossTelegraph_Unclear",
})

ALLOWED_MATCH_RESULTS = frozenset({
    "Victory",
    "Defeat",
})

# Khớp chính xác 12 trường cấp 1 của MatchReportPayload trong TelemetryLogger.cs
REQUIRED_MATCH_REPORT_FIELDS = (
    "timestamp",
    "sessionId",
    "experimentGroup",
    "matchResult",
    "elapsedSeconds",
    "playerHP",
    "bossHP",
    "totalBossAttacks",
    "totalHitsTaken",
    "totalDodgedIFrame",
    "attackBreakdown",
    "surveyAnswer",
)

# Khớp chính xác Regex của TelemetrySecurityCore.cs để trích xuất coreJson + timestampUnix + recordHash
SIGNED_SUFFIX_REGEX = re.compile(
    r',"timestampUnix":(-?\d+),"recordHash":"([0-9a-fA-F]{64})"\}$'
)
EMPTY_SIGNED_OBJECT_REGEX = re.compile(
    r'^\{"timestampUnix":(-?\d+),"recordHash":"([0-9a-fA-F]{64})"\}$'
)
BREAKDOWN_ENTRY_REGEX = re.compile(
    r"\(T:(-?\d+),H:(-?\d+),D:(-?\d+),M:(-?\d+)\)"
)

FORMULA_TRIGGER_CHARS = frozenset({"=", "+", "-", "@", "\t", "\r"})


class TelemetryAuthError(Exception):
    """Lỗi xác thực chữ ký HMAC-SHA256 hoặc chống Replay Attack (HTTP 401)."""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code
        self.message = message


class TelemetryValidationError(Exception):
    """Lỗi xác thực schema, Formula Injection hoặc bất biến nghiệp vụ (HTTP 400)."""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code
        self.message = message


def is_formula_trigger_char(ch: str) -> bool:
    return ch in FORMULA_TRIGGER_CHARS


def is_dangerous_control_char(ch: str) -> bool:
    code = ord(ch)
    return code < 0x20 or code == 0x7F


def is_dangerous_spreadsheet_field(value: str | None) -> bool:
    """Khớp chính xác TelemetrySecurityCore.IsDangerousSpreadsheetField(string)."""
    if not value:
        return False

    if is_formula_trigger_char(value[0]):
        return True

    for ch in value:
        if is_dangerous_control_char(ch):
            return True

    trimmed_start = value.lstrip(" ")
    return len(trimmed_start) > 0 and is_formula_trigger_char(trimmed_start[0])


def sanitize_spreadsheet_field(value: str | None, reject_dangerous: bool = False) -> str:
    """Khớp chính xác TelemetrySecurityCore.SanitizeSpreadsheetField(string, bool)."""
    if not value:
        return ""

    has_leading_formula_trigger = is_formula_trigger_char(value[0])
    has_control_chars = False
    first_non_space_index = -1

    for idx, ch in enumerate(value):
        if is_dangerous_control_char(ch):
            has_control_chars = True
        if first_non_space_index < 0 and ch != " ":
            first_non_space_index = idx

    if first_non_space_index >= 0 and is_formula_trigger_char(value[first_non_space_index]):
        has_leading_formula_trigger = True

    if reject_dangerous and (has_leading_formula_trigger or has_control_chars):
        raise TelemetryValidationError(
            "FormulaInjectionBlocked",
            "Phát hiện chuỗi đầu vào có nguy cơ CSV/Formula Injection hoặc chứa ký tự điều khiển độc hại.",
        )

    cleaned_chars: list[str] = []
    if has_leading_formula_trigger:
        cleaned_chars.append("'")

    for ch in value:
        if is_dangerous_control_char(ch):
            continue
        cleaned_chars.append(ch)

    if not has_leading_formula_trigger and cleaned_chars and is_formula_trigger_char(cleaned_chars[0]):
        cleaned_chars.insert(0, "'")

    return "".join(cleaned_chars)


def validate_telemetry_invariants(total: int, hit: int, dodged: int, missed: int) -> tuple[bool, str]:
    """Khớp chính xác TelemetrySecurityCore.ValidateTelemetryInvariants(int, int, int, int)."""
    if total < 0 or hit < 0 or dodged < 0 or missed < 0:
        return (
            False,
            f"InvariantViolation: Giá trị thống kê không được âm (T={total}, H={hit}, D={dodged}, M={missed}).",
        )

    if hit + dodged + missed != total:
        return (
            False,
            f"InvariantViolation: Tổng T ({total}) != H + D + M ({hit} + {dodged} + {missed} = {hit + dodged + missed}).",
        )

    return True, ""


def build_canonical_payload(core_json_object: str, timestamp_unix: int) -> str:
    """Khớp chính xác TelemetrySecurityCore.BuildCanonicalPayload(string, long)."""
    return f"{timestamp_unix}.{core_json_object or ''}"


def compute_record_hmac_sha256(canonical_payload: str, secret_key: str | None) -> str:
    """Khớp chính xác TelemetrySecurityCore.ComputeRecordHmacSha256(string, string)."""
    payload_bytes = (canonical_payload or "").encode("utf-8")
    if secret_key:
        key_bytes = secret_key.encode("utf-8")
        return hmac.new(key_bytes, payload_bytes, hashlib.sha256).hexdigest()
    return hashlib.sha256(payload_bytes).hexdigest()


def verify_record_integrity(canonical_payload: str, expected_signature: str | None, secret_key: str | None) -> bool:
    """So sánh chữ ký thời gian hằng số (hmac.compare_digest) khớp TelemetrySecurityCore.VerifyRecordIntegrity."""
    if canonical_payload is None or not expected_signature or not expected_signature.strip():
        return False

    computed = compute_record_hmac_sha256(canonical_payload, secret_key)
    normalized_expected = expected_signature.strip().lower()
    return hmac.compare_digest(computed, normalized_expected)


def create_signed_envelope_json(raw_json_object: str, timestamp_unix: int, secret_key: str) -> tuple[str, str]:
    """Khớp chính xác TelemetrySecurityCore.CreateSignedEnvelopeJson(string, long, string, out string)."""
    if not raw_json_object or not raw_json_object.strip():
        raise ValueError("JSON đầu vào không được rỗng.")

    trimmed = raw_json_object.strip()
    if len(trimmed) < 2 or not trimmed.startswith("{") or not trimmed.endswith("}"):
        raise ValueError("JSON đầu vào phải là đối tượng JSON hợp lệ bọc trong { }.")

    canonical = build_canonical_payload(trimmed, timestamp_unix)
    record_hash = compute_record_hmac_sha256(canonical, secret_key)

    if trimmed == "{}":
        signed_json = f'{{"timestampUnix":{timestamp_unix},"recordHash":"{record_hash}"}}'
    else:
        signed_json = f'{trimmed[:-1]},"timestampUnix":{timestamp_unix},"recordHash":"{record_hash}"}}'

    return signed_json, record_hash


def extract_signed_envelope(raw_body: str) -> tuple[str, int | None, str | None]:
    """
    Tách phần JSON lõi (12 trường) và cặp (timestampUnix, recordHash) ở cuối đối tượng JSON.
    Giữ nguyên từng byte của coreJson để khớp 100% với Unity JsonUtility.ToJson.
    """
    trimmed = raw_body.strip()
    if len(trimmed) < 2 or not trimmed.startswith("{") or not trimmed.endswith("}"):
        raise TelemetryValidationError("InvalidJson", "Payload phải là một đối tượng JSON hợp lệ.")

    match = SIGNED_SUFFIX_REGEX.search(trimmed)
    if match:
        ts_unix = int(match.group(1))
        record_hash = match.group(2)
        core_json = trimmed[: match.start()] + "}"
        return core_json, ts_unix, record_hash

    empty_match = EMPTY_SIGNED_OBJECT_REGEX.match(trimmed)
    if empty_match:
        ts_unix = int(empty_match.group(1))
        record_hash = empty_match.group(2)
        return "{}", ts_unix, record_hash

    return trimmed, None, None


@dataclass(frozen=True)
class VerifiedEnvelope:
    core_json: str
    payload: dict[str, Any]
    timestamp_unix: int
    signature: str
    client_version: str


def verify_request_signature_and_replay(
    raw_body: str,
    header_signature: str | None,
    header_timestamp: str | None,
    header_client_version: str | None,
    secret_key: str,
    now_unix: int | None = None,
    max_skew_seconds: int = DEFAULT_MAX_TIMESTAMP_SKEW_SECONDS,
) -> VerifiedEnvelope:
    """
    Bước 1 (HTTP 401): Xác thực chữ ký HMAC-SHA256 và cửa sổ thời gian chống Replay Attack.
    Bắt buộc HMAC_SECRET phải được cấu hình trên server để không chấp nhận chữ ký rỗng.
    """
    if not secret_key:
        raise TelemetryAuthError("ServerMisconfigured", "Máy chủ chưa cấu hình HMAC_SECRET.")

    if not header_signature or not header_signature.strip():
        raise TelemetryAuthError("MissingSignature", "Thiếu header X-DR-Signature.")

    if not header_timestamp or not header_timestamp.strip():
        raise TelemetryAuthError("MissingTimestamp", "Thiếu header X-DR-Timestamp.")

    try:
        ts_unix = int(header_timestamp.strip())
    except ValueError as exc:
        raise TelemetryAuthError("InvalidTimestamp", "Header X-DR-Timestamp phải là số nguyên Unix seconds.") from exc

    current_unix = int(time.time()) if now_unix is None else now_unix
    if abs(current_unix - ts_unix) > max_skew_seconds:
        raise TelemetryAuthError(
            "TimestampExpired",
            f"Timestamp lệch quá ngưỡng cho phép ({max_skew_seconds}s) — từ chối chống Replay Attack.",
        )

    try:
        core_json, body_ts, body_hash = extract_signed_envelope(raw_body)
    except TelemetryValidationError as exc:
        raise TelemetryAuthError("MalformedEnvelope", exc.message) from exc

    # Nếu body có đính kèm timestampUnix/recordHash (chuẩn Unity), bắt buộc khớp với header
    if body_ts is not None and body_ts != ts_unix:
        raise TelemetryAuthError("TimestampMismatch", "timestampUnix trong body không khớp X-DR-Timestamp.")

    if body_hash is not None and not hmac.compare_digest(body_hash.lower(), header_signature.strip().lower()):
        raise TelemetryAuthError("SignatureMismatch", "recordHash trong body không khớp X-DR-Signature.")

    # Xác minh chữ ký trực tiếp trên canonicalPayload = "{timestampUnix}.{coreJson}"
    canonical_payload = build_canonical_payload(core_json, ts_unix)
    if not verify_record_integrity(canonical_payload, header_signature, secret_key):
        raise TelemetryAuthError("InvalidSignature", "Chữ ký HMAC-SHA256 không hợp lệ hoặc dữ liệu đã bị sửa đổi.")

    try:
        parsed = json.loads(core_json)
    except json.JSONDecodeError as exc:
        raise TelemetryValidationError("InvalidJson", "Nội dung JSON không hợp lệ.") from exc

    if not isinstance(parsed, dict):
        raise TelemetryValidationError("InvalidJson", "Payload JSON phải là object.")

    # Chặn trường hợp kẻ tấn công đặt trường recordHash/timestampUnix sai vị trí để lách regex
    if "recordHash" in parsed or "timestampUnix" in parsed:
        raise TelemetryAuthError("MalformedEnvelope", "Cấu trúc phong bì chữ ký không đúng chuẩn.")

    return VerifiedEnvelope(
        core_json=core_json,
        payload=parsed,
        timestamp_unix=ts_unix,
        signature=header_signature.strip().lower(),
        client_version=(header_client_version or CLIENT_VERSION).strip() or CLIENT_VERSION,
    )


def _is_valid_number(value: Any) -> bool:
    if isinstance(value, bool):
        return False
    if not isinstance(value, (int, float)):
        return False
    return math.isfinite(float(value))


def _is_valid_int(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def validate_match_report_payload(payload: dict[str, Any]) -> dict[str, Any]:
    """
    Bước 2 (HTTP 400): Kiểm tra 12 trường cấp 1 khớp TelemetryLogger.MatchReportPayload,
    chặn CSV/Formula Injection (OWASP A03) và kiểm tra bất biến T == H + D + M (OWASP A04).
    """
    missing = [field for field in REQUIRED_MATCH_REPORT_FIELDS if field not in payload]
    if missing:
        raise TelemetryValidationError(
            "MissingRequiredFields",
            f"Thiếu trường bắt buộc theo hợp đồng Unity: {', '.join(missing)}.",
        )

    # 1. Kiểm tra Formula Injection trên TẤT CẢ các trường chuỗi
    string_fields = (
        "timestamp",
        "sessionId",
        "experimentGroup",
        "matchResult",
        "attackBreakdown",
        "surveyAnswer",
    )
    for field in string_fields:
        val = payload.get(field)
        if not isinstance(val, str):
            raise TelemetryValidationError("InvalidFieldType", f"Trường '{field}' phải là chuỗi.")
        if is_dangerous_spreadsheet_field(val):
            raise TelemetryValidationError(
                "FormulaInjectionBlocked",
                f"Trường '{field}' chứa ký tự nguy cơ CSV/Formula Injection hoặc ký tự điều khiển.",
            )

    if not payload["sessionId"].strip() or len(payload["sessionId"]) > 64:
        raise TelemetryValidationError("InvalidSessionId", "sessionId không hợp lệ.")

    if not payload["timestamp"].strip() or len(payload["timestamp"]) > 64:
        raise TelemetryValidationError("InvalidTimestampField", "timestamp không hợp lệ.")

    # 2. Kiểm tra allow-list nhóm thí nghiệm, kết quả và lý do khảo sát
    experiment_group = payload["experimentGroup"]
    if experiment_group not in ALLOWED_EXPERIMENT_GROUPS:
        raise TelemetryValidationError(
            "InvalidExperimentGroup",
            f"experimentGroup '{experiment_group}' không nằm trong danh sách hợp lệ.",
        )

    survey_answer = payload["surveyAnswer"]
    if survey_answer not in ALLOWED_SURVEY_ANSWERS:
        raise TelemetryValidationError(
            "InvalidSurveyAnswer",
            f"surveyAnswer '{survey_answer}' không nằm trong danh sách hợp lệ.",
        )

    match_result = payload["matchResult"]
    if match_result not in ALLOWED_MATCH_RESULTS:
        raise TelemetryValidationError(
            "InvalidMatchResult",
            f"matchResult '{match_result}' phải là Victory hoặc Defeat.",
        )

    # 3. Kiểm tra giới hạn số thực (elapsedSeconds, playerHP, bossHP)
    for num_field, upper_bound in (
        ("elapsedSeconds", MAX_ELAPSED_SECONDS),
        ("playerHP", MAX_PLAYER_HP),
        ("bossHP", MAX_BOSS_HP),
    ):
        num_val = payload[num_field]
        if not _is_valid_number(num_val) or float(num_val) < 0.0 or float(num_val) > upper_bound:
            raise TelemetryValidationError(
                "OutOfRangeValue",
                f"Trường '{num_field}' phải là số hữu hạn trong đoạn [0, {upper_bound}].",
            )

    # 4. Kiểm tra các bộ đếm nguyên và bất biến T == H + D + M
    for int_field in ("totalBossAttacks", "totalHitsTaken", "totalDodgedIFrame"):
        if not _is_valid_int(payload[int_field]):
            raise TelemetryValidationError("InvalidFieldType", f"Trường '{int_field}' phải là số nguyên.")

    total = payload["totalBossAttacks"]
    hits = payload["totalHitsTaken"]
    dodged = payload["totalDodgedIFrame"]

    if total > MAX_TOTAL_BOSS_ATTACKS:
        raise TelemetryValidationError(
            "OutOfRangeValue",
            f"totalBossAttacks ({total}) vượt quá giới hạn tối đa ({MAX_TOTAL_BOSS_ATTACKS}).",
        )

    missed = total - hits - dodged
    ok, inv_error = validate_telemetry_invariants(total, hits, dodged, missed)
    if not ok:
        raise TelemetryValidationError("InvariantViolation", inv_error)

    # 5. Nếu có attackBreakdown, xác minh từng mục (T,H,D,M) và tổng khớp với các trường tổng
    breakdown = payload["attackBreakdown"]
    if len(breakdown) > 1024:
        raise TelemetryValidationError("OutOfRangeValue", "attackBreakdown vượt quá độ dài cho phép.")

    if breakdown.strip():
        matches = list(BREAKDOWN_ENTRY_REGEX.finditer(breakdown))
        if not matches:
            raise TelemetryValidationError(
                "InvalidBreakdownFormat",
                "attackBreakdown không đúng định dạng thống kê (T:..,H:..,D:..,M:..).",
            )

        sum_t = 0
        sum_h = 0
        sum_d = 0
        for m in matches:
            t_i, h_i, d_i, m_i = int(m.group(1)), int(m.group(2)), int(m.group(3)), int(m.group(4))
            entry_ok, entry_err = validate_telemetry_invariants(t_i, h_i, d_i, m_i)
            if not entry_ok:
                raise TelemetryValidationError("InvariantViolation", entry_err)
            sum_t += t_i
            sum_h += h_i
            sum_d += d_i

        if sum_t != total or sum_h != hits or sum_d != dodged:
            raise TelemetryValidationError(
                "InvariantViolation",
                f"Tổng trong attackBreakdown (T={sum_t},H={sum_h},D={sum_d}) không khớp tổng báo cáo (T={total},H={hits},D={dodged}).",
            )

    return {field: payload[field] for field in REQUIRED_MATCH_REPORT_FIELDS}


class SlidingWindowRateLimiter:
    """Bộ giới hạn tần suất theo cửa sổ trượt (mặc định 10 requests / 60 giây mỗi IP)."""

    def __init__(self, max_requests: int = 10, window_seconds: float = 60.0) -> None:
        self.max_requests = max(1, int(max_requests))
        self.window_seconds = max(1.0, float(window_seconds))
        self._buckets: dict[str, deque[float]] = {}
        self._lock = Lock()

    def allow(self, client_ip: str, now_monotonic: float | None = None) -> tuple[bool, int]:
        now = time.monotonic() if now_monotonic is None else now_monotonic
        cutoff = now - self.window_seconds
        with self._lock:
            bucket = self._buckets.setdefault(client_ip, deque())
            while bucket and bucket[0] <= cutoff:
                bucket.popleft()

            if len(bucket) >= self.max_requests:
                retry_after = max(1, int(math.ceil(self.window_seconds - (now - bucket[0]))))
                return False, retry_after

            bucket.append(now)
            return True, 0

    def reset(self) -> None:
        with self._lock:
            self._buckets.clear()
