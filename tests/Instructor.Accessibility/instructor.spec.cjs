const { test, expect } = require('@playwright/test');
const AxeBuilder = require('@axe-core/playwright').default;

async function scan(page, name) {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa'])
    .analyze();
  expect(results.violations.map(v => ({
    id: v.id, impact: v.impact, description: v.description,
    nodes: v.nodes.map(n => n.target)
  })), name + ' accessibility violations').toEqual([]);
}

test('library: axe, keyboard and skip navigation', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('#guides-table .table')).toBeVisible();
  await scan(page, 'Guide library');
  await page.keyboard.press('Tab');
  await expect(page.locator('.skip-link')).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.locator('#main-content')).toBeFocused();
  await expect(page.locator('#guide-search')).toHaveAccessibleName(/חיפוש לומדה/);
});

test('guide detail and step tabs: axe and arrow navigation', async ({ page }) => {
  await page.goto('/');
  const first = page.locator('[data-open]').first();
  await expect(first).toBeVisible();
  await first.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('#detail-view')).toBeVisible();
  await expect(page.locator('#new-guide')).toBeHidden();
  await scan(page, 'Guide detail');
  const steps = page.locator('[data-tab="steps"]');
  const contexts = page.locator('[data-tab="contexts"]');
  await steps.focus();
  await page.keyboard.press('ArrowLeft');
  await expect(contexts).toBeFocused();
  await expect(contexts).toHaveAttribute('aria-selected','true');
  await expect(page.locator('#contexts-panel')).toBeVisible();
  await scan(page, 'Application contexts');
  await page.keyboard.press('ArrowRight');
  await expect(steps).toBeFocused();
  await expect(steps).toHaveAttribute('aria-selected','true');
  const firstStep = page.locator('[data-step]').first();
  if (await firstStep.count()) {
    await firstStep.focus();
    await page.keyboard.press('Enter');
    await expect(firstStep).toHaveAttribute('aria-pressed','true');
    await expect(page.locator('#step-inspector h2')).toBeVisible();
    await scan(page, 'Step inspector');
  }
});

test('edit dialog: axe, escape and focus restoration (read only)', async ({ page }) => {
  await page.goto('/');
  await page.locator('[data-open]').first().click();
  const opener = page.locator('#rename-guide');
  await opener.click();
  const dialog = page.locator('#editor-dialog');
  await expect(dialog).toBeVisible();
  await scan(page, 'Edit dialog');
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(opener).toBeFocused();
});
