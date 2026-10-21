# Deskflow + Viola Clipboard 0.3.0 (Windows)

This fork combines upstream Deskflow mouse/keyboard, Unicode text, HTML and bitmap clipboard sharing with Viola's authenticated streamed file/folder transfers and encrypted local clipboard history. Upstream Deskflow is GPL-2.0-only with its existing OpenSSL exception; the independent Viola companion sources are MIT. This is a Windows-only companion addition; other Deskflow platforms retain their existing behavior.

## One setup on each PC

Use **Deskflow-Viola-0.3.0-Setup.exe** from the Windows build artifact for a new PC. It includes the compiled fork, clipboard companion, startup option and firewall helper. Existing users can install **ViolaFileBridge-Setup.exe** from the companion artifact beside their current Deskflow without replacing it. No compiler, Visual Studio, Electron or separate .NET runtime download is needed on maintained Windows 10/11 with .NET Framework 4.8. The forked Deskflow uses its bundled app-local Microsoft VC++ runtime DLLs; the included Microsoft-signed runtime installer is only a fallback if those DLLs are missing. Original Deskflow installs/configurations are not removed. The per-user full bundle uses the Desktop process mode; installing the optional secure-desktop service remains a separate upstream MSI capability.

If an original Deskflow is already running, setup keeps it running and does not autostart a second Deskflow input server. Use the companion-only installer to upgrade an existing working pair. To switch to the full fork later, exit the original Deskflow and launch the bundled fork from `App\Deskflow\deskflow.exe`.

1. Install on the first PC as your normal Windows user. Startup is optional. Only the firewall helper elevates.
2. In Viola's tray choose **Add another PC**. Choose a strong password (12+ characters); save the private ZIP.
3. Transfer the ZIP through a trusted channel, extract it on the other PC, run its setup and enter the password once. Confirm the same fingerprint. The password is not stored in the ZIP.
4. In Settings, **Trust this LAN and fix firewall** lets you explicitly select the trusted home/office connection. It marks only that selected connection Private and configures narrow app rules. Cancel on untrusted/public networks.
5. Wait for **Connected**. Delete the private setup ZIP after use. Existing export/import `.vfbpair` pairing remains available for compatibility with 0.2.0.

Deskflow's host/client role and physical screen layout still need to be chosen once through its normal GUI on a new installation. Viola discovery does not depend on Deskflow screen names or Windows hostnames. A pair reconnects automatically after network changes. The current file bridge has one active peer; adding a replacement/new PC is supported, simultaneous multi-recipient file broadcast is not.

## Text, screenshots and saved clipboard items

Deskflow already synchronizes text, HTML and bitmap images. Keep its clipboard sharing enabled. Copy text or use Win+Shift+S on one PC, switch through Deskflow, paste in an appropriate application on the other PC. This uses Deskflow's existing authenticated transport and size settings; arbitrary application-specific formats are not guaranteed. Viola does not install a duplicate text/image network loop.

Open **Clipboard > Clipboard history** in the forked Deskflow menu or **Clipboard history — text and screenshots** in Viola's tray. Search saved text, preview screenshots, pin, delete, clear, copy again, or save text/PNG to disk. Rich text/HTML are preserved locally when supplied by the source. File history stores references; unavailable originals/cache entries cannot be restored. It never copies file contents into history or deletes originals. Restoring a file reference uses COPY semantics.

History is local to each PC. Each PC can save synchronized content when it reaches its own clipboard; this is not a separate synchronized history database. Items already saved before another PC joined are not automatically uploaded. Clipboard password-manager opt-out formats are respected. Pause recording from the tray at any time. DPAPI CurrentUser encrypts each saved item. Limits: 100 items, 64 MiB encrypted disk storage, seven days for unpinned items, 1 Mi characters per text format, PNG images up to 16 MiB and 20 million pixels. Pinned items remain subject to the hard count/byte limit. Capture is event-driven with a bounded background queue. The app records only clipboard changes while running, not existing clipboard contents on launch.

## Files and folders

Copy files/folders in Explorer, wait for **Ready to paste** on the receiving PC, then paste in Explorer normally. Both directions use the same path. CUT is copied and originals remain safe. Large files stage to `%LOCALAPPDATA%\ViolaFileBridge\Cache` before paste; early Ctrl+V during transfer is not implemented. Transfer uses fixed 256 KiB buffers and Int64 sizes, SHA-256 verification, three full-transfer attempts, per-transfer GUID directories, safe path validation and cleanup. Byte-offset resume is not implemented. Local reparse points, UNC selections and Viola's own data files are excluded. Explorer decides final destination collisions.

If you copy newer text, an image, or other files while files are arriving, the receiver keeps that newer clipboard selection. The verified transfer still completes into its cache/history. Use **Copy latest received files** in the receiving tray when you want to paste those files. Selecting something else while the sender is still enumerating a large folder supersedes that unsent selection. The receiver rechecks clipboard changes on each clipboard-lock retry rather than replacing newer content after a long busy period.

TCP 48751 and UDP 48752 are separate from Deskflow's default TCP 24800. Only physical Private Ethernet/Wi-Fi IPv4 adapters and same-subnet peers are eligible. VPN/virtual adapters are excluded. UDP wildcard reception validates the actual ingress interface; TCP binds eligible addresses only. Manual IP fallback still requires mutual TLS. File cache expires after 72 hours (partial: 24 hours), protecting current/latest clipboard references and active transfers.

## Build, test and package

From this directory run `./Build.ps1 -Tests -NoClipboard` in PowerShell. `-Large` includes a >4 GiB streaming and hash test. Omit `-NoClipboard` for the real clipboard/STA test when the live bridge is stopped and you are not using the clipboard. Build outputs are under ignored `out/`. The inbox .NET Framework compiler is used. The CMake target `viola-bridge` builds/installs this companion on Windows; disable with `-DBUILD_VIOLA_BRIDGE=OFF`.

The **Deskflow + Viola Windows** workflow builds the actual C++/Qt fork and creates a portable ZIP and one full setup EXE. Pass `-DeskflowPayload <CPack ZIP>` to Build.ps1 to embed that compiled fork in the installer. A build without this option is the lightweight companion installer. No pairing files, history, config, logs or private invitation bundles belong in source control or public artifacts.

## Data and removal

`%LOCALAPPDATA%\ViolaFileBridge`: `App` (programs), `pairing.dpapi` (credential), `History` (encrypted saved clips), `Cache` (received files), `Logs`, `Backups`. Existing 0.2.0 pairing is preserved on ordinary upgrade. Settings shows live status and peer, unlike the old stale Settings label.

Uninstall through Windows Installed apps > Deskflow + Viola Clipboard, or `App\Uninstall.exe --uninstall`. Exit the bundled fork first if running. Only this installed bundle, its startup values and its firewall rules are removed. Choose whether to keep pairing, history and received cache. Original Deskflow installs and personal source files remain. Debug launch: `ViolaFileBridge.exe --debug`. Commands `--history`, `--settings`, `--background`, `--exit` target the current user's companion through an ACL-restricted named pipe. `--data-root <directory>` isolates developer data.

The earlier user reported physical 0.2.0 file copy/paste working. This fork's new setup/history/UI changes require their own validation; automated local tests do not establish Windows 10 laptop, reboot, sleep/wake or all source application clipboard compatibility.
