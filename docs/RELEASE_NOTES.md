# UniLink 1.2.1

Portal traffic now verifies the hardware Wi-Fi/Ethernet adapter and rechecks its GUID/index before new connections. Windows sockets stay bound to that interface and local IPv4; system proxies are disabled and hostname DNS is interface-scoped. Missing or virtual adapters cannot trigger a default-route fallback.

The university error `Your data transfer has been exceeded, Please contact the administrator` is now recognized for negative login and keepalive responses, including old saved settings. Exhausted accounts are saved and enabled backups are tried sequentially at the next short retry, normally 3 seconds.

The compact dark blue 1.2 UI and existing encrypted account/settings format are retained. Exit the old process, replace UniLink.exe in the same folder, and launch it again.

A VPN firewall/kill switch can still block LAN traffic; enable Allow LAN or an appropriate exception in that VPN when needed. The app does not modify system routes or firewall rules.

Local verification: 46 tests passed, including real HTTPS source binding/proxy exclusion and exact-message account fallback. Native Windows guarded tests and the GitHub workflow have not yet run on this Linux build host. The Windows x64 package includes its runtime and is not Authenticode-signed.
