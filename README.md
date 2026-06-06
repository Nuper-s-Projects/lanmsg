# LanMsg — Windows LAN Messaging

LanMsg is a replacement for the Windows `msg` command. It works on **Windows 10/11 Home, Pro, Education, and Enterprise** without Remote Desktop Services, `msg.exe`, or Pro-only features.

Authorized computers on the same local network can send encrypted popup messages with sender identity, priority, reply, and delivery status.

## Install from GitHub (one command)

Open **PowerShell** (normal window is fine) and run:

```powershell
irm https://raw.githubusercontent.com/Nuper-s-Projects/lanmsg/main/installer/install-from-github.ps1 | iex
```


2. Enter the **same LAN group code** on each PC in the setup wizard.
3. Click **Test Message**.
4. Open **Sender** from the tray icon and send messages.
