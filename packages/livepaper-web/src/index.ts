export type VisibilityState =
  | "Visible"
  | "PartiallyCovered"
  | "FullyCovered"
  | "OutputDisabled"
  | "SessionLocked";

export interface Visibility {
  readonly state: VisibilityState;
  readonly shouldRender: boolean;
  readonly shouldMute: boolean;
}

export interface PointerPosition {
  readonly x: number;
  readonly y: number;
}

export type AudioSpectrum = readonly number[];

export interface LivePaperEventMap {
  visibilitychange: CustomEvent<Visibility>;
  pointerpositionchange: CustomEvent<PointerPosition>;
  audiospectrumchange: CustomEvent<AudioSpectrum>;
}

export interface LivePaper extends EventTarget {
  readonly visibility: Visibility;
  readonly pointerPosition: PointerPosition | null;
  readonly audioSpectrum: AudioSpectrum | null;

  addEventListener<K extends keyof LivePaperEventMap>(
    type: K,
    listener: (this: LivePaper, event: LivePaperEventMap[K]) => void,
    options?: boolean | AddEventListenerOptions,
  ): void;
  addEventListener(
    type: string,
    callback: EventListenerOrEventListenerObject | null,
    options?: boolean | AddEventListenerOptions,
  ): void;

  removeEventListener<K extends keyof LivePaperEventMap>(
    type: K,
    listener: (this: LivePaper, event: LivePaperEventMap[K]) => void,
    options?: boolean | EventListenerOptions,
  ): void;
  removeEventListener(
    type: string,
    callback: EventListenerOrEventListenerObject | null,
    options?: boolean | EventListenerOptions,
  ): void;
}

declare global {
  interface Window {
    readonly livepaper?: LivePaper;
  }
}

export function getLivePaper(): LivePaper {
  if (!window.livepaper) {
    throw new Error("This page is not running in LivePaper.");
  }

  return window.livepaper;
}

export function isRunningInLivePaper(): boolean {
  return window.livepaper !== undefined;
}
