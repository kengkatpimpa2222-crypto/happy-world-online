/** Unit tests: pure logic, no database. */
module.exports = {
  rootDir: 'src',
  testRegex: '.*\\.spec\\.ts$',
  transform: { '^.+\\.ts$': ['ts-jest', { tsconfig: '<rootDir>/../tsconfig.json' }] },
  moduleNameMapper: { '^@hwo/shared-contracts$': '<rootDir>/../../../packages/shared-contracts/src' },
  testEnvironment: 'node',
};
