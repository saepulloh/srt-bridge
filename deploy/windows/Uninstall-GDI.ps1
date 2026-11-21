$ErrorActionPreference = "SilentlyContinue"

Write-Host "Cleaning up GDI SRT Embedded firewall rules and services..."

# Remove firewall rules
Remove-NetFirewallRule -DisplayName "GDI SRT Web Remote (TCP 8787)" -ErrorAction SilentlyContinue
Remove-NetFirewallRule -DisplayName "GDI SRT Media Transport (UDP 9000-9100)" -ErrorAction SilentlyContinue

Write-Host "GDI SRT Embedded uninstall cleanup finished."
