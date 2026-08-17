type ScreenWakeLockSentinel = {
  readonly released: boolean;
  release(): Promise<void>;
  addEventListener(type: 'release', listener: () => void): void;
};

type NavigatorWithWakeLock = Navigator & {
  wakeLock?: {
    request(type: 'screen'): Promise<ScreenWakeLockSentinel>;
  };
};

let sentinel: ScreenWakeLockSentinel | null = null;
let requestInProgress = false;
let installed = false;

async function requestScreenWakeLock(): Promise<void> {
  if (document.visibilityState !== 'visible' || requestInProgress) return;
  if (sentinel && !sentinel.released) return;

  const nav = navigator as NavigatorWithWakeLock;
  if (!nav.wakeLock) {
    console.warn('TWIN A: Screen Wake Lock API is not supported by this browser.');
    return;
  }

  requestInProgress = true;
  try {
    const lock = await nav.wakeLock.request('screen');
    sentinel = lock;
    lock.addEventListener('release', () => {
      if (sentinel === lock) sentinel = null;
      console.log('TWIN A: Screen wake lock released.');
    });
    console.log('TWIN A: Screen wake lock active.');
  } catch (error) {
    sentinel = null;
    console.warn('TWIN A: Unable to acquire screen wake lock.', error);
  } finally {
    requestInProgress = false;
  }
}

export function installScreenWakeLock(): void {
  if (installed) return;
  installed = true;

  const retry = (): void => { void requestScreenWakeLock(); };

  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') retry();
  });
  window.addEventListener('pageshow', retry);
  window.addEventListener('focus', retry);
  document.addEventListener('pointerdown', retry, { passive: true });

  retry();
}
