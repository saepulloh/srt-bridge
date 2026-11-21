$sourceDir = "c:\Users\ARGAPURA NEW\Pictures\GDI"
$zipPath = "c:\Users\ARGAPURA NEW\Pictures\GDI\artifacts\GDI-SRT-Embedded-Native-v1.0.0-Source.zip"

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

$tempDir = Join-Path $env:TEMP ("gdi_src_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

$itemsToCopy = @("src", "tests", "deploy", "docs", "scripts", "GDI-SRT-Embedded.sln", "README.md", ".gitignore")
foreach ($item in $itemsToCopy) {
    $srcItem = Join-Path $sourceDir $item
    if (Test-Path $srcItem) {
        $dest = Join-Path $tempDir $item
        Copy-Item -Path $srcItem -Destination $dest -Recurse -Force
    }
}

# Clean out bin and obj from tempDir
Get-ChildItem -Path $tempDir -Include "bin","obj",".vs" -Recurse -Directory -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Compress-Archive -Path "$tempDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Source package created successfully at: $zipPath"
$file = Get-Item $zipPath
Write-Host "Size: $([math]::Round($file.Length / 1MB, 2)) MB"
