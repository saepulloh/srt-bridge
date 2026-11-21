# PANDUAN INSTALASI — GDI SRT EMBEDDED v0.7.0

Dokumen ini memuat panduan langkah demi langkah untuk menginstal dan mengonfigurasi **GDI SRT EMBEDDED v0.7.0** pada platform **Windows** dan **Ubuntu Linux**.

---

## 🪟 1. Instalasi pada Windows (Windows 10 / 11 64-bit)

Aplikasi Windows didistribusikan dalam bentuk installer terintegrasi mandiri (*self-contained installer*). Pengguna **tidak perlu** menginstal .NET Runtime atau FFmpeg secara terpisah karena seluruh dependensi telah dikemas ke dalam paket instalasi.

### Langkah-langkah Instalasi:
1. Pastikan komputer target memenuhi spesifikasi minimum (Windows 10 64-bit atau Windows 11 64-bit).
2. Unduh dan jalankan file installer:
   ```
   GDI-SRT-Embedded-v0.7.0-Windows-x64-Setup.exe
   ```
3. Klik **Yes** saat diminta hak akses Administrator (UAC).
4. Pilih direktori instalasi (default: `C:\Program Files\GDI\SRT Embedded`).
5. Centang opsi:
   - `Create a desktop icon` (Membuat shortcut di Desktop).
   - `Start GDI SRT Embedded automatically when Windows starts` (Opsional jika ingin mode appliance).
6. Klik **Install**. Installer akan secara otomatis:
   - Mengekstrak binary aplikasi native desktop dan background node.
   - Menyiapkan direktori data di `%ProgramData%\GDI\SRT Embedded` dengan hak akses terbatas (hanya SYSTEM dan Administrator).
   - Mendaftarkan aturan Windows Firewall untuk port TCP 8787 (Web Remote) dan port UDP 9000-9100 (Media SRT).
7. Klik **Finish** untuk langsung membuka aplikasi.

---

## 🐧 2. Instalasi pada Ubuntu Linux (22.04+ LTS)

Untuk lingkungan server tanpa GUI atau workstation Ubuntu, GDI SRT Embedded dapat berjalan sebagai **systemd service** dengan kontrol penuh melalui Web Remote.

### Langkah-langkah Instalasi:
1. Salin folder distribusi Ubuntu ke komputer target.
2. Berikan izin eksekusi pada skrip instalasi:
   ```bash
   chmod +x deploy/ubuntu/install-ubuntu.sh
   ```
3. Jalankan skrip dengan hak akses root:
   ```bash
   sudo ./deploy/ubuntu/install-ubuntu.sh
   ```
4. Skrip akan menginstal dependensi sistem (`ffmpeg`, `libsrt`, `v4l-utils`, `libasound2`), mendaftarkan unit service `gdi-srt-node.service`, dan langsung mengaktifkannya.
5. Verifikasi status service:
   ```bash
   systemctl status gdi-srt-node.service
   ```
6. Buka browser pada komputer di jaringan yang sama dan akses:
   ```
   http://<IP_UBUNTU>:8787
   ```

---

## 🔑 3. Konfigurasi Awal & Autentikasi API Token

1. Saat pertama kali membuka Web Remote atau menghubungkan desktop ke node jarak jauh, Anda akan diminta memasukkan **API Token**.
2. Token bawaan instalasi disimpan terenkripsi di DPAPI Vault.
3. Anda dapat melihat atau memperbarui token melalui tab **Settings** pada Desktop Application, atau mengatur environment variable:
   ```powershell
   [System.Environment]::SetEnvironmentVariable("GDI_API_TOKEN", "TokenRahasiaAnda123!", "Machine")
   ```
4. Simpan token ini dengan aman. Token tidak akan ditampilkan secara terbuka dalam log sistem.
