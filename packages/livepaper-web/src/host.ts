import type { AudioSpectrum, LivePaper, PointerPosition, Visibility } from "./index.js";

interface BootstrapConfig {
  readonly wallpaperRoot: string;
  readonly properties: Record<string, { readonly value: unknown }> | null;
  readonly directoryFiles: Record<string, readonly string[]>;
  readonly visibility: Visibility;
  readonly pointerPosition: PointerPosition | null;
  readonly force2DTransforms: boolean;
  readonly remoteEvents: boolean;
  readonly diagnostics: boolean;
}

interface LivePaperHost extends LivePaper {
  _setAudioSpectrum(samples: number[]): void;
  _setPointerPosition(position: PointerPosition): void;
  _setVisibility(visibility: Visibility): void;
}

declare global {
  interface Window {
    wallpaperPropertyListener?: {
      applyUserProperties?(properties: Record<string, { readonly value: unknown }>): void;
      userDirectoryFilesAddedOrChanged?(propertyName: string, files: string[]): void;
    };
    wallpaperRegisterAudioListener?(listener: (samples: AudioSpectrum) => void): void;
    wallpaperRequestRandomFileForProperty?(
      propertyName: string,
      callback: (propertyName: string, filePath: string) => void,
    ): void;
  }
}

const config = loadConfig();
installConsoleDiagnostics();
installFileUrlCompatibility(config.wallpaperRoot);
installTransformCompatibility(config.force2DTransforms);

let visibility = Object.freeze(config.visibility);
let pointerPosition = config.pointerPosition === null ? null : Object.freeze(config.pointerPosition);
let audioSpectrum: AudioSpectrum | null = null;
let audioListener: ((samples: AudioSpectrum) => void) | null = null;
const mediaElements = new Set<HTMLMediaElement>();
const livepaper = new EventTarget() as LivePaperHost;

Object.defineProperties(livepaper, {
  visibility: { get: () => visibility, enumerable: true },
  pointerPosition: { get: () => pointerPosition, enumerable: true },
  audioSpectrum: { get: () => audioSpectrum, enumerable: true },
  _setAudioSpectrum: {
    value: (next: number[]) => {
      audioSpectrum = next;
      audioListener?.(next);
      livepaper.dispatchEvent(new CustomEvent("audiospectrumchange", { detail: audioSpectrum }));
    },
  },
  _setPointerPosition: {
    value: (next: PointerPosition) => {
      pointerPosition = Object.freeze(next);
      livepaper.dispatchEvent(new CustomEvent("pointerpositionchange", { detail: pointerPosition }));
      const target = document.elementFromPoint(pointerPosition.x, pointerPosition.y) ?? window;
      target.dispatchEvent(new MouseEvent("mousemove", {
        bubbles: true,
        composed: true,
        clientX: pointerPosition.x,
        clientY: pointerPosition.y,
        screenX: pointerPosition.x,
        screenY: pointerPosition.y,
      }));
    },
  },
  _setVisibility: {
    value: (next: Visibility) => {
      visibility = Object.freeze(next);
      for (const media of mediaElements) media.muted = visibility.shouldMute;
      livepaper.dispatchEvent(new CustomEvent("visibilitychange", { detail: visibility }));
    },
  },
});

Object.defineProperty(window, "livepaper", { value: livepaper, enumerable: true });
Object.defineProperty(window, "wallpaperRegisterAudioListener", {
  value: (listener: (samples: AudioSpectrum) => void) => {
    if (typeof listener !== "function") throw new TypeError("Audio listener must be a function.");
    audioListener = listener;
  },
});

if (config.remoteEvents) installRemoteEvents();

Object.defineProperty(window, "wallpaperRequestRandomFileForProperty", {
  value: (propertyName: string, callback: (propertyName: string, filePath: string) => void) => {
    const files = Object.hasOwn(config.directoryFiles, propertyName) ? config.directoryFiles[propertyName]! : [];
    const file = files[Math.floor(Math.random() * files.length)] ?? "";
    window.setTimeout(() => callback(propertyName, file), 0);
  },
});

const nativeMediaPlay = HTMLMediaElement.prototype.play;
HTMLMediaElement.prototype.play = function (...args): Promise<void> {
  mediaElements.add(this);
  this.muted = visibility.shouldMute;
  return nativeMediaPlay.apply(this, args);
};

document.addEventListener("DOMContentLoaded", () => {
  if (pointerPosition) livepaper._setPointerPosition(pointerPosition);
}, { once: true });

