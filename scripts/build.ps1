# Build solution (Windows PowerShell)
$ErrorActionPreference = "Stop"

dotnet restore

dotnet build -c Release
