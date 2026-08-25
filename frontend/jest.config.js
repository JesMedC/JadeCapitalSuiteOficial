const { createCjsPreset } = require('jest-preset-angular/presets/index.js');

module.exports = {
  ...createCjsPreset(),
  setupFilesAfterEnv: ['<rootDir>/src/jest.setup.ts'],
  testEnvironment: 'jest-preset-angular/environments/jest-jsdom-env',
  testPathIgnorePatterns: ['/node_modules/', '/dist/', '/e2e/'],
  moduleNameMapper: {
    '^@core/(.*)$': '<rootDir>/src/app/core/$1',
    '^@shared/(.*)$': '<rootDir>/src/app/shared/$1',
    '^@features/(.*)$': '<rootDir>/src/app/features/$1',
    '^@env/(.*)$': '<rootDir>/src/environments/$1',
  },
};
