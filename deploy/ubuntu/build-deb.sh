#!/usr/bin/env bash
# Build Debian Package (.deb) for GDI SRT Embedded
set -euo pipefail

VERSION="1.0.0"
PKG_DIR="gdi-srt-embedded_${VERSION}_amd64"

echo "[*] Building Debian package: ${PKG_DIR}.deb"

mkdir -p "${PKG_DIR}/DEBIAN"
mkdir -p "${PKG_DIR}/opt/gdi-srt-embedded"
mkdir -p "${PKG_DIR}/lib/systemd/system"
mkdir -p "${PKG_DIR}/etc/gdi/config"

cat <<EOF > "${PKG_DIR}/DEBIAN/control"
Package: gdi-srt-embedded
Version: ${VERSION}
Section: video
Priority: optional
Architecture: amd64
Depends: ffmpeg, libasound2, v4l-utils
Maintainer: Global Data Indonesia <support@gdi.co.id>
Description: GDI SRT Embedded Professional Broadcast SRT Encoder and Decoder Appliance
 Professional broadcast video over IP appliance supporting multi-channel
 SRT Caller and Listener, low-latency GPU/CPU hardware encoding, Multiview,
 Program Return, and remote node control.
EOF

cat <<EOF > "${PKG_DIR}/DEBIAN/postinst"
#!/bin/sh
systemctl daemon-reload
systemctl enable gdi-srt-node.service
systemctl start gdi-srt-node.service
exit 0
EOF

chmod 755 "${PKG_DIR}/DEBIAN/postinst"
cp ./gdi-srt-node.service "${PKG_DIR}/lib/systemd/system/"
cp -r ../../artifacts/publish/linux-x64/* "${PKG_DIR}/opt/gdi-srt-embedded/" || true

dpkg-deb --build "${PKG_DIR}"
echo "[+] Created ${PKG_DIR}.deb successfully."
