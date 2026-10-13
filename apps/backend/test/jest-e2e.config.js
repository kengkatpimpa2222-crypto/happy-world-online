/** Integration tests: real Nest app + real PostgreSQL test database (.env.test). */
module.exports = {
  rootDir: '.',
  testRegex: '.*\\.e2e-spec\\.ts$',
  transform: { '^.+\\.ts$': ['ts-jest', { tsconfig: '<rootDir>/../tsconfig.json' }] },
  moduleNameMapper: { '^@hwo/shared-contracts$': '<rootDir>/../../../packages/shared-contracts/src' },
  testEnvironment: 'node',
  globalSetup: '<rootDir>/global-setup.ts',
  setupFiles: ['<rootDir>/load-test-env.ts'],
  testTimeout: 30000,
};
