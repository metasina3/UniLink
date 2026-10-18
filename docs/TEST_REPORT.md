# Verification report — UniLink 1.2.1

Native Windows result: **49 passed, 0 failed**; **39 WinForms views, 0 layout failures**.

Windows Server 2025 (build 26100), .NET SDK 8.0.426, x64, 96 DPI. [CI run](https://github.com/metasina3/UniLink/actions/runs/39115228489). Full core output: windows-test-results.txt. UI summary: windows-ui-results.txt.

Earlier Linux result: **46 passed, 0 failed**. Full output: test-results.txt.

Linux x64, .NET SDK 8.0.426. New regression coverage includes the exact university quota message with existing/custom phrase lists, sequential account fallback from login and keepalive, physical/virtual adapter eligibility, Windows ABI layout, scoped DNS record decoding, refusal after adapter validation fails, and real HTTPS from the selected local IPv4 while an unusable system proxy is configured. Existing protocol, TLS pinning, account order, monthly reset, state persistence, concurrency and redaction cases still pass.

All three guarded native Windows tests passed: live adapter GUID identity, IP_UNICAST_IF host/network byte-order readback and missing-index refusal. The synthetic TLS server imports its certificate through PKCS#12 to support Windows Schannel. Windows hostname DNS is covered by ABI/record decoding checks; live campus DNS, hardware filtering with real VPN adapters and physical multi-monitor DPI transitions still need field verification. CI builds the x64 executable with its runtime.

The compact 1.2 layout is retained; its previous 156 compatibility-renderer checks are documented in UI_VERIFICATION.md. The GitHub workflow ran the core tests and inspected 39 actual Windows views without layout failures. Native font measurements now set heading heights.

Earlier versions were confirmed working on Windows/UUWN by the user. No real student credentials or original HAR files are included in the source or release.
