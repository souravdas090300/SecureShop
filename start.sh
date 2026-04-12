#!/usr/bin/env bash
set -euo pipefail

cd SecureShop.API

dotnet restore

dotnet run --project SecureShop.API.csproj --configuration Release --urls "http://0.0.0.0:${PORT:-8080}"
