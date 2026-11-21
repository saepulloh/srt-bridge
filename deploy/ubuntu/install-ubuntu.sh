#!/usr/bin/env bash
# GDI SRT EMBEDDED — Ubuntu 22.04+ LTS Installation Script
set -euo pipefail

echo "=========================================================="
echo " GDI SRT EMBEDDED — Linux Deployment Installer"
echo " Global Data Indonesia — Version 0.7.0"
echo "=========================================================="

if [ "$EUID" -ne 0 ]; then
  echo "[-] Please run as root (sudo ./install-ubuntu.sh)"
  exit 1
fi

echo "[*] Updating package repositories..."
apt-get update -y

echo "[*] Installing dependencies (FFmpeg with libsrt, ALSA, v4l-utils)..."
apt-get install -y ffmpeg libsrt1.4-openssl v4l-utils alsa-utils libasound2 curl ca-certificates

echo "[*] Creating system directories..."
mkdir -p /opt/gdi-srt-embedded
mkdir -p /etc/gdi/config
mkdir -p /etc/gdi/security
mkdir -p /var/log/gdi

chmod 700 /etc/gdi/security
chmod 755 /etc/gdi/config

echo "[*] Copying binary payload..."
cp -r ./publish/* /opt/gdi-srt-embedded/ || true
chmod +x /opt/gdi-srt-embedded/GDI.Node || true

echo "[*] Installing systemd broadcast service..."
cp ./gdi-srt-node.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable gdi-srt-node.service
systemctl restart gdi-srt-node.service

echo "[+] GDI SRT Embedded successfully installed and running!"
echo "[+] Web Remote active on: http://$(hostname -I | awk '{print $1}'):8787"
