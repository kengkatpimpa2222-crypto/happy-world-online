import { BillingAdapter, VerificationResult } from './billing-adapter';

/**
 * Google Play Developer API verification (purchases.products.get / purchases.subscriptionsv2.get).
 * NOT IMPLEMENTED YET (Phase 6): needs a Play Console app, a service account with
 * "View financial data" permission and Sandbox license testers. Until then it reports
 * itself as not configured and the API answers 503 BILLING_NOT_CONFIGURED.
 */
export class GooglePlayBillingAdapter implements BillingAdapter {
  readonly provider = 'GOOGLE_PLAY' as const;

  constructor(
    private readonly packageName: string | undefined,
    private readonly serviceAccountJson: string | undefined,
  ) {}

  isConfigured(): boolean {
    return false; // flip only after Sandbox testing passes (see docs/security.md)
  }

  async verify(_productId: string, _token: string): Promise<VerificationResult> {
    throw new Error('Google Play verification is not implemented yet');
  }
}
