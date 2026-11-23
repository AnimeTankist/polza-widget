# Сборка одного exe. Требуется .NET SDK 8.
$ErrorActionPreference = 'Stop'

$dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($dotnet) { $dotnet = $dotnet.Source } else { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'Не найден dotnet. Установите .NET SDK 8: winget install Microsoft.DotNet.SDK.8' }

$project = Join-Path $PSScriptRoot 'src\PolzaWidget\PolzaWidget.csproj'
& $dotnet build $project -c Release

$exe = Join-Path $PSScriptRoot 'src\PolzaWidget\bin\Release\net48\PolzaWidget.exe'
if (Test-Path $exe) {
    $size = [math]::Round((Get-Item $exe).Length / 1KB, 1)
    Write-Host ""
    Write-Host "Готово: $exe ($size КБ)" -ForegroundColor Green
}
