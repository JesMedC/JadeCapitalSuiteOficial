/**
 * Jest configuration for the Angular frontend. Slice 0g ships the harness
 * that slice 0d.1 had deferred. ts-jest compiles TS in CommonJS mode;
 * jest-preset-angular sets up the Angular TestBed environment via the
 * jest.setup.ts file referenced below.
 *
 * Run with: npm test  (executes `jest` from package.json)
 */
module.exports = {
  preset: 'jest-preset-angular',
  setupFilesAfterEach: ['<rootDir>/src/jest.setup.ts'],
  testEnvironmentOptions: {
    customExportConditions: ['node'],
  },
  testPathIgnorePatterns: ['/node_modules/', '/dist/'],
  moduleNameMapper: {
    '^@core/(.*)$': '<rootDir>/src/app/core/$1',
    '^@shared/(.*)$': '<rootDir>/src/app/shared/$1',
    '^@features/(.*)$': '<rootDir>/src/app/features/$1',
    '^@env/(.*)$': '<rootDir>/src/environments/$1',
  },
};
