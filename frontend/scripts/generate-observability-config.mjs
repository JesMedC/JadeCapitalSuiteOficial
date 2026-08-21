import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';

const output = resolve(process.argv[2] ?? 'src/app/core/observability/observability.generated.ts');
const dsn = process.env.FRONTEND_SENTRY_DSN?.trim() ?? '';
const release = process.env.SENTRY_RELEASE?.trim() ?? '';
const environment = process.env.APP_ENV?.trim() || 'production';
const allowDisabled = process.env.ALLOW_FRONTEND_SENTRY_DISABLED === 'true';

const errors = [];
if (!release) errors.push('SENTRY_RELEASE is required');
if (!dsn && !allowDisabled) {
  errors.push('FRONTEND_SENTRY_DSN is required unless ALLOW_FRONTEND_SENTRY_DISABLED=true');
}
if (errors.length > 0) {
  console.error(`Frontend Sentry configuration invalid: ${errors.join('; ')}`);
  process.exit(1);
}

const config = { dsn, release, environment, enabled: dsn.length > 0 };
const source = [
  '// Generated at build time by scripts/generate-observability-config.mjs.',
  'export const frontendObservability = Object.freeze({',
  `  dsn: ${JSON.stringify(config.dsn)},`,
  `  release: ${JSON.stringify(config.release)},`,
  `  environment: ${JSON.stringify(config.environment)},`,
  `  enabled: ${config.enabled},`,
  '});',
  '',
].join('\n');

mkdirSync(dirname(output), { recursive: true });
writeFileSync(output, source, 'utf8');
console.log(config.enabled ? 'Frontend Sentry enabled.' : 'Frontend Sentry intentionally disabled.');
