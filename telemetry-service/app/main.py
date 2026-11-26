"""
Dịch vụ Telemetry Security Proxy (FastAPI) cho Death Relapse (v0.9).

Biện pháp bảo mật #1:
- Xác thực HMAC-SHA256 + chống Replay Attack (X-DR-Signature, X-DR-Timestamp).
- Kiểm tra hợp đồng 12 trường Unity, chặn CSV/Formula Injection (OWASP A03) và bất biến T = H + D + M (OWASP A04).
- Giới hạn tần suất (Rate Limiting) theo IP.
- Đọc toàn bộ bí mật (HMAC_SECRET, APPS_SCRIPT_URL) từ biến môi trường; không bao giờ in bí mật ra log hay response.
"""

from __future__ import annotations

import json
import logging
import os
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import httpx
from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from app.security import (
    CLIENT_VERSION,
    DEFAULT_MAX_TIMESTAMP_SKEW_SECONDS,
    SlidingWindowRateLimiter,
    TelemetryAuthError,
    TelemetryValidationError,
    validate_match_report_payload,
    verify_request_signature_and_replay,
)

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)
logger = logging.getLogger("death_relapse.telemetry_proxy")


def _parse_bool_env(name: str, default: bool) -> bool:
    raw = os.getenv(name)
    if raw is None or not raw.strip():
        return default
    return raw.strip().lower() in {"1", "true", "yes", "on"}


def _parse_int_env(name: str, default: int) -> int:
    raw = os.getenv(name)
    if raw is None or not raw.strip():
        return default
    try:
        return int(raw.strip())
    except ValueError:
        return default


def _default_audit_log_path() -> str:
    return os.getenv(
        "AUDIT_LOG_PATH",
        str(Path(tempfile.gettempdir()) / "death_relapse_telemetry_audit.jsonl"),
    )


