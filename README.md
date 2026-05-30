# LanMsg — Windows LAN Messaging

LanMsg is a production-ready replacement for the Windows `msg` command. It works on **Windows 10/11 Home, Pro, Education, and Enterprise** without Remote Desktop Services, `msg.exe`, or Pro-only features.

Authorized computers on the same local network can send encrypted popup messages with sender identity, priority, reply, and delivery status.

## Install from GitHub (one command)

Open **PowerShell** (normal window is fine) and run:

```powershell
irm https://raw.githubusercontent.com/Nuper-s-Projects/lanmsg/main/installer/install-from-github.ps1 | iex
```

You will get a **UAC Administrator prompt first**, then the download and install begin.

That script will:
1. Download **LanMsg-Setup-lite.zip** (~5 MB) — fast default
2. Fall back to the full zip (~100 MB) if needed
3. Use **curl** + direct GitHub URLs (not slow `Invoke-WebRequest`)
4. Extract with **tar** (faster than Expand-Archive)
5. Install LanMsg automatically

No manual zip step. Re-running upgrades the existing install.

Lite installs .NET 6 Desktop Runtime via winget if missing.

Force full offline package (no .NET download later):

```powershell
iex "& { $(irm https://raw.githubusercontent.com/Nuper-s-Projects/lanmsg/main/installer/install-from-github.ps1) } -Package full"
```

**First time only:** On GitHub go to **Actions → Build and Release → Run workflow** to create the first release zips.

Install a specific version:

```powershell
irm https://raw.githubusercontent.com/Nuper-s-Projects/lanmsg/main/installer/install-from-github.ps1 | iex -ReleaseTag v1.0.0
```

## Share LanMsg with others (easy)

**Option A — One-liner (recommended):** Share this command:

```powershell
irm https://raw.githubusercontent.com/Nuper-s-Projects/lanmsg/main/installer/install-from-github.ps1 | iex
```

**Option B — Manual zip:** build locally with `build-release.ps1`, upload `dist/LanMsg-Setup.zip` to GitHub Releases, or share the zip file directly.

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
