# Changelog

## 0.3.0 — 2026-10-21
- Added the previously working Viola bridge as a Windows-only CMake companion inside the Deskflow fork.
- Added Deskflow menu/tray entry points for clipboard history and file sharing/pairing.
- Preserved upstream text/HTML/bitmap synchronization without a competing text network loop.
- Added encrypted bounded local history for text/HTML/RTF, screenshots/PNG and file references, with search/preview/pin/delete/clear/restore/export/pause.
- Added current-user-only command pipe, live Settings status and peer, and explicit trusted-network setup.
- Added private Add another PC setup bundles and automatic invitation import during install; existing v0.2.0 pairing/protocol remains compatible.
- Added companion/full Windows installers, CMake install integration, targeted Windows build workflow and history/security/installer tests.
- Existing limits: one active file peer, IPv4 same-subnet Private LAN, wait for Ready before paste, full-transfer retries rather than resume; new Deskflow role/layout chosen once in its GUI.

## 0.2.0
Working independent file bridge with mutual TLS/DPAPI, authenticated discovery, SHA-256 streamed file transfer, safe staging, clipboard listener, tray progress and installer. User reported desktop/laptop file copying working before this fork integration.
