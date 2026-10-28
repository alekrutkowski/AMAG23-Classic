# Run via: pwsh -NoProfile -File ./Start-AMAG23.ps1 -SelfTest
$ErrorActionPreference='Stop'
$results=[ClassicSelfTests]::Run()
$results | ForEach-Object {Write-Host $_ -ForegroundColor Green}
Write-Host "$($results.Count) native protocol tests passed with a simulated USB transport. No real USB access."
