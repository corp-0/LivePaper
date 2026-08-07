# LivePaper protocol

The daemon and renderer communicate using versioned messages. The renderer exposes selected host messages to the page through an event-based JavaScript API.

Each renderer gets a private Unix-domain socket under `$XDG_RUNTIME_DIR/livepaper`. Socket files are readable and writable only by the current user. Messages are UTF-8 JSON objects delimited by a single newline; every message carries the protocol version and message kind.

The renderer injects `window.livepaper` before wallpaper scripts run. It is an
`EventTarget`; the current visibility is available as `livepaper.visibility`,
and changes dispatch a `visibilitychange` event whose `detail` includes
`state`, `shouldRender`, and `shouldMute`.

Manifests must declare every optional host capability they use. Unknown and
duplicate capability names are rejected so a misspelled permission cannot fail
open. Version 1 defines:

- `pointer_input`: enables normal DOM pointer input. The wallpaper claims pointer
  events over its surface, including clicks and scrolling.
- `global_pointer_tracking`: exposes the compositor's global logical cursor coordinates as
  `livepaper.pointerPosition`. Changes dispatch `pointerpositionchange` with
  `{ x, y }` in `detail`. The wallpaper remains click-through unless it also
  declares `pointer_input`. LivePaper also emits a motion-only `mousemove` for
  compatibility with Wallpaper Engine web wallpapers. The event targets the
  element under the cursor and bubbles normally, so listeners on a canvas,
  document, or window all receive it. Synthetic motion never includes buttons,
  clicks, or scrolling.
- `audio_reaction`: exposes 128-sample stereo spectrum frames. The first 64
  values are the left channel and the last 64 are the right channel, ordered
  from low to high frequency. Values are normalized to the `0.0` to `1.0`
  range. Each frame updates `livepaper.audioSpectrum`, dispatches an
  `audiospectrumchange` event, and calls the listener registered through the
  Wallpaper Engine-compatible `wallpaperRegisterAudioListener(callback)` API.

The built-in `global_pointer_tracking` implementation uses the KWin backend.
KWin owns the global cursor position, so tracking continues while another
surface is under the cursor; standard Wayland clients cannot obtain that
information from an empty input region. Other compositor backends can provide
the same protocol capability from their own privileged integration.

Imported Wallpaper Engine wallpapers can ask the renderer to replace CSS
`translate3d(x, y, 0)` calls with 2D `translate(x, y)` when legacy parallax
code produces GPU sampling seams:

```toml
[wallpaper_engine]
force_2d_transforms = true
```

The setting defaults to `false`. It is an opt-in rendering workaround and may
change layer promotion or animation performance.

## Wallpaper properties

Native wallpapers define editable properties in `manifest.toml`. The `value`
is both the current setting and the fallback used before a settings UI exists:

```toml
[properties.horn_volume]
type = "slider"
label = "Horn volume"
value = 0.25
min = 0.0
max = 1.0
step = 0.05
```

Supported control types are `slider`, `toggle`, `text`, `color`, and `select`.
The renderer sends the current values to the same Wallpaper Engine-compatible
callback used by imported web wallpapers:

```js
window.wallpaperPropertyListener = {
  applyUserProperties(properties) {
    horn.volume = properties.horn_volume.value;
  },
};
```

Property definitions stay with the installed wallpaper. `livepaper.toml` only
selects a wallpaper and configures engine-wide behavior.

Protocol version 1 begins with these host-to-page event groups:

- lifecycle and visibility
- pointer input
- user settings
- now playing
- audio spectrum
- system state

Page-to-host requests cover settings persistence, media control, and declared capabilities. Message envelopes and transport framing will be fixed before the first WebKit bridge is implemented.
