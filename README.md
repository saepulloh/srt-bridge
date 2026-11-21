# GDI SRT EMBEDDED — Versi 0.7.0

**GDI SRT EMBEDDED** adalah aplikasi broadcast encoder dan decoder profesional berbasis protokol **SRT (Secure Reliable Transport)** yang dikembangkan oleh **Global Data Indonesia (GDI)**. Software ini dirancang khusus untuk mengubah komputer workstation standar (Windows 10/11 x64 dan Ubuntu 22.04+ LTS) menjadi perangkat broadcast encoder/decoder andal dengan latensi sangat rendah (*sub-second ultra-low latency*).

---

## 🌟 Fitur Utama (Baseline 0.7.0)

1. **Native Desktop Application (Windows)**
   - Dibangun dengan WPF (.NET 8 LTS) dengan tema broadcast modern (obsidian dark, VU meter, tally border indicator).
   - Bukan sekadar browser wrapper/Electron; antarmuka bekerja langsung dengan engine proses lokal.
2. **Web Remote Management (LAN / VPN)**
   - Kontrol jarak jauh responsif melalui peramban web pada port TCP 8787.
   - Autentikasi aman dengan **API Token** (`X-GDI-Token`) dengan verifikasi *constant-time* untuk mencegah serangan timing.
3. **Multi-Channel SRT Engine**
   - Mendukung channel encoder dan decoder simultan (target baseline 4 channel 1080p60, dapat ditingkatkan hingga 32 channel).
   - Isolasi kegagalan (*fault isolation*): Jika 1 channel terputus atau gagal, channel lainnya tetap berjalan tanpa henti.
   - Mode fleksibel: **SRT Caller** dan **SRT Listener** pada masing-masing channel.
   - Enkripsi AES-128, AES-192, dan AES-256 (passphrase 10–79 karakter).
   - Parameter lengkap: Latency buffer, Stream ID, connection timeout, dan auto-reconnect interval.
4. **Hardware Encoding & Automatic Fallback**
   - Deteksi akselerasi GPU: **NVIDIA NVENC** (`h264_nvenc`, `hevc_nvenc`), **Intel Quick Sync Video** (`h264_qsv`, `hevc_qsv`), dan **AMD AMF** (`h264_amf`, `hevc_amf`).
   - Fallback otomatis ke **CPU Software Encoding** (`libx264`, `libx265`) jika GPU tidak tersedia.
   - Profil broadcast low-latency (tune `zerolatency`/`ll`, 0 B-frames, CBR).
5. **SRT Decoder / Receiver**
   - Penerimaan stream SRT dari perangkat luar (Caller atau Listener).
   - Output ke monitor lokal, FFplay fullscreen, atau routing ke perangkat playout SDI/HDMI.
   - Monitoring real-time: bitrate aktual, FPS, drop frames, RTT, packet loss, dan audio peak meter.
6. **Broadcast Multiview Engine**
   - Layout fleksibel: **1 View**, **2 View**, **4 View (2x2 Quad)**, **6 View (1+5)**, **8 View (2+6)**, **9 View (3x3)**, dan **16 View (4x4)**.
   - Tile monitoring interaktif dengan *Tally indicator* (Program merah, Live kuning, Off abu-abu).
7. **Program Return Pipeline (Studio ↔ Venue)**
   - Workflow terdedikasi dua arah antara Ruang Kontrol Studio (BOD/Master Control) dan Tim Lapangan (Venue).
   - Mode Studio Send (menangkap program output switcher dan mengirim via SRT).
   - Mode Venue Receive (menerima SRT dan menampilkan fullscreen pada monitor venue).
8. **Unified Hardware I/O Architecture & Assignment**
   - Abstraksi vendor terpadu untuk **Blackmagic Design DeckLink**, **AJA Video Systems NTV2**, **Magewell Pro Capture**, dan **DirectShow/V4L2**.
   - Matriks penugasan port fisik manual dengan sistem validasi konflik port (`PortConflictValidator`):
     - Memastikan arah port valid (port input hanya untuk input, port output hanya untuk output).
     - Mencegah konflik perebutan port fisik yang sama oleh dua channel berbeda.
9. **Network Manager & Throughput Monitoring**
   - Deteksi kartu jaringan (NIC), link speed, alamat IP, Subnet Mask, Gateway, DNS, dan MAC address.
   - Perhitungan throughput real-time RX dan TX dalam Mbps.
   - Kebijakan aman: tidak mengubah konfigurasi adapter Windows tanpa izin operator.
