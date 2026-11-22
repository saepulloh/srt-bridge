#!/usr/bin/env bash
# ====================================================================
#  GDI SRT EMBEDDED — Linux x64 Automated Build and Packaging Script
# ====================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
VERSION="1.0.0"
OUTPUT_DIR="${ROOT_DIR}/artifacts/linux-x64"

echo "===================================================================="
echo " GDI SRT EMBEDDED — Linux x64 Release Build (v${VERSION})"
echo "===================================================================="
echo "Workspace Root : ${ROOT_DIR}"
echo "Target Platform: linux-x64 (Self-Contained .NET 8 LTS)"

# 1. Run Unit Tests
echo "[1/4] Running automated test suite..."
dotnet test "${ROOT_DIR}/tests/GDI.Tests/GDI.Tests.csproj" -c Release

# 2. Publish Self-Contained GDI.Node
echo "[2/4] Publishing self-contained GDI.Node for linux-x64..."
mkdir -p "${OUTPUT_DIR}/publish"
dotnet publish "${ROOT_DIR}/src/GDI.Node/GDI.Node.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=false \
    -o "${OUTPUT_DIR}/publish"

# 3. Create standalone .tar.gz distribution
echo "[3/4] Creating standalone distribution tarball..."
tar -czf "${OUTPUT_DIR}/gdi-srt-embedded-linux-x64-v${VERSION}.tar.gz" -C "${OUTPUT_DIR}/publish" .
echo "  -> Created ${OUTPUT_DIR}/gdi-srt-embedded-linux-x64-v${VERSION}.tar.gz"

# 4. Optional Debian (.deb) package
if command -v dpkg-deb &>/dev/null; then
    echo "[4/4] Building Debian/Ubuntu .deb package..."
    cd "${ROOT_DIR}/deploy/ubuntu"
    cp -r "${OUTPUT_DIR}/publish" ./publish
    ./build-deb.sh
    rm -rf ./publish
    echo "  -> Debian package built successfully."
else
    echo "[4/4] dpkg-deb not found. Skipping .deb creation (tarball is ready)."
fi

echo "===================================================================="
echo " LINUX BUILD COMPLETED SUCCESSFULLY"
echo "===================================================================="
