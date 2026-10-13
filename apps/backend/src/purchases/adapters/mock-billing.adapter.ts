import { BillingAdapter, VerificationResult } from './billing-adapter';

/**
 * DEVELOPMENT ONLY. Simulates store verification so the flow can be tested without a Play account.
 * Refused in production by env validation (BILLING_MODE=mock) and by isConfigured().
 * Tokens: `mock_ok_*` = purchased, `mock_pending_*` = pending, anything else = invalid.
 */
export class MockBillingAdapter implements BillingAdapter {
  readonly provider = 'DEV_MOCK' as const;

  constructor(private readonly enabled: boolean) {}

  isConfigured(): boolean {
    return this.enabled;
  }

  async verify(productId: string, token: string): Promise<VerificationResult> {
    if (token.startsWith('mock_ok_')) {
      return { valid: true, state: 'PURCHASED', productId, orderId: `MOCK.${token.slice(8, 40)}`, isTest: true };
    }
    if (token.startsWith('mock_pending_')) return { valid: true, state: 'PENDING', productId, isTest: true };
    return { valid: false, state: 'CANCELLED', productId, isTest: true };
  }
}
