import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { TokenResponse, UserSummary } from './models';

const sessionKey = 'proxytype.session';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly session = signal<TokenResponse|null>(this.readSession());
  readonly user = computed(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed(() => Boolean(this.session()?.accessToken));
  readonly accessToken = computed(() => this.session()?.accessToken ?? null);

  login(username: string, password: string): Observable<TokenResponse> {
    return this.http.post<TokenResponse>('/api/auth/login', { username, password }).pipe(tap(value => this.store(value)));
  }
  refresh(): Observable<TokenResponse> {
    return this.http.post<TokenResponse>('/api/auth/refresh', { refreshToken: this.session()?.refreshToken ?? '' }).pipe(tap(value => this.store(value)));
  }
  loadCurrentUser(): Observable<UserSummary> {
    return this.http.get<UserSummary>('/api/auth/me').pipe(tap(user => { const value = this.session(); if (value) this.store({ ...value, user }); }));
  }
  changePassword(currentPassword: string, newPassword: string, confirmPassword: string): Observable<void> {
    return this.http.post<void>('/api/auth/change-password', { currentPassword, newPassword, confirmPassword });
  }
  logout(): void {
    const refreshToken = this.session()?.refreshToken;
    if (refreshToken) this.http.post('/api/auth/logout', { refreshToken }).subscribe({ error: () => undefined });
    this.clear();
  }
  clear(): void { sessionStorage.removeItem(sessionKey); this.session.set(null); }
  private store(value: TokenResponse): void { sessionStorage.setItem(sessionKey, JSON.stringify(value)); this.session.set(value); }
  private readSession(): TokenResponse|null {
    try { const value = sessionStorage.getItem(sessionKey); return value ? JSON.parse(value) as TokenResponse : null; }
    catch { sessionStorage.removeItem(sessionKey); return null; }
  }
}
