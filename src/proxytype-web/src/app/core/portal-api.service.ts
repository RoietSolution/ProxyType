import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { AepsBank, AepsRequest, AepsResponse, DashboardData, EffectiveService, FinoDmtRequest, FinoDmtResponse, FundRequestResponse, OrganizationUnit, RechargeOperator, RechargeRequest, RechargeResponse, ServiceDefinition, TransactionHistoryItem, UpiFundingRequest, UpiFundingResponse, UserListItem, UserPage, WalletTransferReceiver, WalletTransferRequest, WalletTransferResponse } from './models';

@Injectable({ providedIn: 'root' })
export class PortalApiService {
  private readonly http = inject(HttpClient);
  dashboard() { return this.http.get<DashboardData>('/api/dashboard'); }
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
  createFundRequest(form: FormData) { return this.http.post<FundRequestResponse>('/api/services/fund-request/requests', form); }
  reviewFundRequest(id: string, decision: 'APPROVED'|'REJECTED', reason?: string) { return this.http.post<FundRequestResponse>(`/api/services/fund-request/requests/${id}/review`, { decision, reason }); }
  users(filters: { search?: string; active?: boolean; unitType?: string; page?: number; pageSize?: number }) { let params = new HttpParams().set('page', filters.page || 1).set('pageSize', filters.pageSize || 50); if (filters.search) params = params.set('search', filters.search); if (filters.active !== undefined) params = params.set('active', filters.active); if (filters.unitType) params = params.set('unitType', filters.unitType); return this.http.get<UserPage>('/api/users', { params }); }
  user(userId: string) { return this.http.get<UserListItem>(`/api/users/${userId}`); }
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
