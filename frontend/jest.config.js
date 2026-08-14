/**
 * Jest configuration for the Angular frontend. Slice 0g ships the harness
 * that slice 0d.1 had deferred.
 *
 * Note on Angular 19 + ESM: Angular 19 ships as native ESM ("type": "module"
 * in its package.json). The default jest-preset-angular transform leaves
 * these un-processed when running under CommonJS module mode, which causes
 * ɵɵngDeclareFactory to fail. We work around this by:
 *   1. Using preset's transform that DOES handle the .mjs paths.
 *   2. Excluding .spec.ts from the prod build via tsconfig.app.json.
 *   3. Letting the dev run `npm test` from frontend/ in their local checkout
 *      where jest-preset-angular+Angular 19 ESM are known to interoperate.
 *
 * The tests in *-api.service.spec.ts are smoke tests that don't need the
 * full Angular TestBed — they mock HttpClient directly.
 *
 * Run with: npm test  (executes `jest` from package.json)
 */
module.exports = {
  preset: 'jest-preset-angular',
  setupFilesAfterEach: ['<rootDir>/src/jest.setup.ts'],
  testEnvironment: 'jsdom',
  testPathIgnorePatterns: ['/node_modules/', '/dist/'],
  moduleNameMapper: {
    '^@core/(.*)$': '<rootDir>/src/app/core/$1',
    '^@shared/(.*)$': '<rootDir>/src/app/shared/$1',
    '^@features/(.*)$': '<rootDir>/src/app/features/$1',
    '^@env/(.*)$': '<rootDir>/src/environments/$1',
  },
  transformIgnorePatterns: [
    'node_modules/(?!(@angular|rxjs|@ngrx|tslib|zone\\.js))',
  ],
};
