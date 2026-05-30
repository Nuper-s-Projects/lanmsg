# LanMsg — Windows LAN Messaging

LanMsg is a production-ready replacement for the Windows `msg` command. It works on **Windows 10/11 Home, Pro, Education, and Enterprise** without Remote Desktop Services, `msg.exe`, or Pro-only features.

Authorized computers on the same local network can send encrypted popup messages with sender identity, priority, reply, and delivery status.

## Share LanMsg with others (easy)

**You (once):** build the download zip:

```powershell
cd LanMsg
powershell -ExecutionPolicy Bypass -File build-release.ps1
```

This creates **`dist/LanMsg-Setup.zip`** — upload that file anywhere (Drive, Dropbox, GitHub Releases).

**Your friends:**  
1. Download and extract `LanMsg-Setup.zip`  
2. Double-click **`Install-LanMsg.bat`**  
3. Click **Yes** (Administrator)  
4. Enter the same **LAN group code** on each PC  
5. Click **Test Message**

Re-running the installer **automatically removes the old LanMsg** and installs the new version (your group code and settings are kept).

No source code, no .NET SDK, no commands — the zip includes everything.

## Quick start (on your PC)

1. Install on both computers — either double-click **`Install-LanMsg.bat`**, or run PowerShell as Administrator:

```powershell
cd LanMsg
powershell -ExecutionPolicy Bypass -File installer\install.ps1
```

2. Enter the **same LAN group code** on each PC in the setup wizard.
3. Click **Test Message**.
4. Open **Sender** from the tray icon and send messages.

## Features

- Windows Service for always-on receive + UDP discovery
- WPF tray app for sending, popups, settings, and history
- AES-256-GCM encryption + HMAC authentication
- Shared LAN group code (PBKDF2-derived keys)
- Blocklist / allowlist, rate limiting, receive toggle
- Send to one, selected, or all discovered devices
- Manual host/IP send, priority levels, reply, copy
- SQLite message history, queued delivery when tray is offline

## Architecture

```
┌─────────────────┐     named pipes      ┌──────────────────┐
│  LanMsg.Tray    │◄────────────────────►│ LanMsg.Service   │
│  (user session) │                      │ (Windows Service)│
│  popups + UI    │                      │ TCP 9848 + UDP   │
└─────────────────┘                      │ 9847 discovery   │
                                         └────────┬─────────┘
                                                  │ LAN
                                         ┌────────▼─────────┐
                                         │  Other LanMsg PCs│
                                         └──────────────────┘
```

### LAN discovery

- UDP broadcast on port **9847** every 5 seconds
- Encrypted beacon contains device ID, display name, hostname, IP, receive status
- Stale devices expire after 30 seconds
- Duplicate hostnames shown as `Name (192.168.x.x)`

### Authentication and encryption

- Users create/join a trusted group with a shared code at setup
- Code is stored as DPAPI-protected blob + verifier hash (never plaintext on disk)
- PBKDF2 (100k iterations) derives AES + HMAC keys — same code = same keys on all PCs
- Each wire frame: HMAC-signed header + AES-GCM encrypted JSON payload
- Replay protection via nonce + 5-minute timestamp window

### Service-to-desktop popups

- Services run in Session 0 and cannot show UI
- `LanMsg.Service` validates inbound TCP messages and pushes events via named pipe
- `LanMsg.Tray` runs in the logged-in user session and shows WPF popup windows
- Messages queue in SQLite when no tray is connected; replay on connect

### Firewall

Installer adds **Private profile only** rules tied to `LanMsg.Service.exe`:

- UDP **9847** (discovery)
- TCP **9848** (messages)

## Build from source

Requirements: .NET 6 SDK + .NET 6 Desktop Runtime

```powershell
cd LanMsg
dotnet build LanMsg.sln -c Release
dotnet publish src/LanMsg.Service/LanMsg.Service.csproj -c Release -r win-x64 --self-contained false -o publish/Service
dotnet publish src/LanMsg.Tray/LanMsg.Tray.csproj -c Release -r win-x64 --self-contained false -o publish/Tray
```

### Dev run (no install)

Terminal 1:

```powershell
dotnet run --project src/LanMsg.Service/LanMsg.Service.csproj -- --console
```

Terminal 2:

```powershell
dotnet run --project src/LanMsg.Tray/LanMsg.Tray.csproj
```

## Configuration

Machine config: `%ProgramData%\LanMsg\config.json`  
User history: `%LocalAppData%\LanMsg\history.db`  
Service logs: `%ProgramData%\LanMsg\logs\service.log` (no message bodies unless debug)

See [config/appsettings.example.json](config/appsettings.example.json) for field reference.

| Setting | Description |
|---------|-------------|
| `ReceiveEnabled` | Opt-in receive toggle |
| `AllowlistOnly` | Only allow listed device IDs |
| `BlockedDeviceIds` / `BlockedIPs` | Block senders |
| `RateLimitPerSenderPerMinute` | Default 10 |
| `PlaySounds` | Priority-based notification sounds |
| `DebugLogging` | Verbose logs (avoid on shared PCs) |

## Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File installer\uninstall.ps1
```

Removes service, firewall rules, scheduled task, shortcuts, and program files. Optionally deletes config and history.

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Cannot connect to service | Check `Get-Service LanMsgService` — start if stopped |
| No devices in list | Same LAN/subnet, same group code, Private network profile |
| Invalid group key | Re-enter the same code in Settings on all PCs |
| Message not received | Tray running? Receive enabled? Check blocklist |
| Firewall blocked | `Get-NetFirewallRule -DisplayName "LanMsg*"` |
| Test message fails | Run service first; check `%ProgramData%\LanMsg\logs\service.log` |

## Security notes

- LanMsg is **opt-in** — users must install and enable receiving
- Group code is a shared secret; treat it like a Wi‑Fi password
- Messages are encrypted on the LAN but not end-to-end beyond the group key
- Use allowlist mode on sensitive networks
- Disable receiving when not needed
- Not a replacement for Active Directory, MDM, or enterprise policy tools

## Project structure

```
LanMsg/
├── LanMsg.sln
├── src/
│   ├── LanMsg.Core/       Protocol, crypto, discovery
│   ├── LanMsg.Ipc/        Named pipe contracts
│   ├── LanMsg.Service/    Windows Service
│   └── LanMsg.Tray/       WPF tray + UI
├── installer/
│   ├── install.ps1
│   ├── uninstall.ps1
│   └── LanMsg-Common.ps1
├── build-release.ps1      # Creates dist/LanMsg-Setup.zip to share
├── Install-LanMsg.bat       # Double-click installer (dev folder)
├── dist/                    # Output: LanMsg-Setup.zip
├── config/
│   └── appsettings.example.json
└── README.md
```

## License

MIT — use at your own risk on trusted networks only.
