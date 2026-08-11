# Protocol

The daemon and renderer use newline-delimited JSON over a private Unix socket in
`$XDG_RUNTIME_DIR/livepaper`. Every message has a protocol version and kind. The
current version is 4.

The daemon sends visibility, pointer position, and audio spectrum updates. A
hosted renderer replies with `PresentationReady` and its loopback HTTP URL. The
daemon passes that URL to `IHostedWallpaperBackend`.

## Browser API

The renderer creates `window.livepaper` before the wallpaper runs. It is an
`EventTarget` with these values and events:

- `visibility` and `visibilitychange`
- `pointerPosition` and `pointerpositionchange`
- `audioSpectrum` and `audiospectrumchange`

Visibility contains `state`, `shouldRender`, and `shouldMute`.

## Capabilities

A wallpaper lists optional features in `manifest.toml`. Unknown or duplicate
names are rejected.

- `pointer_input` enables clicks and scrolling on the wallpaper.
- `global_pointer_tracking` reports global logical coordinates. It does not make
  the wallpaper clickable. Add `pointer_input` for that.
- `audio_reaction` sends 128 normalized spectrum values: 64 for the left channel
  followed by 64 for the right channel, from low to high frequency.

Global pointer tracking needs compositor support. KWin provides it through its
backend because normal Wayland clients cannot track the cursor over other
surfaces.

For Wallpaper Engine compatibility, pointer updates also emit a motion-only
`mousemove`, and audio updates call the listener registered with
`wallpaperRegisterAudioListener(callback)`.

## Wallpaper properties

Properties live in `manifest.toml`:

```toml
[properties.horn_volume]
type = "slider"
label = "Horn volume"
value = 0.25
min = 0.0
max = 1.0
step = 0.05
```

Supported types are `slider`, `toggle`, `text`, `color`, and `select`. The
renderer sends their values through the Wallpaper Engine-compatible callback:

```js
window.wallpaperPropertyListener = {
  applyUserProperties(properties) {
    horn.volume = properties.horn_volume.value;
  },
};
```

`livepaper.toml` selects the wallpaper and configures LivePaper. Wallpaper
properties stay with the wallpaper.

## 2D transform workaround

Some imported wallpapers get seams from `translate3d(x, y, 0)`. They can opt
into replacing it with `translate(x, y)`:

```toml
[wallpaper_engine]
force_2d_transforms = true
```

Leave it off unless the wallpaper needs it.
