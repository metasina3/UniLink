# Network behavior — 1.2.1

WindowsNetworkMonitor uses GetIfEntry2, requiring a driver-reported hardware interface (Wi-Fi 71 or Ethernet 6), no filter flag, no tunnel, operational Up and a valid interface index. The native GUID must match the managed adapter GUID. It does not infer physical status from adapter names. Hardware classification is supplied by Windows/the driver; in a virtual machine, the guest's base NIC can be presented as hardware. Software-only host VPN/TAP/vEthernet adapters that do not meet these properties are excluded.

The app's portal factory rechecks the selected physical adapter before each new connection. Each socket binds its local IPv4 and sets IP_UNICAST_IF in network byte order; zero/invalid indices are rejected on Windows. There is no fallback to an unbound socket, VPN adapter or default route. Existing sessions are discarded on address/interface changes. TLS pinning and redirect rejection still apply.

System HTTP proxy settings are ignored. A numeric IPv4 portal is used directly without DNS. Windows DNS requests for hostname portals set DNS_QUERY_REQUEST.InterfaceIndex to the same base interface and only request A records. One bounded worker owns synchronous native DNS; request cancellation stops waiting while that worker safely finishes and frees its native result. The worker gate is released only after native completion. There is no fallback to an unscoped DNS query on Windows.

Interface binding selects the outbound interface; it does not grant an exception to Windows Filtering Platform or a VPN's kill switch. If that VPN blocks private/LAN traffic at the firewall, enable its Allow LAN option or an appropriate local-network exception. The application makes no global route/proxy/firewall changes and does not require elevation for these operations.

## Exhausted quota

The university message below is a built-in negative-reply classification, independent of saved phrase-list versions:

```
Your data transfer has been exceeded, Please contact the administrator
```

It applies to negative login and negative keepalive replies. The account is saved as exhausted for the current university-time-zone Gregorian month, and the next eligible account is tried sequentially (normally after 3 seconds). A real successful status still wins over message text; transport failures remain transport failures. Monthly reset and delayed day-one probes are unchanged.

## Verification

See test-results.txt. Linux verification includes real HTTPS requests from the selected source IP with a deliberately unusable system proxy, no credential submission after adapter validation fails, exact-message classification, persisted quota exclusions, sequential login fallback and keepalive fallback. Native structure sizes/offsets and DNS record decoding are checked. The 46 Linux tests passed. Three Windows-only tests additionally inspect the live adapter API, IP_UNICAST_IF readback and zero-index refusal. All 49 tests, including those guarded Windows cases, passed on Windows Server 2025 in [CI](https://github.com/metasina3/UniLink/actions/runs/39115228489). See windows-test-results.txt.

References: [MIB_IF_ROW2](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_if_row2), [GetIfEntry2](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-getifentry2), [IP_UNICAST_IF](https://learn.microsoft.com/en-us/windows/win32/winsock/ipproto-ip-socket-options), [DNS_QUERY_REQUEST](https://learn.microsoft.com/en-us/windows/win32/api/windns/ns-windns-dns_query_request).

The optional Open login page action launches your default browser, whose routing/proxy behavior belongs to that browser. It is not used by automatic login or keepalive.

Verification: 46 tests passed in the earlier Linux run; 49 tests passed in native Windows CI. Live campus DNS and real VPN firewall behavior remain field checks.
