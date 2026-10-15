import { test, expect } from '@playwright/test';
import { login } from './roadmap-helpers';

test.use({ trace: 'off', screenshot: 'off' });
test.afterEach(async ({ page }) => { await page.goto('about:blank'); });

test('login and logout keep a short transition while tabs reveal their content immediately', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'no-preference' });
  await page.addInitScript(() => {
    const seen: string[] = [];
    Object.assign(window, { motionEvents: seen });
    document.addEventListener('animationstart', event => seen.push((event as AnimationEvent).animationName));
    document.addEventListener('transitionrun', event => seen.push((event as TransitionEvent).propertyName));
  });
  await login(page);
  await expect.poll(() => page.evaluate(() => (window as unknown as { motionEvents: string[] }).motionEvents.some(name => name === 'screen-enter'))).toBe(true);
  await page.getByRole('menuitem', { name: '授权管理', exact: true }).click();
  await page.getByRole('menuitem', { name: '卡密管理', exact: true }).click();
  await expect(page.getByRole('heading', { name: '卡密管理', exact: true })).toBeVisible();
  const tabs = page.getByRole('tablist', { name: '工作区标签', exact: true });
  const overview = await tabs.getByRole('tab', { name: '概览', exact: true }).elementHandle();
  const licenses = await tabs.getByRole('tab', { name: '卡密管理', exact: true }).elementHandle();
  const frames = await page.evaluate(async ({ overview, licenses }) => {
    const frames: { path: string; opacity: string; transform: string; visiblePanes: number }[] = [];
    for (const tab of [overview, licenses, overview, licenses]) {
      // Observe the first committed frame, before an animation can mask a blank
      // or overlapping page. Waiting for visibility would miss that regression.
      await new Promise<void>(resolve => {
        const observer = new MutationObserver(() => {
          const pane = document.querySelector<HTMLElement>('.workspace-pane[aria-hidden="false"]')!;
          const style = getComputedStyle(pane);
          frames.push({ path: location.pathname, opacity: style.opacity, transform: style.transform,
            visiblePanes: [...document.querySelectorAll('.workspace-pane')].filter(element => element.getClientRects().length > 0).length });
          observer.disconnect();
          resolve();
        });
        observer.observe(document.querySelector('.workspace-panels')!, { attributes: true, childList: true, subtree: true });
        (tab as HTMLElement).click();
      });
    }
    return frames;
  }, { overview, licenses });
  expect(frames).toEqual(['/overview', '/licenses', '/overview', '/licenses'].map(path => ({ path, opacity: '1', transform: 'none', visiblePanes: 1 })));
  await page.getByRole('button', { name: '退出登录', exact: true }).click();
  await expect(page.getByRole('heading', { name: '登录管理后台', exact: true })).toBeVisible();
  await expect(page.getByRole('tablist', { name: '工作区标签', exact: true })).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => (window as unknown as { motionEvents: string[] }).motionEvents.filter(name => name === 'screen-enter').length)).toBeGreaterThanOrEqual(2);
});

test('reduced motion preserves keyboard navigation without movement', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await login(page);
  await page.getByRole('menuitem', { name: '授权管理', exact: true }).click();
  await page.getByRole('menuitem', { name: '卡密管理', exact: true }).click();
  const pane = page.getByRole('tabpanel', { name: '卡密管理', exact: true });
  await expect(pane).toBeVisible();
  const duration = await pane.evaluate(element => Number.parseFloat(getComputedStyle(element).animationDuration));
  expect(duration).toBeLessThan(.01);
  const tabs = page.getByRole('tablist', { name: '工作区标签', exact: true });
  await tabs.getByRole('tab', { name: '卡密管理', exact: true }).focus();
  await page.keyboard.press('ArrowLeft');
  await expect(tabs.getByRole('tab', { name: '概览', exact: true })).toBeFocused();
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
});