10. **Sistem Keamanan & Secrets Protection**
    - Enkripsi passphrase SRT dan API Token menggunakan **Windows DPAPI (Data Protection API)** mesin lokal.
    - Redaksi otomatis (*secret redactor*): Passphrase dan API token tidak akan pernah tercetak dalam log atau respon API terbuka.
11. **Auto Start & Embedded Appliance Mode**
    - Berjalan otomatis saat komputer dinyalakan (Windows Startup / Linux systemd service).
    - Watchdog background engine melakukan auto-transmit dan auto-recovery mandiri.

---

## 🏛️ Arsitektur Solusi

Solusi terstruktur secara modular dalam `GDI-SRT-Embedded.sln` (.NET 8 LTS):

```
GDI-SRT-Embedded/
├── src/
│   ├── GDI.Core/           # Model data, DPAPI vault, secret redactor, event bus, storage
│   ├── GDI.Hardware/       # Unified Hardware Architecture, SDK adapter, port validator
│   ├── GDI.SRT.Engine/     # SRT URL builder, socket parameter, metrics parser
│   ├── GDI.Encoder/        # NVENC/QSV/AMF/CPU encoder pipeline, probe hardware
│   ├── GDI.Decoder/        # SRT receiver pipeline, FFplay preview, telemetri stream
│   ├── GDI.Multiview/      # Multiview compositor (1, 2, 4, 6, 8, 9, 16 view)
│   ├── GDI.ProgramReturn/  # Studio Send & Venue Receive pipeline
│   ├── GDI.Network/        # Adapter enumeration & real-time RX/TX throughput
│   ├── GDI.Node/           # Background daemon, REST API, WebSockets, Web Remote host
│   ├── GDI.Desktop/        # Native Windows Desktop Application (WPF dark broadcast UI)
│   └── GDI.WebRemote/      # Antarmuka web responsif di wwwroot
├── hardware/               # Referensi resmi C++ COM SDK Blackmagic, AJA, Magewell
├── deploy/
│   ├── windows/            # Installer Inno Setup (.iss) & skrip PowerShell
│   └── ubuntu/             # Unit systemd & skrip instalasi Ubuntu 22.04+
├── tests/                  # xUnit test suite pengujian komponen
├── docs/                   # Panduan instalasi, troubleshooting, dan laporan pengujian
├── README.md               # Dokumentasi utama proyek
└── CHANGELOG.md            # Riwayat versi pengembangan
```

---

## 💻 Persyaratan Sistem

| Spesifikasi | Minimum (Light / 1-2 Channel) | Recommended (4 Channel 1080p60 Broadcast) |
|---|---|---|
| **CPU** | 4-Core / 8-Thread 64-bit | 8-Core / 16-Thread (Intel Core i7/i9 atau AMD Ryzen 7/9) |
| **RAM** | 8 GB DDR4 | 16 GB - 32 GB DDR4/DDR5 |
| **GPU** | Opsional (CPU encoding) | NVIDIA GeForce GTX 1660 / RTX 3060+ (NVENC) atau Intel Arc / Iris Xe |
| **Penyimpanan** | SSD SATA 10 GB free | NVMe M.2 SSD 50 GB free |
| **Jaringan** | 1 Gbps Gigabit Ethernet | Dedicated 1 Gbps / 2.5 Gbps Ethernet / Fiber |
| **Sistem Operasi** | Windows 10/11 x64 (Build 19045+) | Windows 11 64-bit / Ubuntu 22.04+ LTS 64-bit |

---

## 🚀 Cara Menjalankan

### Melalui Installer Windows (Direkomendasikan)
1. Jalankan installer `artifacts/GDI-SRT-Embedded-v0.7.0-Windows-x64-Setup.exe`.
2. Ikuti instruksi wizard (installer mengonfigurasi direktori `%ProgramData%\GDI\SRT Embedded`, firewall, dan shortcut).
3. Buka **GDI SRT Embedded** dari Start Menu atau Desktop.
4. Akses Web Remote melalui peramban di `http://localhost:8787`.

### Menjalankan dari Source Code (.NET 8 SDK)
```powershell
# Build seluruh solution
dotnet build GDI-SRT-Embedded.sln

# Menjalankan Native Desktop Application
dotnet run --project src/GDI.Desktop/GDI.Desktop.csproj

# Menjalankan Background Node / Web Remote Service
dotnet run --project src/GDI.Node/GDI.Node.csproj
```

---

## 🔒 Lisensi & Pengembang

Dikembangkan oleh **Global Data Indonesia (GDI)**.
Hak Cipta &copy; 2026 PT Global Data Indonesia. Seluruh hak cipta dilindungi undang-undang.
