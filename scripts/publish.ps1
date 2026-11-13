<#
.SYNOPSIS
  Собирает готовую к использованию папку dist\VkHotkeys: программа, мост Native Messaging и расширение.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
  powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -SelfContained   # не требует установленного .NET
#>
param(
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "dist\VkHotkeys"
$sc = if ($SelfContained) { "true" } else { "false" }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

Write-Host "==> Тесты" -ForegroundColor Cyan
dotnet test "$root\tests\VkHotkeys.Core.Tests" -c Release
if ($LASTEXITCODE -ne 0) { throw "Тесты не прошли" }

Write-Host "==> Публикация приложения и моста ($Runtime, self-contained=$sc)" -ForegroundColor Cyan
foreach ($proj in @("src\VkHotkeys.App\VkHotkeys.App.csproj", "src\VkHotkeys.Bridge\VkHotkeys.Bridge.csproj")) {
    dotnet publish "$root\$proj" -c Release -r $Runtime --self-contained $sc -o $out -p:DebugType=none
    if ($LASTEXITCODE -ne 0) { throw "Ошибка публикации $proj" }
}

Write-Host "==> Копирование расширения" -ForegroundColor Cyan
Copy-Item "$root\extension" "$out\extension" -Recurse

Write-Host ""
Write-Host "Готово: $out" -ForegroundColor Green
Write-Host "Дальше: запустите $out\VkHotkeys.exe и установите расширение из $out\extension (см. README)."
