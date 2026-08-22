import { readFile } from 'node:fs/promises';
import { expect, type Page, type TestInfo } from '@playwright/test';

export const e2ePassword = 'Wave13!Pass123';

export interface TestAccount {
  email: string;
  displayName: string;
}

export interface JourneyData {
  account: TestAccount;
  tradingAccount: { name: string; broker: string };
  trade: {
    symbol: string;
    entryPrice: string;
    volume: string;
    expectedEntryPrice: string;
    expectedVolume: string;
    strategy: string;
    notes: string;
  };
}

interface ExportPayload {
  formatVersion: string;
  sections: {
    user_profile: { email: string; display_name: string; role: string; status: string };
    gdpr_consent: Record<string, unknown>;
  };
}

export function uniqueJourneyData(journey: string, testInfo: TestInfo): JourneyData {
  const suffix = `${testInfo.workerIndex}-${testInfo.retry}`;
  return {
    account: {
      email: `wave13-${journey}-${suffix}@example.test`,
      displayName: `Wave 13 ${journey} ${suffix}`,
    },
    tradingAccount: {
      name: `Wave13 ${journey} ${suffix}`,
      broker: 'E2E Broker',
    },
    trade: {
      symbol: `W13${testInfo.workerIndex}${testInfo.retry}/USD`,
      entryPrice: '1.23456',
      volume: '0.25',
      expectedEntryPrice: '1.23456',
      expectedVolume: '0.25',
      strategy: `Breakout ${journey} ${suffix}`,
      notes: `Persisted by Wave 13 ${journey} journey ${suffix}`,
    },
  };
}

export async function registerAuthenticatedUser(page: Page, account: TestAccount): Promise<void> {
  await page.goto('/auth/register');
  await page.getByLabel('Correo electrónico').fill(account.email);
  await page.getByLabel('Nombre').fill(account.displayName);
  await page.getByLabel('Contraseña').fill(e2ePassword);
  const submit = page.getByRole('button', { name: 'Crear cuenta' });
  await expect(submit).toBeDisabled();
  await page.getByTestId('accept-terms').check();
  await page.getByTestId('accept-privacy').check();
  await expect(submit).toBeEnabled();
  await submit.click();
  await expect(page).toHaveURL(/\/app\/dashboard$/);
  await expect(page.getByText(account.email, { exact: true })).toBeVisible();
}

export async function loginThroughUi(page: Page, email: string): Promise<void> {
  await page.goto('/auth/login');
  await page.getByLabel('Correo electrónico').fill(email);
  await page.getByLabel('Contraseña').fill(e2ePassword);
  await page.getByRole('button', { name: 'Iniciar sesión' }).click();
  await expect(page).toHaveURL(/\/app\/dashboard$/);
  await expect(page.getByText(email, { exact: true })).toBeVisible();
}

export async function seedTradingPrerequisites(
  page: Page,
  data: JourneyData,
): Promise<{ accountId: string; instrumentId: string }> {
  const authorization = await bearerAuthorization(page);
  const accountResponse = await page.request.post('/api/accounts', {
    headers: { authorization },
    data: {
      ...data.tradingAccount,
      marketType: 1,
      currency: 'USD',
      initialBalance: 10000,
      leverage: 100,
    },
  });
  const accountBody = await accountResponse.text();
  await expectSuccessful(accountResponse.status(), accountBody, 'create account');
  const account = JSON.parse(accountBody) as { id: string };

  const instrumentResponse = await page.request.post('/api/instruments', {
    headers: { authorization },
    data: {
      symbol: data.trade.symbol,
      assetClasses: 1,
      contractSize: 100000,
      decimalPlaces: 5,
      pipValue: 10,
      payoutPercent: 0,
    },
  });
  const instrumentBody = await instrumentResponse.text();
  await expectSuccessful(instrumentResponse.status(), instrumentBody, 'create instrument');
  const instrument = JSON.parse(instrumentBody) as { id: string };

  return {
    accountId: account.id,
    instrumentId: instrument.id,
  };
}

export async function createOpenTrade(
  page: Page,
  data: JourneyData,
  prerequisites: { accountId: string; instrumentId: string },
): Promise<{ status: number; strategy: string; notes: string }> {
  const authorization = await bearerAuthorization(page);
  const response = await page.request.post('/api/trades', {
    headers: { authorization },
    data: {
      ...prerequisites,
      symbol: data.trade.symbol,
      assetClass: 1,
      direction: 1,
      volume: Number(data.trade.volume),
      volumeCurrency: 'USD',
      entryPrice: Number(data.trade.entryPrice),
      entryPriceCurrency: 'USD',
      strategy: data.trade.strategy,
      notes: data.trade.notes,
      checklist: null,
    },
  });
  const body = await response.text();
  await expectSuccessful(response.status(), body, 'create trade');
  return JSON.parse(body) as { status: number; strategy: string; notes: string };
}

export async function downloadGdprExport(page: Page): Promise<{
  suggestedFilename: string;
  contentType: string;
  payload: ExportPayload;
}> {
  const downloadPromise = page.waitForEvent('download');
  const contentType = await page.evaluate(async () => {
    const token = sessionStorage.getItem('jcs.access');
    const response = await fetch('/api/users/me/export', {
      headers: { Authorization: `Bearer ${token ?? ''}` },
    });
    if (!response.ok) throw new Error(`GDPR export failed with HTTP ${response.status}`);
    const type = response.headers.get('content-type') ?? '';
    const blob = await response.blob();
    const href = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = href;
    anchor.download = 'jadecapital-account-export.json';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(href);
    return type;
  });
  const download = await downloadPromise;
  const path = await download.path();
  if (!path) throw new Error('GDPR export download did not produce a local file');
  return {
    suggestedFilename: download.suggestedFilename(),
    contentType,
    payload: JSON.parse(await readFile(path, 'utf8')) as ExportPayload,
  };
}

async function bearerAuthorization(page: Page): Promise<string> {
  const token = await page.evaluate(() => sessionStorage.getItem('jcs.access'));
  if (!token) throw new Error('Authenticated journey has no access token');
  return `Bearer ${token}`;
}

async function expectSuccessful(status: number, body: string, action: string): Promise<void> {
  if (status < 200 || status >= 300) {
    throw new Error(`Failed to ${action}: HTTP ${status}: ${body}`);
  }
}
