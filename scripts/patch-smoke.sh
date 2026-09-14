#!/usr/bin/env bash
set -euo pipefail
python3 - <<'PY'
from pathlib import Path
p=Path('.github/workflows/smoke-test.yml')
s=p.read_text()
old='dotnet run --project src/Swgoh.Api/Swgoh.Api.csproj --configuration Release --no-build --no-restore > smoke-results/api.log 2>&1 &'
new='dotnet run --project src/Swgoh.Api/Swgoh.Api.csproj --configuration Release --no-build --no-restore --no-launch-profile > smoke-results/api.log 2>&1 &'
if old not in s:
    raise SystemExit('target not found')
p.write_text(s.replace(old,new,1))
PY
