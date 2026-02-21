# Run API (Windows PowerShell)
# From repo root:
#   .\scripts\run.ps1

$ErrorActionPreference = "Stop"

Write-Host "Restoring..."
dotnet restore

Write-Host "Building..."
dotnet build

Write-Host "Running API..."
dotnet run --project .\src\TimeSeriesForecast.Api\TimeSeriesForecast.Api.csproj
