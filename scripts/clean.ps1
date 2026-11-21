$files = Get-ChildItem -Path "src\GDI.Desktop" -Recurse -Filter "*.xaml"
foreach ($f in $files) {
    $content = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
    $newContent = [System.Text.RegularExpressions.Regex]::Replace($content, '\s*LetterSpacing="[^"]*"', '')
    if ($content -ne $newContent) {
        [System.IO.File]::WriteAllText($f.FullName, $newContent, [System.Text.Encoding]::UTF8)
        Write-Host "Updated $($f.Name)"
    }
}
