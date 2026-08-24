import { expect, test } from '@playwright/test';
import {
  createOpenTrade,
  downloadGdprExport,
  registerAuthenticatedUser,
  seedTradingPrerequisites,
  uniqueJourneyData,
} from './fixtures/real-stack';

test.describe('Remaining journeys against the real stack', () => {
  test('creates, lists, and opens a trade with persisted detail', async ({ page }, testInfo) => {
    const data = uniqueJourneyData('trade', testInfo);
    await registerAuthenticatedUser(page, data.account);
    const prerequisites = await seedTradingPrerequisites(page, data);
    const createdTrade = await createOpenTrade(page, data, prerequisites);

    await page.goto('/app/trades');
    expect(createdTrade.status).toBe(1);
    expect(createdTrade.strategy).toBe(data.trade.strategy);
    expect(createdTrade.notes).toBe(data.trade.notes);
    const persistedTrade = page.getByRole('row').filter({ hasText: data.trade.symbol });
    await expect(persistedTrade).toContainText('Long');
    await expect(persistedTrade).toContainText('Abierta');
    await expect(persistedTrade).toContainText(data.trade.expectedEntryPrice);
    await expect(persistedTrade).toContainText(data.trade.expectedVolume);
  });

  test('downloads the authenticated GDPR export containing account data', async ({ page }, testInfo) => {
    const data = uniqueJourneyData('export', testInfo);
    await registerAuthenticatedUser(page, data.account);

    const exported = await downloadGdprExport(page);

    expect(exported.suggestedFilename).toBe('jadecapital-account-export.json');
    expect(exported.contentType).toContain('application/json');
    expect(exported.payload.formatVersion).toBe('1.0');
    expect(exported.payload.sections.user_profile.email).toBe(data.account.email);
    expect(exported.payload.sections.user_profile.display_name).toBe(data.account.displayName);
    expect(exported.payload.sections.user_profile.role).toBe('Trader');
    expect(exported.payload.sections.user_profile.status).toBe('Active');
    expect(exported.payload.sections.gdpr_consent).toEqual(expect.any(Object));
  });

  test('account deletion enters the 30-day grace period', async ({ page }, testInfo) => {
    const data = uniqueJourneyData('deletion', testInfo);
    await registerAuthenticatedUser(page, data.account);
    await page.goto('/app/settings/delete-account');

    await page.getByRole('button', { name: 'Solicitar eliminación' }).click();
    await page.getByLabel('Confirmación').fill('ELIMINAR');
    const deletionResponse = page.waitForResponse(
      (response) => response.url().endsWith('/api/users/me/account') && response.request().method() === 'DELETE',
    );
    await page.getByRole('button', { name: 'Confirmar eliminación' }).click();

    const response = await deletionResponse;
    expect(response.status()).toBe(202);
    const result = await response.json();
    const gracePeriodMs = Date.parse(result.scheduledHardDeleteAt) - Date.parse(result.softDeletedAt);
    expect(gracePeriodMs).toBe(30 * 24 * 60 * 60 * 1000);
    const pendingDeletion = page.getByTestId('ad-success-card');
    await expect(pendingDeletion).toContainText('Eliminación programada');
    await expect(pendingDeletion).toContainText('Hard-delete programado');
  });
});
