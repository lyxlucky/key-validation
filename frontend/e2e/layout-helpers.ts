import { expect, type Page } from '@playwright/test';

export async function expectTableFilterSpacing(page: Page) {
  const touching = await page.locator('.ant-table-wrapper:visible').evaluateAll(tables => tables.flatMap(table => {
    const previous = table.previousElementSibling;
    if (!previous?.matches('.filter-bar, .operations-filter, .section-toolbar, .ant-space')) return [];
    const controls = Array.from(previous.querySelectorAll<HTMLElement>('.ant-input-affix-wrapper, .ant-input-search, .ant-select, .ant-picker, input, button'));
    const bottoms = controls.map(control => control.getBoundingClientRect()).filter(rect => rect.width && rect.height).map(rect => rect.bottom);
    if (!bottoms.length) return [];
    const gap = table.getBoundingClientRect().top - Math.max(...bottoms);
    return gap < 15.5 ? [{ filter: previous.className, gap }] : [];
  }));
  expect(touching, 'Search and filter controls need at least 16px before their table').toEqual([]);
}