window.addEventListener("load", () => {
  // Wallpaper load handlers must initialize their canvases before receiving settings.
  window.setTimeout(() => applyProperties(config.properties), 0);
}, { once: true });

if (config.diagnostics) installDiagnostics(livepaper);

function loadConfig(): BootstrapConfig {
  // The host API must exist before the wallpaper's first script runs.
  const request = new XMLHttpRequest();
  request.open("GET", "/__livepaper/config.json", false);
  request.send();
  if (request.status !== 200) {
    throw new Error(`Could not load LivePaper host configuration (${request.status}).`);
  }

  return JSON.parse(request.responseText) as BootstrapConfig;
}

function applyProperties(properties: BootstrapConfig["properties"]): void {
  if (properties === null) return;
  const apply = (): void => {
    const listener = window.wallpaperPropertyListener;
    if (typeof listener?.applyUserProperties === "function") {
      // Populate slideshow lists before settings select the first image.
      for (const [propertyName, files] of Object.entries(config.directoryFiles)) {
        listener.userDirectoryFilesAddedOrChanged?.(propertyName, [...files]);
      }
      listener.applyUserProperties(properties);
    } else {
      window.setTimeout(apply, 0);
    }
  };
  apply();
}

function installRemoteEvents(): void {
  const events = new EventSource("/__livepaper/events");
  events.addEventListener("message", event => {
    const update = JSON.parse(event.data) as {
      readonly type: "visibility" | "pointerPosition" | "audioSpectrum";
      readonly value: Visibility | PointerPosition | number[];
    };
    switch (update.type) {
      case "visibility":
        livepaper._setVisibility(update.value as Visibility);
        break;
      case "pointerPosition":
        livepaper._setPointerPosition(update.value as PointerPosition);
        break;
      case "audioSpectrum":
        livepaper._setAudioSpectrum(update.value as number[]);
        break;
    }
  });
}

