// Wave 12 slice 12.1 — Sentry FE init tests.
//
// Asserts the silent-skip contract from the FE side:
//   1. Empty __SENTRY_DSN__ → Sentry.init is NEVER called.
//   2. Set __SENTRY_DSN__ → Sentry.init IS called with the right shape.
//   3. Whitespace __SENTRY_DSN__ → also skipped (treats as unset).
//
// The mock for @sentry/angular is local to this file so the rest of the
// app's jest tests are not affected.

import { initSentry } from './sentry-init';

const initMock = jest.fn();

jest.mock('@sentry/angular', () => ({
  init: (...args: unknown[]) => initMock(...args),
}));

describe('initSentry', () => {
  beforeEach(() => {
    initMock.mockClear();
  });

  it('skips Sentry.init when __SENTRY_DSN__ is empty', () => {
    // @ts-expect-error -- global is provided by build-time define
    globalThis.__SENTRY_DSN__ = '';
    // @ts-expect-error -- global is provided by build-time define
    globalThis.__APP_ENV__ = 'production';

    initSentry();

    expect(initMock).not.toHaveBeenCalled();
  });

  it('skips Sentry.init when __SENTRY_DSN__ is whitespace', () => {
    // @ts-expect-error -- global is provided by build-time define
    globalThis.__SENTRY_DSN__ = '   ';
    // @ts-expect-error -- global is provided by build-time define
    globalThis.__APP_ENV__ = 'production';

    initSentry();

    expect(initMock).not.toHaveBeenCalled();
  });

  it('calls Sentry.init with sendDefaultPii=false when DSN is set', () => {
    // @ts-expect-error -- global is provided by build-time define
    globalThis.__SENTRY_DSN__ = 'https://fake@sentry.io/123';
    // @ts-expect-error -- global is provided by build-time define
    globalThis.__APP_ENV__ = 'production';

    initSentry();

    expect(initMock).toHaveBeenCalledTimes(1);
    expect(initMock).toHaveBeenCalledWith(expect.objectContaining({
      dsn: 'https://fake@sentry.io/123',
      environment: 'production',
      sendDefaultPii: false,
      tracesSampleRate: 0.1,
    }));
  });

  it('falls back to development when __APP_ENV__ is empty', () => {
    // @ts-expect-error -- global is provided by build-time define
    globalThis.__SENTRY_DSN__ = 'https://fake@sentry.io/123';
    // @ts-expect-error -- global is provided by build-time define
    globalThis.__APP_ENV__ = '';

    initSentry();

    expect(initMock).toHaveBeenCalledWith(expect.objectContaining({
      environment: 'development',
    }));
  });
});
