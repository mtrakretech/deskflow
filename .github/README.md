<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://github.com/deskflow/deskflow-artwork/blob/main/logo/deskflow-logo-dark-200px.png?raw=true">
  <source media="(prefers-color-scheme: light)" srcset="https://github.com/deskflow/deskflow-artwork/blob/main/logo/deskflow-logo-light-200px.png?raw=true">
  <img alt="Deskflow" width="200" src="https://github.com/deskflow/deskflow-artwork/blob/main/logo/deskflow-logo-light-200px.png?raw=true">
</picture>

# Deskflow + Viola

**One keyboard. One mouse. A clipboard that goes further.**

Share control across your computers, copy files between them,
and find saved text and screenshots in a familiar Deskflow interface.

[![Windows build](https://github.com/mtrakretech/deskflow/actions/workflows/viola-windows.yml/badge.svg?branch=viola%2Fclipboard-and-easy-setup)](https://github.com/mtrakretech/deskflow/actions/workflows/viola-windows.yml?query=branch%3Aviola%2Fclipboard-and-easy-setup)
[![Platform](https://img.shields.io/badge/Viola-Windows_10_%2F_11-0078D4?logo=windows)](../src/apps/viola-bridge/README.md)
[![Source](https://img.shields.io/badge/source-open-238636?logo=github)](https://github.com/mtrakretech/deskflow/tree/viola/clipboard-and-easy-setup)

[Download portable / installer](https://github.com/mtrakretech/deskflow/releases) · [Setup guide](../src/apps/viola-bridge/README.md) · [Security](../src/apps/viola-bridge/SECURITY.md) · [Upstream Deskflow](https://github.com/deskflow/deskflow)

</div>

> [!NOTE]
> This is an independent Windows-focused fork. Development is on `viola/clipboard-and-easy-setup` in [pull request #1](https://github.com/mtrakretech/deskflow/pull/1). Successful Windows builds publish preview packages to [GitHub Releases](https://github.com/mtrakretech/deskflow/releases).

## Download and run

Open [GitHub Releases](https://github.com/mtrakretech/deskflow/releases) and choose the newest **Deskflow + Viola Windows** preview.

| Download | How to use it |
| :--- | :--- |
| **Deskflow-Viola-0.3.0-Windows-x64-Portable.zip** | **Recommended:** extract the entire folder, then double-click **deskflow.exe**. Includes the companion, Qt, OpenSSL and Visual C++ runtime DLLs. No installer or Windows service needed. |
| **Deskflow-Viola-0.3.0-Windows-x64-Setup.exe** | Run the EXE for a normal per-user installation with shortcuts and uninstall support. |
| **Deskflow-Viola-0.3.0-Source.zip** | Corresponding source for building or inspecting this fork. |
| **SHA256SUMS.txt** | Checksums for the portable, setup and source downloads. |

Before switching versions, exit the existing Deskflow and Viola tray apps. Keep all extracted files beside **deskflow.exe** and run it from a writable folder. Configure the normal server/client role and screen arrangement on both PCs, then open **Clipboard → File sharing and pairing**.

Portable Deskflow settings stay beside the EXE under `settings`. Viola's protected pairing, history and received cache stay in the current user's `%LOCALAPPDATA%\ViolaFileBridge`; existing pairing is reused. Optional trusted-network/firewall setup requests administrator approval. These are Windows x64 previews.

## Your desk, connected

| Feature | What you can do |
| :--- | :--- |
| **Shared keyboard and mouse** | Move between computers using Deskflow's existing screen layout and controls. |
| **Text and screenshots** | Use Deskflow's text, HTML and bitmap clipboard sharing. |
| **Files and folders** | Copy in Explorer, wait for **Ready to paste** on the other PC, then paste. Transfers stream over authenticated TLS and verify each file with SHA-256. |
| **Saved clipboard history** | Search, preview, copy again, pin, delete, clear, or save text and screenshots. File entries reference the original files. |
| **Familiar interface** | Open history and pairing from Deskflow's **Clipboard** menu or tray. The integrated dialogs use Deskflow's Qt theme, fonts and native controls. |
| **Easier setup** | Use one installer on each PC, or create a private **Add another PC** bundle with a password-protected pairing file. |

History is encrypted for the current Windows user and saved **on each PC separately**. Deskflow handles live text/image sharing; Viola handles file transfer and local history.

## Feels like Deskflow

The clipboard tools use the same Qt interface as Deskflow: familiar tabs, native controls, and the application's theme and fonts.

![Clipboard history in Deskflow, with search, saved items, text preview and clipboard actions](../docs/viola/clipboard-history.png)

<details>
<summary><strong>Screenshot previews and pairing settings</strong></summary>

![Saved screenshot preview](../docs/viola/clipboard-image.png)

![File sharing and pairing settings](../docs/viola/clipboard-pairing.png)

</details>

Screenshots use isolated sample data from the native UI test. Your history and pairing data stay on your PC.

## Start on two PCs

1. Download the portable ZIP or setup EXE from [GitHub Releases](https://github.com/mtrakretech/deskflow/releases).
2. Extract the portable ZIP and run **deskflow.exe**, or run the setup EXE, on each PC. Configure the keyboard/mouse server, client and screen arrangement in Deskflow as usual.
3. Open **Clipboard → File sharing and pairing**. Export a password-protected pairing file from one PC and import it on the other. Compare the full fingerprints, then delete the exported file.
4. Use **Configure trusted network…** on both PCs to select your trusted LAN and apply the app-specific Private firewall rules. Windows may request administrator approval for this step.
5. When the status reads **Connected**, copy a small file in Explorer. Wait for **Ready to paste** on the receiving PC, then press **Ctrl+V** in its destination folder.

**Already using Deskflow?** The smaller **Viola-0.3.0-companion-Windows** artifact adds the companion beside an existing installation. Install the full fork package for the integrated Qt interface. Existing Viola 0.2.0 pairing is preserved during an ordinary upgrade.

**Adding another computer?** Use **Add another PC…** to create a private setup ZIP. Transfer it using a trusted method, extract it on the new PC and run the included setup. Send its password separately. Keep private bundles and pairing files out of public repositories.

## Find it again

Open **Clipboard → Clipboard history** in Deskflow.

- Search saved text, screenshots and file references.
- Select an item to preview it; choose **Copy again**, then paste in your app.
- Pin useful items, save a text or PNG copy, or pause history capture.
- Clear history when you are finished with it.

The local store is bounded to **100 items / 64 MiB**. Unpinned items expire after **7 days**; pins still count toward the overall limits. Clipboard history exclusion markers are respected.

## Built to stay out of your way

- Fixed buffers and 64-bit lengths support file transfers larger than 4 GiB.
- Background file transfers preserve newer text, images or files you copied meanwhile.
- Cross-computer **Cut** is treated as **Copy**, keeping source files safe.
- Pairing credentials and history use Windows user encryption. File peers authenticate through a pinned certificate.
- File sharing uses a trusted **Private**, same-subnet IPv4 Ethernet/Wi-Fi LAN, with app-specific local-subnet firewall rules.

Current limits: **one active file peer**, full-transfer retry after interruption, and Windows-only Viola additions. Paste files after the receiving notification. macOS/Linux retain upstream Deskflow functionality. See the [feature guide](../src/apps/viola-bridge/README.md) for details.

## Build and verify

The [Windows workflow](workflows/viola-windows.yml) compiles the C++/Qt application, tests the companion, checks the native clipboard UI against isolated fixture data, and packages a complete installer and portable build. UI screenshots are uploaded separately.

For a local companion build on Windows:

```powershell
./src/apps/viola-bridge/Build.ps1 -Tests -NoClipboard
```

Add `-Large` for the transfer test above 4 GiB. Interactive clipboard tests require an isolated desktop session. For the complete toolchain, follow the [Deskflow build guide](https://github.com/deskflow/deskflow/wiki/Building) and enable `BUILD_VIOLA_BRIDGE` on Windows. Enable `BUILD_VIOLA_UI_SMOKE` to build the native dialog test.

Physical desktop/laptop file copy-paste was confirmed with the earlier bridge. New builds still need acceptance checks on each target PC, including reboot and sleep/wake behavior.

## Thanks to Deskflow

Built on [Deskflow](https://github.com/deskflow/deskflow), its contributors and the wider Synergy/Input Leap/Barrier history. The original README, community links and platform information are preserved in [UPSTREAM.md](UPSTREAM.md).

Deskflow and its Qt integration retain **GPL-2.0 with the existing OpenSSL exception**. The independent Viola companion is **MIT**. See [license texts](../LICENSES), the [companion license](../src/apps/viola-bridge/LICENSE), and per-file SPDX notices.
