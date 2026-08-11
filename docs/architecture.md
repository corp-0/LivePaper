# Architecture

LivePaper runs a daemon and one renderer for the active wallpaper.

The daemon picks the platform backend, watches the config and manifest, handles
audio, and restarts the renderer when it fails. The renderer loads the wallpaper
and owns its HTTP server. Wallpaper code never runs in the daemon.

## Platform backends

Compositor code belongs in `LivePaper.Platform`:

- `IPlatformBackend` reports visibility and, when available, pointer position.
- `IHostedWallpaperBackend` lets a compositor embed the renderer's local URL.
- A backend without hosted presentation uses the direct WPE layer-shell path.

KWin installs a Plasma wallpaper package and gives it the local URL. Plasma then
keeps its icons and widgets above the wallpaper. Its D-Bus calls, scripts, QML,
and cleanup code all live under `LivePaper.Platform/KWin`.

Another compositor can implement the same interfaces and register itself in
`PlatformBackendFactory`. Do not add compositor checks to the daemon, renderer,
protocol, or wallpaper API.

Nothing should depend on a developer's output size, home directory, CPU
architecture, or Linux distribution.

## Visibility

Backends report one of these states:

- `Visible`
- `PartiallyCovered`
- `FullyCovered`
- `OutputDisabled`
- `SessionLocked`

The daemon applies the configured policy and sends `shouldRender` and
`shouldMute` with every update. These are separate decisions.

KWin treats fullscreen and fully maximized windows as fully covered. Other
windows intersecting the output count as partial coverage. Other compositors
can get the same states however they need to.

Direct WPE stops completing frames when rendering is disabled. The Plasma host
freezes Chromium instead. Both paths mute media separately.

## Audio

`IAudioSpectrumSource` is the audio backend boundary. The PipeWire backend runs
`pw-record` only while the wallpaper needs audio data. It records the default
output sink, converts it into 64 frequency bands per channel, and sends 128
samples to the renderer.

If `pw-record` is missing or fails, audio reaction is disabled. The wallpaper
keeps running.
