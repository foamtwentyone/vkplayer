#!/usr/bin/env bash
# Кросс-сборка Windows-версии на Linux/macOS (WPF собирается благодаря EnableWindowsTargeting).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/dist/VkHotkeys"
RID="${RID:-win-x64}"
SC="${SELF_CONTAINED:-false}"
rm -rf "$OUT"
dotnet test "$ROOT/tests/VkHotkeys.Core.Tests" -c Release
for p in src/VkHotkeys.App/VkHotkeys.App.csproj src/VkHotkeys.Bridge/VkHotkeys.Bridge.csproj; do
  dotnet publish "$ROOT/$p" -c Release -r "$RID" --self-contained "$SC" -o "$OUT" -p:DebugType=none
done
cp -r "$ROOT/extension" "$OUT/extension"
echo "Готово: $OUT"
