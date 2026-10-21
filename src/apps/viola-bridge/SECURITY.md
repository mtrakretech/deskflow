# Security model — Viola File Bridge 0.2.0

## Pairing and transport

Each first launch generates a random RSA-2048 pairing certificate through .NET's CertificateRequest, signed with SHA-256. A pair shares that certificate and private key by explicit password-protected PFX (`.vfbpair`) export/import. This is a two-device shared credential, not a public PKI identity. Both instances prove possession of the credential through TLS 1.2 client/server certificate authentication. Certificate callbacks compare SHA-256 of the full pinned certificate and enforce validity dates. Unrelated self-signed or publicly trusted certificates are refused. Certificate chain/name errors are expected for this explicit private pin; they are not accepted without a matching pin. The shared certificate is valid for ten years; re-pair before expiry.

Discovery uses HMAC-SHA-256. Its key is derived by hashing a deterministic RSA PKCS#1 SHA-256 signature of a fixed protocol-domain string with the private pairing key. A public certificate alone cannot derive it. Discovery packets carry node ID, protocol tag, fixed transfer port, timestamp and random nonce. Timestamp tolerance is 60 seconds; seen nonces are bounded and expire after two minutes. Discovery is never sufficient to receive files: a mutual TLS connection must also authenticate. Keep both Windows clocks reasonably accurate.

Content encryption and record replay protection are provided by Schannel/SslStream, not handwritten cryptography. TLS 1.0/1.1 are excluded by selecting TLS 1.2 for Windows 10 interoperability. Windows controls cipher suite selection. TLS 1.3 and application AES-GCM are not required by this implementation. A SHA-256 digest and 64-bit length are checked for each file in addition to TLS integrity.

The installed credential is a DPAPI CurrentUser blob. Plaintext exported private-key bytes exist transiently in process memory; managed copies are not guaranteed to be zeroed. Pairing exports are password-protected PFX and must use a strong password communicated separately. The Windows PFX export algorithm is platform-controlled; do not treat a weak password as safe. Delete exports after pairing. A compromised Windows user or paired PC can impersonate either endpoint, read the cache, and send arbitrary files into staging. This is not a sandbox against local malware. Re-pair both PCs after a credential leak; changing an export password does not revoke old exports.

## Network boundary

TCP 48751 binds only to eligible IPv4 addresses on physical Ethernet/Wi-Fi adapters whose Windows network category is Private. VPN and common virtual adapter descriptions are excluded. Accepted source IPs must be on an eligible local subnet. Adapter eligibility refreshes every 30 seconds. UDP 48752 uses a wildcard socket because Windows broadcast reception requires it, but validates the ingress interface using IP_PKTINFO, the source subnet, HMAC, timestamp and nonce before opening a peer connection. Broadcasts are sent explicitly through eligible adapters only. Firewall rules constrain both ports to the exact executable, Private profile and LocalSubnet.

No rule disables Windows Firewall. No UPnP, port forwarding, Deskflow changes, Internet service or hostname-based trust is used. Do not manually forward these ports or weaken the firewall. Settings permits an IPv4 fallback only on a currently eligible subnet, followed by mutual TLS. An unauthenticated connection may use bounded TLS resources but cannot create cache files or publish clipboard data. At most eight tracked connections are accepted; stream operations time out after 30 seconds. The app is intended for exactly two paired PCs, not a multiuser distribution service.

## Files, clipboard and retention

Only local filesystem selections with FileDrop/CF_HDROP are sent. UNC sources and the application's own data directory are excluded. Text is not sent by Viola. Reparse source paths are refused during enumeration; directories and filenames are bounded, and receiver paths reject traversal, drive/UNC injection, alternate data streams, empty components, reserved DOS devices, trailing spaces/dots, duplicate entries and reparse ancestors. Manifest limits: 100,000 entries, 4,096 roots, 128 nested source levels, and 32 KiB UTF-8 control strings. Lengths use checked 64-bit totals. Free-space checks are advisory; a full disk fails the transfer safely.

Each receive creates a fresh unpredictable GUID staging directory and files with CreateNew. Completed verified directories are atomically renamed before clipboard publication. No remote destination path is accepted. Explorer handles user-requested paste and destination overwrite decisions. CUT is downgraded to COPY and original source files are never deleted by Viola.

Cache cleanup deletes only direct GUID-named children of the owned cache root, refuses reparse entries, and skips objects referenced by the clipboard or latest receive. Active transfers suppress cleanup. Windows clipboard history or another program may retain older references that the app cannot discover, so those old entries can become invalid after retention expires. Long paths depend on Windows policy, filesystem and Explorer support as well as the application's longPathAware manifest; no OS-wide long-path policy is changed.

Logs record errors, peer IPs, counts and protocol state. They never intentionally record pairing passwords, raw keys, file contents or full clipboard text. Exceptions may include source paths and filenames; logs are local-user diagnostic data. The installer and firewall helper have their own logs. Builds are currently unsigned; no external security audit or physical two-PC acceptance has been completed by automated local testing.

Report suspected issues privately to the maintainer rather than publishing pairing files, passwords, DPAPI blobs or sensitive logs.

## 0.3.0 clipboard history and setup additions

Text, HTML, RTF, PNG image data and file-reference history are encrypted individually using DPAPI CurrentUser. History files contain no plaintext previews/index. Source clipboard formats ExcludeClipboardContentFromMonitorProcessing and CanIncludeInClipboardHistory=0 skip recording. These advisory formats do not identify every password or secret: users can pause recording or clear saved data. Logs never include history content. Explicit Save as exports plaintext text/PNG only to a user-selected path.

History is bounded to 100 items and 64 MiB with seven-day unpinned retention. Images are capped at 20 million pixels and 16 MiB encoded PNG; background capture queue is bounded. Hash-based deduplication uses the first 128 bits of SHA-256. Pin state is non-sensitive metadata stored separately; hard storage limits override pins. Corrupted DPAPI entries are skipped on load. File references are not archived copies and may expire with the transfer cache.

Local history/settings commands use a named pipe whose ACL permits only the current Windows user SID; it accepts only fixed UI commands, never serialized objects, paths or remote content. No local command channel allows a pairing credential export without the user choosing a file and password.

Add another PC creates a private ZIP containing an installer and password-protected PFX pairing credential, not a plaintext password. Treat that ZIP as private, share the password separately, and delete it after use. The installer confirms the fingerprint, backs up existing DPAPI configuration, and imports with CurrentUser scope. Anyone possessing the file and its password can join this shared-credential pair. The existing two-peer model is retained, without per-device revocation.

The network helper shows connected networks and asks the user to select and explicitly trust one before marking it Private. It does not silently reclassify networks. ZIP payload extraction validates all paths before writing and rejects traversal, duplicate entries and reparse destinations. Full installs are per-user and do not replace upstream Deskflow configuration or stop unrelated Deskflow installations.
