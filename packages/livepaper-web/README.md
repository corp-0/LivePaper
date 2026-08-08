# @livepaper/web

Typed browser API for native [LivePaper](https://github.com/corp-0/LivePaper) wallpapers.

```sh
npm install @livepaper/web
```

```ts
import { getLivePaper } from "@livepaper/web";

const livepaper = getLivePaper();

livepaper.addEventListener("audiospectrumchange", event => {
  drawSpectrum(event.detail);
});

livepaper.addEventListener("visibilitychange", event => {
  animation.paused = !event.detail.shouldRender;
});
```

The package contains the public types and small access helpers. LivePaper injects
the runtime object before the wallpaper's own scripts run.
