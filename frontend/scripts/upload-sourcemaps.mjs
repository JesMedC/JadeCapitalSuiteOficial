import { spawnSync } from 'node:child_process';
import { resolve } from 'node:path';

const credentials = {
  token: process.env.SENTRY_AUTH_TOKEN?.trim() ?? '',
  org: process.env.SENTRY_ORG?.trim() ?? '',
  project: process.env.SENTRY_PROJECT?.trim() ?? '',
};
const release = process.env.SENTRY_RELEASE?.trim() ?? '';
if (!credentials.token) {
  console.log('Sentry source-map upload skipped: no build credentials supplied.');
  process.exit(0);
}
if (!credentials.org || !credentials.project || !release) {
  console.error('Sentry source-map upload requires SENTRY_AUTH_TOKEN, SENTRY_ORG, SENTRY_PROJECT, and SENTRY_RELEASE.');
  process.exit(1);
}

const cli = resolve('node_modules/.bin/sentry-cli');
const result = spawnSync(cli, [
  'sourcemaps', 'upload',
  '--org', credentials.org,
  '--project', credentials.project,
  '--release', release,
  'dist/jade-capital/browser',
], { stdio: 'inherit', env: process.env });

process.exit(result.status ?? 1);
