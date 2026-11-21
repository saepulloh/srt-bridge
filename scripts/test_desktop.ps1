$exe = Resolve-Path "artifacts\GDI-SRT-Embedded-Windows-x64\GDI.Desktop.exe"
$proc = Start-Process -FilePath $exe -PassThru
for ($i = 0; $i -lt 6; $i++) {
    Start-Sleep -Seconds 1
    $proc.Refresh()
    Write-Host "Sec $i : HasExited=$($proc.HasExited), Title=$($proc.MainWindowTitle), Handle=$($proc.MainWindowHandle)"
}
