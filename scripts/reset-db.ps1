# Delete local SQLite DB (dev)
$ErrorActionPreference = "Stop"

$paths = @(
  ".\src\TimeSeriesForecast.Api\tsf_master.db",
  ".\tsf_master.db"
)

foreach ($p in $paths) {
  if (Test-Path $p) {
    Remove-Item $p -Force
    Write-Host "Deleted $p"
  }
}
