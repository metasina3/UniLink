# Verification report — UniLink 1.2.1

Local result: **46 passed, 0 failed**. Full output: test-results.txt.

Linux x64, .NET SDK 8.0.426. New regression coverage includes the exact university quota message with existing/custom phrase lists, sequential account fallback from login and keepalive, physical/virtual adapter eligibility, Windows ABI layout, scoped DNS record decoding, refusal after adapter validation fails, and real HTTPS from the selected local IPv4 while an unusable system proxy is configured. Existing protocol, TLS pinning, account order, monthly reset, state persistence, concurrency and redaction cases still pass.

Three guarded native Windows tests are prepared for live adapter GUID identity, IP_UNICAST_IF host/network byte-order readback and missing-index refusal. They have not run on this Linux host. Windows native DNS calls, hardware filtering with live VPN adapters and Windows GUI/DPI transitions still need native execution. The x64 executable is cross-built with its runtime.

The compact 1.2 layout is retained; its previous 156 compatibility-renderer checks are documented in UI_VERIFICATION.md. The included workflow is prepared to run core and UI checks on Windows, but has not yet run on GitHub.

Earlier versions were confirmed working on Windows/UUWN by the user. No real student credentials or original HAR files are included in the source or release.
