# syntax=docker/dockerfile:1
FROM python:3.12-slim AS builder

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1

WORKDIR /build
COPY telemetry-service/requirements.txt ./requirements.txt
RUN pip install --prefix=/install -r requirements.txt

FROM python:3.12-slim AS runtime

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    DRY_RUN=true \
    RATE_LIMIT_PER_MIN=10 \
    AUDIT_LOG_PATH=/tmp/death_relapse_telemetry_audit.jsonl

WORKDIR /app

# Tạo người dùng non-root để giảm bề mặt tấn công trong container
RUN groupadd --system appgroup && useradd --system --gid appgroup --create-home appuser

COPY --from=builder /install /usr/local
COPY telemetry-service/app ./app

RUN chown -R appuser:appgroup /app
USER appuser

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
    CMD python -c "import urllib.request; r = urllib.request.urlopen('http://127.0.0.1:8080/health', timeout=3); exit(0 if r.status == 200 else 1)"

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8080"]
