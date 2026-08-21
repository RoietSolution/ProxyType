export interface Membership { organizationUnitId: string; code: string; name: string; unitType: string; isPrimary: boolean; }
export interface UserSummary { userId: string; username: string; email: string; displayName: string; mustChangePassword: boolean; roles: string[]; memberships: Membership[]; }
export interface TokenResponse { accessToken: string; refreshToken: string; accessTokenExpiresAtUtc: string; refreshTokenExpiresAtUtc: string; user: UserSummary; }
export interface OrganizationUnit { organizationUnitId: string; parentOrganizationUnitId: string | null; unitType: 'PLATFORM'|'CMF'|'CSF'|'CSP'; code: string; name: string; status: string; }
export interface EffectiveService { serviceId: string; code: string; name: string; category: string; isAllowed: boolean; decisionReason: string; localEffect: 'ALLOW'|'DENY'|null; }
export interface DashboardData {
  organization: Membership | null;
  hierarchy: { total: number; active: number; cmf: number; csf: number; csp: number };
  services: { available: number; restricted: number; items: EffectiveService[] };
  recentSecurityActivity: Array<{ succeeded: boolean; failureCode: string|null; ipAddress: string|null; occurredAtUtc: string }>;
  generatedAtUtc: string;
}
export interface FinoDmtRequest { accountNumber: string; ifscCode: string; beneficiaryName: string; mobile: string; amount: number; transferMode: 'IMPS'|'NEFT'|'RTGS'; comments: string; idempotencyKey: string; }
export interface FinoDmtResponse { transactionId: string; transactionReference: string; status: string; providerMode: string; providerReference: string|null; amount: number; charges: number; commission: number; failureReason: string|null; receiptNumber: string|null; }
export interface UpiFundingRequest { amount: number; idempotencyKey: string; }
export interface UpiFundingResponse { transactionId: string; transactionReference: string; status: string; providerMode: string; providerReference: string|null; paymentUrl: string|null; qrCode: string|null; amount: number; charges: number; commission: number; creditAmount: number; walletCredited: boolean; failureReason: string|null; receiptNumber: string|null; }
export interface RechargeRequest { account: string; amount: number; operator: string; rechargeType: 'MOBILE'|'DTH'|'GOOGLE'|'LAPU'; pincode: string; latitude: number; longitude: number; idempotencyKey: string; }
export interface RechargeOperator { name: string; label: string; type: string; operatorKey: string; }
export interface RechargeResponse { transactionId: string; transactionReference: string; status: string; providerMode: string; providerReference: string|null; account: string; amount: number; charges: number; commission: number; debitAmount: number; failureReason: string|null; receiptNumber: string|null; }
export interface AepsBank { iin: string; name: string; }
export type AepsTransactionType = 'BALANCE_INQUIRY'|'MINI_STATEMENT'|'CASH_WITHDRAWAL';
export interface AepsRequest { transactionType: AepsTransactionType; aadhaarNumber: string; mobileNumber: string; bankIin: string; deviceName: string; biometricCaptureReference: string; amount: number|null; idempotencyKey: string; }
export interface AepsResponse { transactionId: string; transactionReference: string; transactionType: string; status: string; providerMode: string; providerReference: string|null; maskedAadhaar: string; amount: number|null; charges: number; commission: number; failureReason: string|null; receiptNumber: string|null; }
export interface WalletTransferReceiver { userId: string; organizationUnitId: string; displayName: string; mobile: string; organizationCode: string; organizationType: string; }
export interface WalletTransferRequest { receiverMobile: string; amount: number; idempotencyKey: string; }
export interface WalletTransferResponse { transactionId: string; transactionReference: string; status: string; receiverName: string; receiverMobile: string; amount: number; charges: number; debitAmount: number; creditAmount: number; failureReason: string|null; receiptNumber: string|null; }
export interface FundRequestResponse { fundRequestId: string; transactionId: string; transactionReference: string; status: string; amount: number; paymentMode: string; externalReference: string; proofUrl: string|null; requestedByUserId: string; requesterName: string; organizationCode: string; createdAtUtc: string; reviewedAtUtc: string|null; reviewReason: string|null; walletCredited: boolean; receiptNumber: string|null; canReview: boolean; }
export interface TransactionHistoryItem { transactionId: string; transactionReference: string; service: string; provider: string|null; amount: number; charges: number; commission: number; status: string; providerReference: string|null; createdAtUtc: string; }
export interface UserListItem { userId: string; username: string; email: string; mobile: string|null; displayName: string; isActive: boolean; mustChangePassword: boolean; roles: string[]; memberships: Membership[]; legacySystem: string|null; legacyId: string|null; }
export interface UserPage { items: UserListItem[]; page: number; pageSize: number; total: number; totalPages: number; }
export interface ServiceDefinition { serviceId: string; code: string; name: string; description: string|null; category: string; isActive: boolean; isChargeable: boolean; isCommissionable: boolean; requiresKyc: boolean; sortOrder: number; }
