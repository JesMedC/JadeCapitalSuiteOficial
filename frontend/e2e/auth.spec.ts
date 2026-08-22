import { expect, test } from '@playwright/test';
import {
  loginThroughUi,
  registerAuthenticatedUser,
  uniqueJourneyData,
} from './fixtures/real-stack';

test.describe('Authentication against the real stack', () => {
  test('registration requires consent and persists an account that authenticates', async ({ page }, testInfo) => {
    const account = uniqueJourneyData('registration', testInfo).account;

    await registerAuthenticatedUser(page, account);
    await page.getByRole('button', { name: 'Cerrar sesión' }).click();
    await loginThroughUi(page, account.email);
  });

  test('login keeps the authenticated session after a reload', async ({ page }, testInfo) => {
    const account = uniqueJourneyData('session', testInfo).account;

    await registerAuthenticatedUser(page, account);
    await page.getByRole('button', { name: 'Cerrar sesión' }).click();
    await loginThroughUi(page, account.email);

    await page.reload();
    await expect(page).toHaveURL(/\/app\/dashboard$/);
    await expect(page.getByText(account.email, { exact: true })).toBeVisible();
  });
});
