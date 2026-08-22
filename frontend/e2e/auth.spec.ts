import { expect, test, type Page } from '@playwright/test';

const password = 'Wave13!Pass123';

function uniqueAccount(prefix: string): { email: string; displayName: string } {
  const suffix = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
  return {
    email: `${prefix}-${suffix}@example.test`,
    displayName: `Wave 13 ${prefix}`,
  };
}

async function registerThroughUi(
  page: Page,
  account: { email: string; displayName: string },
): Promise<void> {
  await page.goto('/auth/register');
  await page.getByLabel('Correo electrónico').fill(account.email);
  await page.getByLabel('Nombre').fill(account.displayName);
  await page.getByLabel('Contraseña').fill(password);

  const submit = page.getByRole('button', { name: 'Crear cuenta' });
  await expect(submit).toBeDisabled();
  await page.getByTestId('accept-terms').check();
  await page.getByTestId('accept-privacy').check();
  await expect(submit).toBeEnabled();
  await submit.click();

  await expect(page).toHaveURL(/\/app\/dashboard$/);
  await expect(page.getByText(account.email, { exact: true })).toBeVisible();
}

async function loginThroughUi(page: Page, email: string): Promise<void> {
  await page.goto('/auth/login');
  await page.getByLabel('Correo electrónico').fill(email);
  await page.getByLabel('Contraseña').fill(password);
  await page.getByRole('button', { name: 'Iniciar sesión' }).click();
  await expect(page).toHaveURL(/\/app\/dashboard$/);
  await expect(page.getByText(email, { exact: true })).toBeVisible();
}

test.describe('Authentication against the real stack', () => {
  test('registration requires consent and persists an account that authenticates', async ({ page }) => {
    const account = uniqueAccount('registration');

    await registerThroughUi(page, account);
    await page.getByRole('button', { name: 'Cerrar sesión' }).click();
    await loginThroughUi(page, account.email);
  });

  test('login keeps the authenticated session after a reload', async ({ page }) => {
    const account = uniqueAccount('session');

    await registerThroughUi(page, account);
    await page.getByRole('button', { name: 'Cerrar sesión' }).click();
    await loginThroughUi(page, account.email);

    await page.reload();
    await expect(page).toHaveURL(/\/app\/dashboard$/);
    await expect(page.getByText(account.email, { exact: true })).toBeVisible();
  });
});