function installFileUrlCompatibility(wallpaperRoot: string): void {
  const root = wallpaperRoot.replace(/\\/g, "/").replace(/\/+$/, "");
  const translate = (value: string): string => {
    if (typeof value !== "string") return value;
    if (!value.toLowerCase().startsWith("file:")) return value;
    let path: string;
    try {
      path = decodeURIComponent(new URL(value).pathname).replace(/^\/+/, "/");
    } catch {
      return value;
    }
    if (path !== root && !path.startsWith(`${root}/`)) return value;
    const relative = path.slice(root.length).replace(/^\/+/, "");
    const encoded = relative.split("/").map(encodeURIComponent).join("/");
    return new URL(`/${encoded}`, window.location.origin).href;
  };
  const translateCss = (value: string): string => typeof value !== "string" ? value : value.replace(
    /url\(\s*(["']?)(.*?)\1\s*\)/gi,
    (_match, quote: string, url: string) => `url(${quote}${translate(url)}${quote})`,
  );
  const patchUrlProperty = (prototype: object, property: string): void => {
    const descriptor = Object.getOwnPropertyDescriptor(prototype, property);
    if (!descriptor?.set) return;
    Object.defineProperty(prototype, property, {
      ...descriptor,
      set(value: string) { descriptor.set?.call(this, translate(value)); },
    });
  };
  for (const [prototype, properties] of [
    [HTMLImageElement.prototype, ["src"]],
    [HTMLMediaElement.prototype, ["src", "poster"]],
    [HTMLSourceElement.prototype, ["src"]],
  ] as const) {
    for (const property of properties) patchUrlProperty(prototype, property);
  }

  const nativeSetAttribute = Element.prototype.setAttribute;
  Element.prototype.setAttribute = function (name, value): void {
    nativeSetAttribute.call(
      this,
      name,
      name.toLowerCase() === "src" || name.toLowerCase() === "poster" ? translate(value) : value,
    );
  };

  const style = CSSStyleDeclaration.prototype;
  for (const property of ["background", "backgroundImage"] as const) {
    const descriptor = Object.getOwnPropertyDescriptor(style, property);
    if (!descriptor?.set) continue;
    Object.defineProperty(style, property, {
      ...descriptor,
      set(value: string) { descriptor.set?.call(this, translateCss(value)); },
    });
  }
  const nativeSetProperty = style.setProperty;
  style.setProperty = function (property, value, priority): void {
    nativeSetProperty.call(this, property, value === null ? "" : translateCss(value), priority);
  };
}

function installTransformCompatibility(enabled: boolean): void {
  if (!enabled) return;
  const to2D = (value: string | null): string | null => value?.replace(
    /translate3d\(\s*([^,]+),\s*([^,]+),\s*0(?:px)?\s*\)/gi,
    "translate($1, $2)",
  ) ?? value;
  const style = CSSStyleDeclaration.prototype;
  for (const property of ["transform", "webkitTransform"] as const) {
    const descriptor = Object.getOwnPropertyDescriptor(style, property);
    if (!descriptor?.set) continue;
    Object.defineProperty(style, property, {
      ...descriptor,
      set(value: string) { descriptor.set?.call(this, to2D(value)); },
    });
  }
  const nativeSetProperty = style.setProperty;
  style.setProperty = function (property, value, priority): void {
    nativeSetProperty.call(
      this,
      property,
      property === "transform" || property === "-webkit-transform" ? to2D(value) : value,
      priority,
    );
  };
}

function installDiagnostics(host: LivePaper): void {
  const metrics = {
    raf: 0,
    clear: 0,
    drawArrays: 0,
    drawElements: 0,
    mousemove: 0,
    mousedown: 0,
    mouseup: 0,
    click: 0,
    wheel: 0,
    mousemoveTarget: null as string | null,
  };
  window.addEventListener("mousemove", event => {
    metrics.mousemove++;
    metrics.mousemoveTarget = (event.target as HTMLElement | null)?.id
      || (event.target as HTMLElement | null)?.tagName
      || null;
  });
  for (const type of ["mousedown", "mouseup", "click", "wheel"] as const) {
    window.addEventListener(type, () => metrics[type]++);
  }
  const nativeRequestAnimationFrame = window.requestAnimationFrame.bind(window);
  window.requestAnimationFrame = callback => nativeRequestAnimationFrame(timestamp => {
    metrics.raf++;
    callback(timestamp);
  });
  for (const prototype of [
    globalThis.WebGLRenderingContext?.prototype,
    globalThis.WebGL2RenderingContext?.prototype,
  ]) {
    if (!prototype) continue;
    const methods = prototype as unknown as Record<string, (...args: never[]) => unknown>;
    for (const method of ["clear", "drawArrays", "drawElements"] as const) {
      const nativeMethod = methods[method];
      if (!nativeMethod) continue;
      methods[method] = function (this: typeof prototype, ...args: never[]): unknown {
        metrics[method]++;
        return nativeMethod.apply(this, args);
      };
    }
  }
  window.setTimeout(() => {
    const canvas = document.querySelector("canvas");
    const context = (canvas?.getContext("webgl")
      ?? canvas?.getContext("experimental-webgl")) as WebGLRenderingContext | null;
    const backgroundMedia = (globalThis as typeof globalThis & { bgm?: HTMLMediaElement }).bgm;
    const report = {
      ...metrics,
      pointerPosition: host.pointerPosition,
      layers: Array.from(document.querySelectorAll<HTMLElement>(".layer"), layer => layer.style.transform),
      canvas: canvas ? `${canvas.width}x${canvas.height}` : null,
      webgl: context ? context.getParameter(context.RENDERER) : null,
      media: Array.from(document.querySelectorAll<HTMLMediaElement>("audio,video"), mediaState),
      bgm: backgroundMedia ? mediaState(backgroundMedia) : null,
    };
    void fetch(`/__livepaper_benchmark_report?${encodeURIComponent(JSON.stringify(report))}`);
  }, 10_000);
}

function installConsoleDiagnostics(): void {
  const send = (level: string, values: unknown[]): void => {
    const message = values.map(value => {
      if (typeof value === "string") return value;
      try { return JSON.stringify(value); } catch { return String(value); }
    }).join(" ");
    void fetch(`/__livepaper/console?level=${encodeURIComponent(level)}&message=${encodeURIComponent(message)}`)
      .catch(() => undefined);
  };
  for (const level of ["debug", "log", "info", "warn", "error"] as const) {
    const native = console[level].bind(console);
    console[level] = (...values: unknown[]): void => {
      native(...values);
      send(level, values);
    };
  }
  window.addEventListener("error", event => {
    send("error", [`${event.message} (${event.filename}:${event.lineno}:${event.colno})`]);
  });
  window.addEventListener("unhandledrejection", event => {
    send("error", ["Unhandled promise rejection:", event.reason]);
  });
}

function mediaState(element: HTMLMediaElement): object {
  return {
    paused: element.paused,
    muted: element.muted,
    volume: element.volume,
    readyState: element.readyState,
    error: element.error?.message ?? null,
  };
}
