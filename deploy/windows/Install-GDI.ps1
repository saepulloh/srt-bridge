param(
    [string]$InstallDir = "C:\Program Files\GDI\SRT Embedded"
)

$ErrorActionPreference = "SilentlyContinue"

Write-Host "Configuring GDI SRT Embedded environment..."

# 1. Prepare ProgramData folders with strict ACLs
$dataRoot = "$env:ProgramData\GDI\SRT Embedded"
$dirs = @(
    "$dataRoot\config",
    "$dataRoot\security",
    "$dataRoot\logs"
)

foreach ($d in $dirs) {
    if (-not (Test-Path $d)) {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
    }
}

# Restrict security folder ACLs to SYSTEM and Administrators
try {
    $secPath = "$dataRoot\security"
    $acl = Get-Acl $secPath
    $acl.SetAccessRuleProtection($true, $false)
    $systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule("SYSTEM", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
    $adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule("Administrators", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.AddAccessRule($systemRule)
    $acl.AddAccessRule($adminRule)
    Set-Acl -Path $secPath -AclObject $acl
} catch {
    Write-Warning "Could not tighten ACLs on $secPath: $_"
}

# 2. Configure Windows Firewall Rule for Web Remote (Port 8787)
try {
    Remove-NetFirewallRule -DisplayName "GDI SRT Web Remote (TCP 8787)" -ErrorAction SilentlyContinue
    New-NetFirewallRule -DisplayName "GDI SRT Web Remote (TCP 8787)" `
                        -Direction Inbound `
                        -Protocol TCP `
                        -LocalPort 8787 `
                        -Action Allow `
                        -Profile Private,Domain `
                        -Description "Allows trusted LAN/VPN access to GDI SRT Embedded Web Remote interface." | Out-Null
} catch {
    Write-Warning "Firewall rule creation skipped or not permitted: $_"
}

# 3. Configure Windows Firewall Rule for SRT Media Streaming (UDP 9000-9100)
try {
    Remove-NetFirewallRule -DisplayName "GDI SRT Media Transport (UDP 9000-9100)" -ErrorAction SilentlyContinue
    New-NetFirewallRule -DisplayName "GDI SRT Media Transport (UDP 9000-9100)" `
                        -Direction Inbound `
                        -Protocol UDP `
                        -LocalPort 9000-9100 `
                        -Action Allow `
                        -Profile Private,Domain `
                        -Description "Allows incoming SRT Caller connections to GDI SRT Listeners." | Out-Null
} catch {
    Write-Warning "Firewall rule creation skipped: $_"
}

# 4. Ensure GDI Node Background Service is running
try {
    $nodeExe = Join-Path $InstallDir "GDI.Node.exe"
    if (Test-Path $nodeExe) {
        $conn = Get-NetTCPConnection -LocalPort 8787 -ErrorAction SilentlyContinue
        if (-not $conn) {
            Start-Process -FilePath $nodeExe -WorkingDirectory $InstallDir -WindowStyle Hidden
            Write-Host "Started GDI Node service on port 8787."
        }
    }
} catch {
    Write-Warning "Could not start GDI Node service: $_"
}

Write-Host "GDI SRT Embedded installation configuration complete."
