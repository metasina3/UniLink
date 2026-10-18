# 1.2.1

- Verify hardware Wi-Fi/Ethernet flags, exclude virtual Ethernet/filter/tunnel adapters and recheck GUID/index before new connections.
- Keep local-IP binding and IP_UNICAST_IF; reject missing indices without default-route fallback.
- Scope Windows hostname DNS to the same interface; literal IPs need no DNS. Ignore the system HTTP proxy.
- Recognize the exact university data-transfer-exceeded message in negative login and keepalive replies, including old settings.
- Keep sequential account order, monthly reset and encrypted settings compatibility.
- Add regression tests, network documentation and a Windows build/release workflow.
- Retain the compact 1.2 UI.

# 1.2.0

- Removed outer scrolling from all pages and dialogs.
- A smaller 900x550 logical-pixel default window fits within the monitor working area.
- Top navigation replaces the sidebar; a single-row header appears in short windows.
- Two-column connection details, compact action rows and settings cards that fit their viewport.
- Short account form; network, response rules and certificates have separate compact tabs.
- Normal and compact layouts retain all input controls and Save/Cancel actions.
- Scrollbars in long text lists/logs appear only when content overflows. Tables fill their available width without horizontal scrollbars.
- DPI mode is configured once as PerMonitorV2; window bounds are checked after monitor or display changes.
- All core and WindowsServices.cs source remains unchanged from 1.1.0; saved accounts and startup settings remain compatible.

# 1.1.0

- Rebuilt all five pages around explicit, DPI-aware layouts.
- Dark neutral surfaces, blue accents and a blue application icon.
- Compact toolbars; reorder, unblock and template actions remain under More.
- Larger tables with stable headers, row heights and selection colors.
- Account, network and certificate dialogs with fixed action footers.
- Scrollable long forms; window size is constrained to the available screen.
- Shorter status text, uncluttered settings and keyboard navigation (Ctrl+1 through Ctrl+5).
- Removed the busy cursor while editing modal forms.
- No changes to login, heartbeat, account rotation, monthly reset, encryption or stored settings.