def create_app() -> FastAPI:
    app = FastAPI(
        title="Death Relapse Telemetry Security Proxy",
        version=CLIENT_VERSION,
    )

    rate_limit_per_min = _parse_int_env("RATE_LIMIT_PER_MIN", 10)
    app.state.rate_limiter = SlidingWindowRateLimiter(max_requests=rate_limit_per_min, window_seconds=60.0)

    @app.get("/health")
    async def health() -> dict[str, Any]:
        dry_run = _parse_bool_env("DRY_RUN", True)
        return {
            "status": "ok",
            "service": "death-relapse-telemetry-proxy",
            "version": CLIENT_VERSION,
            "dryRun": dry_run,
            "hmacConfigured": bool(os.getenv("HMAC_SECRET", "").strip()),
        }

    @app.post("/api/telemetry")
    async def ingest_telemetry(request: Request) -> JSONResponse:
        # 1. Rate limiting theo IP trước khi xử lý payload
        client_ip = _resolve_client_ip(request)
        allowed, retry_after = app.state.rate_limiter.allow(client_ip)
        if not allowed:
            logger.warning("RateLimitExceeded | ip=%s | retry_after=%ss", client_ip, retry_after)
            return JSONResponse(
                status_code=429,
                headers={"Retry-After": str(retry_after)},
                content={
                    "status": "rejected",
                    "code": "RateLimitExceeded",
                    "detail": "Vượt quá giới hạn tần suất yêu cầu cho phép.",
                },
            )

        raw_bytes = await request.body()
        if len(raw_bytes) > 65536:
            return JSONResponse(
                status_code=400,
                content={
                    "status": "rejected",
                    "code": "PayloadTooLarge",
                    "detail": "Kích thước payload vượt quá 64 KB.",
                },
            )

        try:
            raw_body = raw_bytes.decode("utf-8")
        except UnicodeDecodeError:
            return JSONResponse(
                status_code=400,
                content={
                    "status": "rejected",
                    "code": "InvalidEncoding",
                    "detail": "Payload phải được mã hóa UTF-8.",
                },
            )

        hmac_secret = os.getenv("HMAC_SECRET", "").strip()
        max_skew = _parse_int_env("MAX_TIMESTAMP_SKEW_SECONDS", DEFAULT_MAX_TIMESTAMP_SKEW_SECONDS)

        # 2. Xác thực chữ ký HMAC-SHA256 & chống Replay Attack (HTTP 401)
        try:
            verified = verify_request_signature_and_replay(
                raw_body=raw_body,
                header_signature=request.headers.get("X-DR-Signature"),
                header_timestamp=request.headers.get("X-DR-Timestamp"),
                header_client_version=request.headers.get("X-DR-Client-Version"),
                secret_key=hmac_secret,
                max_skew_seconds=max_skew,
            )
        except TelemetryAuthError as auth_err:
            logger.warning("AuthRejected | ip=%s | code=%s", client_ip, auth_err.code)
            return JSONResponse(
                status_code=401,
                content={
                    "status": "rejected",
                    "code": auth_err.code,
                    "detail": auth_err.message,
                },
            )
        except TelemetryValidationError as val_err:
            logger.warning("ValidationRejected | ip=%s | code=%s", client_ip, val_err.code)
            return JSONResponse(
                status_code=400,
                content={
                    "status": "rejected",
                    "code": val_err.code,
                    "detail": val_err.message,
                },
            )

        # 3. Kiểm tra schema 12 trường, Formula Injection & bất biến T = H + D + M (HTTP 400)
        try:
            clean_payload = validate_match_report_payload(verified.payload)
        except TelemetryValidationError as val_err:
            logger.warning("ValidationRejected | ip=%s | code=%s", client_ip, val_err.code)
            return JSONResponse(
                status_code=400,
                content={
                    "status": "rejected",
                    "code": val_err.code,
                    "detail": val_err.message,
                },
            )

        dry_run = _parse_bool_env("DRY_RUN", True)
        apps_script_url = os.getenv("APPS_SCRIPT_URL", "").strip()
        forwarded = False

        # 4. Ghi log kiểm toán cục bộ (tuyệt đối không ghi bí mật hay URL nhạy cảm)
        _append_audit_record(
            clean_payload=clean_payload,
            timestamp_unix=verified.timestamp_unix,
            record_hash=verified.signature,
            client_version=verified.client_version,
            dry_run=dry_run,
        )

        # 5. Chuyển tiếp sang Google Apps Script khi DRY_RUN=false và có APPS_SCRIPT_URL
        if not dry_run and apps_script_url:
            try:
                async with httpx.AsyncClient(follow_redirects=True, timeout=8.0) as client:
                    upstream = await client.post(apps_script_url, json=clean_payload)
                if 200 <= upstream.status_code < 300:
                    forwarded = True
                else:
                    logger.error("UpstreamForwardFailed | status_code=%s", upstream.status_code)
                    return JSONResponse(
                        status_code=502,
                        content={
                            "status": "error",
                            "code": "UpstreamForwardFailed",
                            "detail": "Không thể chuyển tiếp bản ghi lên máy chủ lưu trữ.",
                        },
                    )
            except httpx.HTTPError:
                # Tuyệt đối không đưa exception detail chứa APPS_SCRIPT_URL ra log hoặc response
                logger.error("UpstreamConnectionError | sessionId=%s", clean_payload["sessionId"])
                return JSONResponse(
                    status_code=502,
                    content={
                        "status": "error",
                        "code": "UpstreamConnectionError",
                        "detail": "Lỗi kết nối khi chuyển tiếp bản ghi Telemetry.",
                    },
                )

        logger.info(
            "Accepted | sessionId=%s | group=%s | result=%s | forwarded=%s",
            clean_payload["sessionId"],
            clean_payload["experimentGroup"],
            clean_payload["matchResult"],
            forwarded,
        )
        return JSONResponse(
            status_code=200,
            content={
                "status": "accepted",
                "forwarded": forwarded,
                "sessionId": clean_payload["sessionId"],
                "clientVersion": verified.client_version,
            },
        )

    return app


def _resolve_client_ip(request: Request) -> str:
    forwarded_for = request.headers.get("X-Forwarded-For")
    if forwarded_for:
        return forwarded_for.split(",")[0].strip() or "unknown"
    return request.client.host if request.client else "unknown"


def _append_audit_record(
    clean_payload: dict[str, Any],
    timestamp_unix: int,
    record_hash: str,
    client_version: str,
    dry_run: bool,
) -> None:
    audit_path = Path(_default_audit_log_path())
    entry = {
        "receivedAtUtc": datetime.now(timezone.utc).isoformat(),
        "clientVersion": client_version,
        "dryRun": dry_run,
        "timestampUnix": timestamp_unix,
        "recordHash": record_hash,
        "payload": clean_payload,
    }
    try:
        audit_path.parent.mkdir(parents=True, exist_ok=True)
        with audit_path.open("a", encoding="utf-8") as fh:
            fh.write(json.dumps(entry, ensure_ascii=False, separators=(",", ":")) + "\n")
    except OSError as exc:
        logger.warning("AuditWriteWarning | reason=%s", type(exc).__name__)


app = create_app()
