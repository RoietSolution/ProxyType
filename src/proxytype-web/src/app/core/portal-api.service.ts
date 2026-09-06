import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AepsBank, AepsRequest, AepsResponse, ChannelUserDetails, ChargeSlab, DashboardData, EffectiveService, FinoDmtRequest, FinoDmtResponse, FundRequestInstructions, FundRequestResponse, OrganizationUnit, PricingMetadata, PricingRoleOption, RechargeCommission, RechargeOperator, RechargeRequest, RechargeResponse, ServiceDefinition, TransactionHistoryItem, UpiFundingRequest, UpiFundingResponse, UserDocumentItem, UserDocumentOwner, UserListItem, UserPage, WalletBalance, WalletTransferReceiver, WalletTransferRequest, WalletTransferResponse } from './models';

@Injectable({ providedIn: 'root' })
export class PortalApiService {
  private readonly http = inject(HttpClient);
  dashboard() { return this.http.get<DashboardData>('/api/dashboard'); }
  walletBalance() { return this.http.get<WalletBalance>('/api/wallet/balance'); }
  organizationTree() { return this.http.get<OrganizationUnit[]>('/api/organizations/tree'); }
  services() { return this.http.get<ServiceDefinition[]>('/api/services'); }
  effectiveServices(organizationId: string) { return this.http.get<EffectiveService[]>(`/api/organizations/${organizationId}/services`); }
  setPermission(organizationId: string, serviceId: string, effect: 'ALLOW'|'DENY') {
    return this.http.put<EffectiveService>(`/api/organizations/${organizationId}/services/${serviceId}`, { effect, reason: 'Updated from the administration portal' });
  }
  finoDmt(request: FinoDmtRequest) { return this.http.post<FinoDmtResponse>('/api/services/fino-dmt/transfers', request); }
  upiFundingOrder(request: UpiFundingRequest) { return this.http.post<UpiFundingResponse>('/api/services/upi-transfer/orders', request); }
  upiFundingStatus(transactionId: string) { return this.http.get<UpiFundingResponse>(`/api/services/upi-transfer/orders/${transactionId}/status`); }
  aepsBanks() { return this.http.get<AepsBank[]>('/api/services/aeps/banks'); }
  aeps(request: AepsRequest) { return this.http.post<AepsResponse>('/api/services/aeps/transactions', request); }
  rechargeOperators(type: string) { return this.http.get<RechargeOperator[]>('/api/services/recharge/operators', { params: { type } }); }
  recharge(request: RechargeRequest) { return this.http.post<RechargeResponse>('/api/services/recharge/transactions', request); }
  walletReceiver(mobile: string) { return this.http.get<WalletTransferReceiver>('/api/services/wallet-to-wallet/receivers', { params: { mobile } }); }
  walletTransfer(request: WalletTransferRequest) { return this.http.post<WalletTransferResponse>('/api/services/wallet-to-wallet/transfers', request); }
  fundRequests() { return this.http.get<FundRequestResponse[]>('/api/services/fund-request/requests'); }
  fundRequestInstructions() { return this.http.get<FundRequestInstructions>('/api/services/fund-request/instructions'); }
  createFundRequest(form: FormData) { return this.http.post<FundRequestResponse>('/api/services/fund-request/requests', form); }
  reviewFundRequest(id: string, decision: 'APPROVED'|'REJECTED', reason?: string) { return this.http.post<FundRequestResponse>(`/api/services/fund-request/requests/${id}/review`, { decision, reason }); }
  chargeSlabMetadata() { return this.http.get<PricingMetadata>('/api/charge-slabs/metadata'); }
  chargeSlabs(serviceId: string, roleId: string) { return this.http.get<ChargeSlab[]>('/api/charge-slabs', { params: { serviceId, roleId } }); }
  saveChargeSlab(request: { pricingRuleId: string|null; serviceId: string; roleId: string; amountFrom: number; amountTo: number; calculationType: 'FIXED'|'PERCENTAGE'; rate: number; tdsRate: number; gstRate: number }) { return this.http.post<ChargeSlab>('/api/charge-slabs', request); }
  deleteChargeSlab(id: string) { return this.http.delete<void>(`/api/charge-slabs/${id}`); }
  rechargeCommissionRoles() { return this.http.get<PricingRoleOption[]>('/api/recharge-commissions/roles'); }
  rechargeCommissions(scope: { roleId?: string; userId?: string }) { return this.http.get<RechargeCommission[]>('/api/recharge-commissions', { params: scope }); }
  saveRechargeCommissions(scope: { roleId?: string; userId?: string }, values: RechargeCommission[]) { return this.http.put<RechargeCommission[]>('/api/recharge-commissions', { ...scope, operators: values.map(x => ({ operatorId: x.operatorId, calculationType: x.calculationType, rate: x.rate, useUserOverride: x.isUserOverride })) }); }
  users(filters: { search?: string; active?: boolean; unitType?: string; page?: number; pageSize?: number }) { let params = new HttpParams().set('page', filters.page || 1).set('pageSize', filters.pageSize || 50); if (filters.search) params = params.set('search', filters.search); if (filters.active !== undefined) params = params.set('active', filters.active); if (filters.unitType) params = params.set('unitType', filters.unitType); return this.http.get<UserPage>('/api/users', { params }); }
  user(userId: string) { return this.http.get<UserListItem>(`/api/users/${userId}`); }
  channelUser(userId: string) { return this.http.get<ChannelUserDetails>(`/api/users/${userId}/channel`); }
  createChannelUser(form: FormData) { return this.http.post<{userId: string; organizationUnitId: string; code: string}>('/api/users/channels', form); }
  updateChannelUser(userId: string, form: FormData) { return this.http.put(`/api/users/${userId}/channel`, form); }
  aadhaarDocument(userId: string) { return this.http.get(`/api/users/${userId}/aadhaar-document`, { responseType: 'blob' }); }
  documentOwners(unitType: string, search?: string) { let params = new HttpParams().set('unitType', unitType); if (search) params = params.set('search', search); return this.http.get<UserDocumentOwner[]>('/api/user-documents/owners', { params }); }
  userDocuments(userId: string) { return this.http.get<UserDocumentItem[]>(`/api/user-documents/users/${userId}`); }
  uploadUserDocument(userId: string, form: FormData) { return this.http.post(`/api/user-documents/users/${userId}`, form); }
  downloadUserDocument(userId: string, documentId: string) { return this.http.get(`/api/user-documents/users/${userId}/${documentId}/download`, { responseType: 'blob' }); }
  disableUser(userId: string) { return this.http.delete(`/api/users/${userId}`); }
  resetUserPassword(userId: string, newPassword: string) { return this.http.post(`/api/users/${userId}/reset-password`, { newPassword }); }
  transactions(filters: { service?: string; status?: string; search?: string; page: number }) {
    let params = new HttpParams().set('page', filters.page);
    if (filters.service) params = params.set('service', filters.service);
    if (filters.status) params = params.set('status', filters.status);
    if (filters.search) params = params.set('search', filters.search);
    return this.http.get<TransactionHistoryItem[]>('/api/transactions', { params });
  }
}
