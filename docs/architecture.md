# Architecture

LivePaper has one daemon and one renderer process per active output.

The daemon owns system-wide integrations, tracks outputs, and restarts failed renderers. A renderer owns exactly one direct Wayland layer-shell surface and one WPE WebKit view. Wallpaper code never runs in the daemon.

Both executables are published with .NET Native AOT. The initial distribution target is Linux x64 on the development machine's compositor and distribution. Native library loading and packaging are allowed to be target-specific; broader compositor and architecture support comes after the first backend works end to end.

## Visibility and rendering

Keyboard focus is not a useful proxy for wallpaper visibility. The platform backend combines compositor-specific signals into a small state sent to each renderer:

- `Visible`: render normally.
- `PartiallyCovered`: reserved for backends that can report partial occlusion reliably.
- `FullyCovered`: one or more windows collectively cover the whole output.
- `OutputDisabled`: the monitor is no longer active.
- `SessionLocked`: the desktop session is locked.

Every update includes independent `shouldRender` and `shouldMute` decisions.
The direct WPE renderer withholds frame completion while rendering is disabled,
so pages do not need to cooperate. Muting applies to media elements without
stopping visual rendering. Compositors may suppress frame callbacks for covered
surfaces, but LivePaper does not treat that behavior as its only visibility
signal.

The KWin backend emits `Visible`, `PartiallyCovered`, and `FullyCovered`.
Active fullscreen and fully maximized windows count as fully covered; other
visible windows intersecting the output count as partial coverage. The daemon
maps those states through the configured rendering and muting policies.

The exact signal source belongs in `LivePaper.Platform`. It can differ between wlroots compositors and KDE without leaking compositor details into the protocol or wallpaper API.
