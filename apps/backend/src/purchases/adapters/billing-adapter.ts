export type VerifiedState = 'PURCHASED' | 'PENDING' | 'CANCELLED';

export interface VerificationResult {
  valid: boolean;
  state: VerifiedState;
  productId: string;
  orderId?: string;
  isTest: boolean;
  raw?: Record<string, unknown>;
}

/** One implementation per store. The backend never trusts the client's claim that a purchase succeeded. */
export interface BillingAdapter {
  readonly provider: 'GOOGLE_PLAY' | 'DEV_MOCK';
  isConfigured(): boolean;
  verify(productId: string, purchaseToken: string): Promise<VerificationResult>;
}
