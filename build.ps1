param(
    [switch]$Publish,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host "Compilation de GameBoost ($Configuration)..." -ForegroundColor Cyan
dotnet build GameBoost.slnx -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "ECHEC de la compilation." -ForegroundColor Red
    exit 1
}

$exe = Join-Path $root "artifacts\build\GameBoost.App\release\GameBoost.exe"
if (-not (Test-Path $exe)) {
    $exe = Join-Path $root "src\GameBoost.App\bin\Release\net10.0-windows\GameBoost.exe"
}
Write-Host ""
Write-Host "Compilation reussie : $exe" -ForegroundColor Green

if ($Publish) {
    Write-Host ""
    Write-Host "Publication autonome dans publish\..." -ForegroundColor Cyan
    dotnet publish (Join-Path $root "src\GameBoost.App\GameBoost.App.csproj") -c $Configuration -o (Join-Path $root "publish")
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ECHEC de la publication." -ForegroundColor Red
        exit 1
    }
    Write-Host "Publication reussie : $(Join-Path $root 'publish\GameBoost.exe')" -ForegroundColor Green
}
