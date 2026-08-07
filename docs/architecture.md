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

The built-in KWin backend emits `Visible`, `PartiallyCovered`, and `FullyCovered`.
Active fullscreen and fully maximized windows count as fully covered; other
visible windows intersecting the output count as partial coverage. The daemon
maps those states through the configured rendering and muting policies. Other
platform backends can derive the same states using compositor-specific signals.

The exact signal source belongs in `LivePaper.Platform`. It can differ between wlroots compositors and KDE without leaking compositor details into the protocol or wallpaper API.

## Audio reaction

Audio capture is exposed to the daemon through `IAudioSpectrumSource`. The
built-in PipeWire implementation starts one temporary `pw-record` stream when
the active wallpaper declares `audio_reaction`. The stream monitors the default
output sink; it does not capture a microphone or write PipeWire configuration.
LivePaper converts 48 kHz stereo PCM into 64 logarithmic spectrum bands per
channel and sends the frames to the renderer. The capture process stops with
the wallpaper or daemon. Other audio systems can implement the same source
interface without changing the protocol or wallpaper API.

Imported web wallpapers receive the capability automatically when their HTML
or JavaScript registers a Wallpaper Engine audio listener. A missing or failed
`pw-record` process disables the built-in audio backend without stopping the
renderer.
