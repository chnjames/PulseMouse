$ErrorActionPreference = "Stop"
Write-Host "PulseMouse Windows build + smoke tests" -ForegroundColor Cyan
dotnet restore .\PulseMouse.slnx
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build .\PulseMouse.slnx -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet run --project .\tests\PulseMouse.Core.SmokeTests -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Build and smoke tests passed. Publishing portable x64..." -ForegroundColor Green
dotnet publish .\src\PulseMouse.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish\win-x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Output: publish\win-x64\PulseMouse.exe" -ForegroundColor Green
