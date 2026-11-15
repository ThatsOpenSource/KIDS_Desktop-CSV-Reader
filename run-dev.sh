#!/usr/bin/env bash
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

dotnet run --project "$HERE/KidsXmlToCsv.csproj"
