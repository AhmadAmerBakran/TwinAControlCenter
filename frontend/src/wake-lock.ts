type ScreenWakeLockSentinel = {
  readonly released: boolean;
  release(): Promise<void>;
  addEventListener(type: 'release', listener: () => void): void;
};

type NavigatorWithWakeLock = Navigator & {
  wakeLock?: { request(type: 'screen'): Promise<ScreenWakeLockSentinel>; };
};

const preferenceKey = 'twina.keepScreenAwake';
let sentinel: ScreenWakeLockSentinel | null = null;
let requestInProgress = false;
let installed = false;
let preferenceEnabled = true;

try { preferenceEnabled = localStorage.getItem(preferenceKey) !== 'false'; } catch { }

async function releaseScreenWakeLock(): Promise<void> {
  const active = sentinel;
  sentinel = null;
  if (active && !active.released) { try { await active.release(); } catch { } }
}

async function requestScreenWakeLock(): Promise<void> {
  if (!preferenceEnabled) { await releaseScreenWakeLock(); return; }
  if (document.visibilityState !== 'visible' || requestInProgress) return;
  if (sentinel && !sentinel.released) return;
  const nav = navigator as NavigatorWithWakeLock;
  if (!nav.wakeLock) return;
  requestInProgress = true;
  try {
    const lock = await nav.wakeLock.request('screen');
    sentinel = lock;
    lock.addEventListener('release', () => { if (sentinel === lock) sentinel = null; });
  } catch { sentinel = null; }
  finally { requestInProgress = false; }
}

export function installScreenWakeLock(): void {
  if (installed) return;
  installed = true;
  const retry = (): void => { void requestScreenWakeLock(); };
  window.addEventListener('twina-wake-lock-preference', (event: Event) => {
    const enabled = Boolean((event as CustomEvent<{enabled:boolean}>).detail?.enabled);
    preferenceEnabled = enabled;
    try { localStorage.setItem(preferenceKey, String(enabled)); } catch { }
    if (enabled) retry(); else void releaseScreenWakeLock();
  });
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') retry(); });
  window.addEventListener('pageshow', retry);
  window.addEventListener('focus', retry);
  document.addEventListener('pointerdown', retry, { passive: true });
  retry();
}
