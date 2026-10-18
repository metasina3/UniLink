# Captured protocol and implementation

The input was a nine-entry HAR captured on 2026-10-17. This document contains protocol facts only, without original credentials, request timestamps or the original HAR.

| Operation | Method/path | Form/query fields | Success response |
| --- | --- | --- | --- |
| Initial page | GET `/` | None | HTML loads `javascript/validation/httpclient.js` |
| Login | POST `login.xml` | `mode=191`, `username`, `password`, `a=<Unix ms>`, `producttype=0` | XML `requestresponse/status=LIVE` |
| Keepalive | GET `live` | `mode=192`, lower-case `username`, `a=<Unix ms>`, `producttype=0` | XML `liverequestresponse/ack=ack` |
| Logout | POST `logout.xml` | `mode=193`, `username`, `a=<Unix ms>`, `producttype=0` | Request defined in captured JavaScript; no logout exchange was present in HAR |

The browser's source sets `liveReqTimeInJS=180`. The capture has one keepalive roughly 180.8 seconds after login; it does not establish the server's exact session-expiry timeout.
The page's `onbeforeunload` handler sends logout. Browser tab closure and missing keepalives are therefore separate causes of the reported disconnects.
No cookies appeared on captured login/live traffic, but the implementation retains an endpoint-local cookie container for compatible deployments.

The captured JavaScript replaces an apostrophe in login/logout usernames with two apostrophes, and lower-cases the original username for live requests. The transport follows those operations and uses UTF-8 form/query encoding, including special characters in passwords.

Only `LIVE` and `ack` establish an authenticated state. `nack` or `login_again` on live traffic causes a same-account login retry. `live_off` stops keepalives, matching the browser behavior. `CHALLENGE` requires manual interaction; it is never treated as authentication success.

No actual quota or bad-password reply was supplied. The default English/Persian phrase lists are conservative starting points, not claims that those messages were observed in this university's HAR. Users can add the university's actual error substring under Networks → Edit → Advanced. Unknown errors use bounded backoff on the same account.

Quota failures are stored for the current Gregorian month in the configured time zone. At a later month, old quota blocks clear. Password rejections remain blocked. On day 1, a quota refusal schedules another check one hour later if that check is still on day 1, allowing for delayed reset jobs. The server's decision always controls access.

Automatic group priority is the displayed group order. An exact SSID match takes priority over portal-only groups. Portal fallback changes the endpoint on a transport failure, while retaining account selection. Disabled accounts are excluded; the default enabled account is tried before other enabled accounts in list order. With automatic fallback disabled, only the preferred/default account is used, except an explicit manual account selection.

All endpoints are HTTPS. First-use certificate approval is mandatory even for a CA-valid certificate, providing a stable identity for portal-only discovery. A matching SHA-256 certificate pin is required thereafter. An endpoint-specific mismatch halts credentials until explicit approval. No global TLS validation override is installed.

Network connections bypass HTTP proxies and redirects, bind to the detected local IPv4 address and, on Windows, set `IP_UNICAST_IF` to select the intended adapter. Each request, including response streaming, has a 12-second deadline and 256 KiB size limit. XML external entities and DTDs are disabled.

Windows SSID detection uses the SSID-only `WlanConnectionProfileDetails.GetConnectedSsid` API rather than scanning BSSIDs. Native WLAN ACM notifications and network-address events invalidate sessions. A three-second UI timer also polls network state. No keepawake API is used.

References:

- [Sophos: Web authentication](https://docs.sophos.com/nsg/sophos-firewall/21.5/Help/en-us/webhelp/onlinehelp/index.html?contextId=authentication-web)
- [Microsoft: Wi-Fi access and location API changes](https://learn.microsoft.com/en-us/windows/win32/nativewifi/wi-fi-access-location-changes)
- [Microsoft: WlanConnectionProfileDetails.GetConnectedSsid](https://learn.microsoft.com/en-us/uwp/api/windows.networking.connectivity.wlanconnectionprofiledetails.getconnectedssid)
- [Microsoft: CryptProtectData](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)
- [Microsoft: IPPROTO_IP socket options](https://learn.microsoft.com/en-us/windows/win32/winsock/ipproto-ip-socket-options)
