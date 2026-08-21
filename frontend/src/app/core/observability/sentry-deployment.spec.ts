import { mkdtempSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';

const root = resolve(__dirname, '../../../..');
const generator = join(root, 'scripts/generate-observability-config.mjs');

describe('frontend Sentry deployment contract', () => {
  it.each([
    [{ SENTRY_RELEASE: 'web@1', APP_ENV: 'production' }, 'FRONTEND_SENTRY_DSN'],
    [{ FRONTEND_SENTRY_DSN: 'https://public@example.invalid/42', APP_ENV: 'production' }, 'SENTRY_RELEASE'],
  ])('rejects a production build missing %s', (environment, missingName) => {
    const result = generate(environment);

    expect(result.status).toBe(1);
    expect(result.stderr).toContain(missingName);
  });

  it('allows an explicitly disabled build while retaining release and environment', () => {
    const result = generate({
      ALLOW_FRONTEND_SENTRY_DISABLED: 'true',
      SENTRY_RELEASE: 'web@disabled',
      APP_ENV: 'production',
    });

    expect(result.status).toBe(0);
    expect(readGenerated(result.output)).toContain('enabled: false');
    expect(readGenerated(result.output)).toContain('release: "web@disabled"');
  });

  it('serializes public build settings without executable placeholder injection', () => {
    const result = generate({
      FRONTEND_SENTRY_DSN: 'https://public@example.invalid/42\";throw new Error(\"injected\")//',
      SENTRY_RELEASE: 'web@configured',
      APP_ENV: 'production',
    });

    expect(result.status).toBe(0);
    const generated = readGenerated(result.output);
    expect(generated).toContain('enabled: true');
    expect(generated).toContain('\\\";throw new Error(\\\"injected\\\")//');
  });

  it('builds hidden source maps with upload credentials confined to the build stage', () => {
    const angular = JSON.parse(readFileSync(join(root, 'angular.json'), 'utf8'));
    const packageJson = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8'));
    const sourceMap = angular.projects['jade-capital'].architect.build.configurations.production.sourceMap;
    const dockerfile = readFileSync(join(root, '../infrastructure/Dockerfile.frontend.prod'), 'utf8');
    const runtimeStage = dockerfile.slice(dockerfile.indexOf('FROM nginx:'));

    expect(sourceMap).toEqual({ scripts: true, styles: false, hidden: true });
    expect(packageJson.scripts.build).toContain('generate-observability-config.mjs');
    expect(dockerfile).toContain('generate-observability-config.mjs');
    expect(dockerfile).toContain('--mount=type=secret,id=sentry_auth_token');
    expect(dockerfile).toContain('upload-sourcemaps.mjs');
    expect(dockerfile).toContain("find dist -name '*.map' -delete");
    expect(runtimeStage).not.toContain('SENTRY_AUTH_TOKEN');
  });

  it('mounts the operator Sentry token as a Compose build secret', () => {
    const compose = readFileSync(join(root, '../docker-compose.prod.yml'), 'utf8');
    const frontendStart = compose.indexOf('  frontend:');
    const frontendBuild = compose.slice(frontendStart, compose.indexOf('    restart:', frontendStart));
    const secretDefinitions = compose.slice(compose.lastIndexOf('\nsecrets:'));

    expect(frontendBuild).toContain('secrets:\n        - source: sentry_auth_token\n          target: sentry_auth_token');
    expect(frontendBuild).not.toContain('SENTRY_AUTH_TOKEN:');
    expect(secretDefinitions).toContain('sentry_auth_token:\n    environment: SENTRY_AUTH_TOKEN');
  });

  it('skips upload without a token but rejects partial authenticated credentials', () => {
    const upload = join(root, 'scripts/upload-sourcemaps.mjs');
    const skipped = spawnSync(process.execPath, [upload], {
      cwd: root,
      env: { PATH: process.env['PATH'], SENTRY_ORG: 'example', SENTRY_PROJECT: 'web', SENTRY_RELEASE: 'web@1' },
      encoding: 'utf8',
    });
    const partial = spawnSync(process.execPath, [upload], {
      cwd: root,
      env: { PATH: process.env['PATH'], SENTRY_AUTH_TOKEN: 'not-a-real-token', SENTRY_RELEASE: 'web@1' },
      encoding: 'utf8',
    });

    expect(skipped.status).toBe(0);
    expect(skipped.stdout).toContain('skipped');
    expect(partial.status).toBe(1);
    expect(partial.stderr).toContain('SENTRY_ORG');
  });
});

function generate(environment: NodeJS.ProcessEnv) {
  const output = join(mkdtempSync(join(tmpdir(), 'jade-sentry-')), 'observability.generated.ts');
  const result = spawnSync(process.execPath, [generator, output], {
    cwd: root,
    env: { PATH: process.env['PATH'], ...environment },
    encoding: 'utf8',
  });
  return { ...result, output };
}

function readGenerated(output: string): string {
  return readFileSync(output, 'utf8');
}
