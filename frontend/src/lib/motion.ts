import { flushSync } from 'react-dom';

let currentTransition: ViewTransition | undefined;

// Native snapshots handle authentication changes; CSS handles the fallback.
export async function changeScreen(update: () => void) {
  if (!document.startViewTransition || window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    update();
    return;
  }
  currentTransition?.skipTransition();
  // The browser snapshots already fade the whole screen. Do not capture a
  // second, partially transparent CSS entry animation inside the new snapshot.
  // Keep this capability marker: removing it would restart CSS entry animations.
  document.documentElement.classList.add('screen-snapshots-enabled');
  const transition = document.startViewTransition(() => { flushSync(update); });
  currentTransition = transition;
  void transition.finished.catch(() => undefined).finally(() => {
    if (currentTransition === transition) {
      currentTransition = undefined;
    }
  });
  await transition.updateCallbackDone;
}
