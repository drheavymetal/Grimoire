import { expect, test } from '@playwright/test';
import { signedIn } from './helpers';

// The cold-start picker (D15/D59). These exist because of a bug a user hit on the live app: he
// picked four thrash bands, searched for Megadeth to make it his fifth, and the search told him
// "Already added" and refused the click. He had never added it — picking a band unfolds its
// neighbours into the grid beneath it, and Megadeth was one of them. The picker was treating
// "visible on the grid" as "chosen by the user". Those are different things, and the grid is
// mostly the former: only `picked` is the user's own choice.
//
// Locators follow the suite's idiom (rite.spec.ts): grid chips are the buttons carrying
// aria-pressed, so a button WITHOUT it that bears a band's name is a search result row.
test.describe('cold start picker', () => {
  test('a band the grid merely suggested can still be added from search', async ({ page, request }) => {
    await signedIn(page, request);
    await page.goto('/rite');
    await expect(page.getByRole('heading', { name: 'Name what you already love' })).toBeVisible();

    const chips = page.locator('button[aria-pressed]');
    await expect(chips.first()).toBeVisible();
    const before = await chips.count();

    // Pick the first band: its kin unfold directly beneath it, growing the grid.
    await chips.first().click();
    await expect(page.locator('button[aria-pressed="true"]')).toHaveCount(1);
    await expect.poll(async () => chips.count()).toBeGreaterThan(before);

    // The chip right below the pick is one of the bands the unfold just suggested. The user never
    // chose it — it is exactly Carlos's Megadeth.
    const suggested = page.locator('button[aria-pressed="false"]').nth(0);
    const name = (await suggested.locator('span').first().innerText()).trim();
    expect(name.length).toBeGreaterThan(0);

    // Search for it. Before the fix this row read "Already added" and was disabled.
    await page.getByRole('searchbox').fill(name);
    const row = page.locator('button:not([aria-pressed])').filter({ hasText: name });
    await expect(row.first()).toBeVisible();
    await expect(row.first()).toBeEnabled();
    await expect(row.first()).not.toContainText('Already added');

    // And the click must take: "added" becomes true only now.
    await row.first().click();
    await expect.poll(async () => page.locator('button[aria-pressed="true"]').count()).toBe(2);
  });

  test('a band the user really picked does read as already added', async ({ page, request }) => {
    // The other half: the label is not wrong, it was shown to the wrong bands. If this fails the
    // fix went too far and the search now offers duplicates of real picks.
    await signedIn(page, request);
    await page.goto('/rite');
    await expect(page.getByRole('heading', { name: 'Name what you already love' })).toBeVisible();

    const chips = page.locator('button[aria-pressed]');
    await expect(chips.first()).toBeVisible();
    const name = (await chips.first().locator('span').first().innerText()).trim();

    await chips.first().click();
    await expect(page.locator('button[aria-pressed="true"]')).toHaveCount(1);

    await page.getByRole('searchbox').fill(name);
    const row = page.locator('button:not([aria-pressed])').filter({ hasText: name });
    await expect(row.first()).toBeVisible();
    await expect(row.first()).toContainText('Already added');
    await expect(row.first()).toBeDisabled();
  });
});
