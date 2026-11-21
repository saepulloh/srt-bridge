# CHANGELOG — GDI SRT EMBEDDED

Seluruh riwayat perkembangan produk dari konsep awal hingga baseline Versi 0.7.0.

---

## [0.7.1] — 2026-11-21 (PERBAIKAN FONT, MULTIVIEW & ENCODER PIPELINE)

### Diperbaiki & Ditingkatkan:
1. **Pemilihan Font & Tipografi Broadcast:**
   - Ditambahkan fitur **UI Font Family Picker (Pemilihan Font)** pada menu Settings Desktop dan Web Remote (pilihan: Segoe UI, Inter, Bahnschrift, Roboto, Consolas, Arial).
   - Pengaturan font tersimpan permanen pada `node-settings.json` dan diterapkan secara dinamis ke seluruh elemen antarmuka WPF via resource `MasterFontFamily`.
   - Perbaikan fallback tipografi pada `MainWindow.xaml` dan tema `Glassmorphism.xaml` agar tidak lagi memakai font bitmap default Windows.
2. **Multiview Broadcast Engine:**
   - Penambahan layout **6 View (Hexa: 1+5)** dan **8 View (Octo: 2+6)** yang sebelumnya hilang dari bar navigasi Multiview desktop dan web.
   - Perbaikan kalkulasi dimensi grid (`GridRows` dan `GridColumns`) pada `MultiviewViewModel` untuk layout 6 View (2x3) dan 8 View (2x4) agar tile tidak terpotong.
   - Penambahan meter **Audio Peak VU L/R (stereo)** real-time pada setiap tile Multiview (Desktop & Web).
   - Pengaktifan fungsi tombol **Toggle Fullscreen (F11)** pada Multiview agar langsung memperbesar jendela secara fullscreen.
   - Penambahan endpoint `/api/channels/{id}/preview` pada `GDI.Node` agar Web Remote Multiview dapat menampilkan cuplikan frame langsung.
3. **Sisi Encoder (Encoder Engine & UI):**
   - Pemisahan dan perbaikan output snapshot preview FFmpeg (Output 2) dengan parameter `-an -c:v mjpeg -q:v 5 -f image2 -update 1 -y` guna mencegah konflik audio container dan pewarisan encoder GPU yang menyebabkan crash.
   - Penambahan opsi pemilihan **Video Codec (H.264 / AVC vs H.265 / HEVC)** pada modal konfigurasi channel (Desktop & Web) serta normalisasi case-insensitive pada engine.
   - Penambahan deteksi kegagalan instan (immediate failure detection) saat startup encoder (250ms guard) sehingga error hardware/port langsung dilaporkan ke operator, bukan menampilkan status sukses semu.
   - Penanganan status proses exit non-zero menjadi `ERROR` (dengan tooltip error detail) alih-alih `STOPPED`.
   - Telemetri broadcast lengkap pada halaman Encoder: bitrate dinamis aktual, FPS real-time, dropped frame counter, dan VU meter responsif berdasarkan metrik audio nyata.

---

## [0.7.0] — 2026-11-20 (BASELINE RILIS LENGKAP)

### Ditambahkan:
- **Native Desktop Application (Windows):** Antarmuka desktop native profesional berbasis WPF (.NET 8 LTS) dengan tema dark broadcast studio (obsidian black, tally program/live border, VU meter audio).
- **Unified Hardware Architecture:** Abstraksi vendor terpadu (`IHardwareIoProvider`, `IDeviceDiscovery`, `IInputCaptureProvider`, `IOutputPlaybackProvider`, `IDeviceCapabilityProvider`) untuk Blackmagic Design DeckLink, AJA Video Systems NTV2, Magewell Pro Capture, dan DirectShow/V4L2.
- **Manual Hardware Assignment & Port Conflict Validation:** Mesin penugasan port fisik manual dengan validasi arah port (`CanInput`/`CanOutput`) dan deteksi konflik perebutan port fisik ganda (`PortConflictValidator`).
- **GDI Decoder Engine:** Penerima SRT mandiri yang mendukung mode Caller dan Listener, penayangan low-delay dengan FFplay, dan monitoring telemetri stream.
- **Multiview Compositor:** Dukungan layout 1 View, 2 View, 4 View (2x2 Quad), 6 View, 8 View, 9 View (3x3), dan 16 View (4x4) dengan indikator Tally siaran (Program, Live, Off Air).
- **Program Return Pipeline:** Workflow terdedikasi dua arah Studio Send ↔ Venue Receive dengan mode fullscreen dan seleksi monitor.
- **DPAPI Secret Vault & Secret Redaction:** Penyimpanan aman passphrase SRT dan API Token menggunakan Windows DPAPI, serta penyaringan otomatis rahasia dari log sistem dan respon API.
- **Network Manager:** Enumerasi kartu jaringan dengan perhitungan delta throughput RX dan TX real-time dalam Mbps.
- **Paket Instalasi Mandiri:** Inno Setup 7 Windows x64 self-contained installer (`GDI-SRT-Embedded-v0.7.0-Windows-x64-Setup.exe`) dan skrip systemd untuk Ubuntu 22.04+ LTS.

---

## [0.6.0] — PROGRAM RETURN PIPELINE
- Implementasi konsep awal pengiriman Program Studio ke Venue melalui SRT.
- Penambahan mode fullscreen monitor display untuk operator lapangan.

## [0.5.0] — UNIVERSAL EMBEDDED NODE
- Fondasi arsitektur multiplatform untuk Windows dan Ubuntu Linux.
- Integrasi auto-start watchdog dan mekanisme persistensi konfigurasi JSON.

## [0.4.0] — MONITORING & DASHBOARD
- Tampilan dashboard status sistem dan pemantauan kartu jaringan.
- Konsep awal Multiview monitoring operator.

## [0.3.0] — ENCODER & DECODER FOUNDATION
- Integrasi hardware encoding NVIDIA NVENC, Intel QuickSync, dan AMD AMF dengan fallback CPU.
- Monitoring bitrate, FPS, dan dropped frames.

## [0.2.0] — MULTI-CHANNEL SYSTEM
- Penambahan dukungan banyak channel streaming secara simultan.
- Pengaturan independen untuk masing-masing channel.

## [0.1.0] — INITIAL CONCEPT & PROOF OF CONCEPT
- Konsep dasar transmisi video broadcast melalui protokol SRT (Caller & Listener).
- Pengujian encode dan transmit video dari kamera lokal.
