#!/usr/bin/env bash
# Diagnostic wrapper — output goes to Railway Deploy Logs
set -uo pipefail

echo "[START] $(date -u) container starting"
echo "[START] PORT=${PORT:-NOT_SET}"
echo "[START] ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-NOT_SET}"
echo "[START] DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=${DOTNET_SYSTEM_GLOBALIZATION_INVARIANT:-NOT_SET}"
echo "[START] Working directory: $(pwd)"
echo "[START] Binary check:"
ls -la /app/out/SecureShop.API 2>&1 || echo "[START] ERROR: binary not found at /app/out/SecureShop.API"

# Disable ICU globalization requirement — common silent crash cause on minimal Linux images
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

echo "[START] Launching /app/out/SecureShop.API ..."
exec /app/out/SecureShop.API
