# UI verification — 1.2.0

39 views were rendered and inspected at each of 96, 120, 144 and 192 logical DPI (100%, 125%, 150%, 200%): 156 views and zero layout failures. Screenshots were reviewed, including the compact 640x300 client area at 200%.

Coverage:
- All five main pages at logical client sizes 900x550, 760x420, 640x340, 640x300 and 1100x700.
- Long status and certificate-review states at regular and compact sizes.
- Add/edit account forms.
- All three network editor tabs at default and compact 560x300 client sizes.
- Certificate review at default and compact 520x300 client sizes.

Checks reject outer AutoScroll, controls outside their parent bounds, sibling overlaps, horizontal table scrollbars, clipped button captions, labels too short for one text line, and undersized table areas. Long account lists, phrase lists and logs can still scroll internally; these are variable-length data rather than oversized pages.

The rendering environment was Mono WinForms under Xvfb on Linux, using temporary compatibility adapters with in-memory sample data, old API mappings and simulated DPI/font scaling. Adapters are not part of the Windows release. This does not execute the Windows binary or verify native Windows font rasterization and monitor transitions. The Windows x64 executable was cross-compiled with its runtime.

Native Windows inspection remains available with:

```powershell
./UniLink.exe --ui-check C:\Temp\UniLink-UI
```

This offline mode uses synthetic accounts and does not read saved credentials, register startup or contact a portal. It writes screenshots and a geometry report using real Windows controls.

All 37 core tests passed. All source files in UniLink.Core, WindowsServices.cs and the core test source remain byte-for-byte unchanged from 1.1.0. User settings stay at the same path and retain their existing encryption and format.

Implementation references: [Microsoft — automatic scaling](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/forms/autoscale) and [RichTextBox scrollbars](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-display-scroll-bars-in-the-windows-forms-richtextbox-control).

Version 1.2.1 retains this layout; only the displayed version label changed. Network changes are documented in NETWORK_BEHAVIOR.md.
