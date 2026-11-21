# PANDUAN TROUBLESHOOTING & DIAGNOSTIK — GDI SRT EMBEDDED v0.7.0

Panduan ini ditujukan bagi operator broadcast dan teknisi IT untuk menyelesaikan kendala operasional yang mungkin dihadapi saat menggunakan **GDI SRT EMBEDDED**.

---

## 1. Kendala Koneksi SRT (Koneksi Tidak Terbentuk)

### Gejala:
- Status channel tetap `STOPPED` atau `ERROR`.
- Multiview menampilkan `NO INPUT SIGNAL`.
- Log mencatat pesan timeout koneksi.

### Solusi & Pengecekan:
1. **Periksa Peran Mode (Caller vs Listener):**
   - Satu sisi harus bertindak sebagai **Listener** dan sisi lainnya sebagai **Caller**.
   - Jika kedua perangkat diset sebagai *Caller* atau keduanya sebagai *Listener*, koneksi tidak akan pernah terjadi.
2. **Periksa Port UDP dan Firewall:**
   - Protokol SRT menggunakan transport **UDP**, bukan TCP.
   - Pastikan port media (misal UDP 9000) telah dibuka pada router/firewall kedua belah pihak.
   - Pada Windows, pastikan profil jaringan diset ke *Private* atau aturan firewall Windows mengizinkan UDP pada port terkait.
3. **Pencocokan Passphrase Enkripsi:**
   - Jika enkripsi diaktifkan, pastikan *passphrase* pada sisi pengirim dan penerima sama persis.
   - Panjang kunci (`pbkeylen`) harus identik (16 untuk AES-128, 24 untuk AES-192, 32 untuk AES-256).
   - Panjang passphrase yang sah adalah 10 hingga 79 karakter.
4. **Pengaturan Latensi (SRT Latency Buffer):**
   - Untuk transmisi internet publik, latensi disarankan minimal `2.5 × RTT (Round Trip Time)` atau default `250 ms`. Jika RTT jaringan adalah 80 ms, latensi SRT minimal yang disarankan adalah 200 ms.

---

## 2. Kendala Hardware Encoder GPU (NVENC / QuickSync / AMF)

### Gejala:
- Channel gagal mulai dengan pesan `No compatible h264 encoder found`.
- GPU tidak terdeteksi pada dashboard sistem.

### Solusi:
1. **Periksa Driver GPU:**
   - Untuk NVIDIA: pastikan driver Game Ready atau Studio terbaru terpasang. Jalankan `nvidia-smi` pada terminal untuk memverifikasi.
   - Untuk Intel QuickSync: pastikan driver Intel Graphics terpasang dan iGPU diaktifkan pada BIOS.
2. **Batasan Sesi Simultan (NVENC Session Limit):**
   - Kartu grafis consumer (GeForce) memiliki batas sesi encoding hardware simultan (misal 3 atau 5 sesi tergantung versi driver).
   - Jika kuota sesi habis, sistem akan menolak memulai channel tambahan dengan NVENC.
   - **Solusi:** Ubah pengaturan encoder pada channel ke **CPU (x264)** atau gunakan kartu workstation (Quadro / RTX A-series).
3. **Fallback Otomatis:**
   - Jika encoder diset ke `Auto`, sistem secara cerdas akan memilih hardware encoder yang tersedia, dan otomatis beralih ke CPU Software jika GPU tidak tersedia.

---

## 3. Frame Drop dan Video Patah-Patah

### Gejala:
- Penghitung *Dropped Frames* pada Multiview meningkat secara terus menerus.
- Video terlihat tersendat (*stuttering*).

### Solusi:
1. **Bandwidth Jaringan Tidak Mencukupi:**
   - Periksa tab **Network** pada aplikasi. Pastikan aggregate bitrate seluruh channel tidak melebihi kapasitas upload/download koneksi.
   - Berikan headroom bandwidth minimal 30% di atas aggregate bitrate video.
2. **Kinerja CPU Terlalu Tinggi (CPU Saturation):**
   - Jika menggunakan CPU encoder (x264), pastikan preset diset ke `veryfast` atau `ultrafast`.
   - Turunkan frame rate dari 60 fps ke 30 fps atau resolusi dari 1080p ke 720p jika utilisasi CPU mendekati 90%.

---

## 4. Validasi Konflik Port Hardware I/O

### Gejala:
- Dialog validasi menampilkan pesan `Physical input port conflict` atau `Port does not support video input`.

### Solusi:
1. Buka tab **Hardware I/O**.
2. Periksa baris penugasan port:
   - Satu port fisik SDI/HDMI tidak boleh ditugaskan ke lebih dari satu sumber input channel secara bersamaan.
   - Port output (misal Port Playback) tidak boleh digunakan untuk input capture.
3. Ubah penugasan ke port yang berbeda atau kosong, lalu klik **Validate & Save Routing**.

---

## 5. Kendala Web Remote & Otentikasi API Token

### Gejala 1: `Connection failed: Failed to fetch`
- **Penyebab:** Background service `GDI.Node` belum berjalan pada port `8787` ketika halaman Web Remote dibuka di browser.
- **Solusi:**
  1. Pastikan aplikasi Desktop (`GDI.Desktop.exe`) sedang berjalan (aplikasi desktop secara otomatis memicu background node service).
  2. Atau jalankan `GDI.Node.exe` secara langsung melalui Start Menu / shortcut atau terminal.
  3. Periksa apakah port `8787` mendengarkan dengan perintah:
     ```powershell
     curl.exe http://localhost:8787/api/health
     ```
  4. Jika background service telah berjalan, refresh halaman browser (`Ctrl + F5`) untuk memuat antarmuka web terbaru.

### Gejala 2: `Unauthorized: Invalid API token`
- **Penyebab:** Token yang dimasukkan pada form login tidak cocok dengan token yang tersimpan di Windows DPAPI vault node.
- **Solusi:**
  1. Masukkan token default instalasi: `GDI-SRT-2026`.
  2. Jika Anda telah mengubah token di tab Settings atau melalui variabel lingkungan `GDI_API_TOKEN`, gunakan token kustom tersebut.
  3. Token dapat direset kapan saja dengan mengeset variabel lingkungan `GDI_API_TOKEN` atau mengupdate setting di aplikasi.
