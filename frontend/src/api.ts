/** Empty means "same origin": the page and the API are served from one HTTPS address by the proxy. */
export const API_URL = import.meta.env.VITE_API_URL ?? ''

/**
 * Sent on every request. A page on another site cannot add a custom header without a CORS preflight, which the API
 * refuses, so this (with SameSite=Strict on the cookie) stops cross-site request forgery.
 */
export const APP_HEADERS = { 'X-Requested-With': 'PennyPath' }

export type Health = { status: string; database: boolean }
export type AuthResponse = { expiresAt: string; email: string }

/** Signing in either succeeds, or says the account needs its two-factor code as well. */
export type LoginResult = AuthResponse | { mfaRequired: true }
export type SecurityEvent = { id: string; type: string; at: string; ip: string | null }

export class ApiError extends Error {
  status: number
  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

type Fetch = typeof fetch

export async function getHealth(fetchFn: Fetch = fetch): Promise<Health> {
  const res = await fetchFn(`${API_URL}/api/health`)
  if (!res.ok) throw new Error(`Health check failed: ${res.status}`)
  return res.json()
}

async function postAuth<T>(path: string, payload: object, fetchFn: Fetch): Promise<T> {
  const res = await fetchFn(`${API_URL}/api/auth/${path}`, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', ...APP_HEADERS },
    body: JSON.stringify(payload),
  })
  if (!res.ok) {
    const body = await res.json().catch(() => null)
    const message = res.status === 429 ? 'Too many attempts. Try again in a minute.' : body?.error ?? 'Something went wrong.'
    throw new ApiError(message, res.status)
  }
  return res.json()
}

export const register = (email: string, password: string, fetchFn: Fetch = fetch) =>
  postAuth<AuthResponse>('register', { email, password }, fetchFn)

/** `totpCode` is the 6-digit code (or a recovery code) when the account has two-factor login on. */
export const login = (email: string, password: string, totpCode?: string, fetchFn: Fetch = fetch) =>
  postAuth<LoginResult>('login', totpCode ? { email, password, totpCode } : { email, password }, fetchFn)

async function call<T>(path: string, init: RequestInit, fetchFn: Fetch): Promise<T> {
  const res = await fetchFn(`${API_URL}${path}`, {
    ...init,
    credentials: 'same-origin',
    headers: { ...init.headers, ...APP_HEADERS, 'Content-Type': 'application/json' },
  })
  if (!res.ok) {
    const body = await res.json().catch(() => null)
    throw new ApiError(body?.error ?? 'Something went wrong.', res.status)
  }
  return (res.status === 204 ? undefined : await res.json()) as T
}

/** Who is signed in? Rejects with status 401 when there is no valid session cookie. */
export const getMe = (fetchFn: Fetch = fetch) =>
  call<{ id: string; email: string; emailVerified: boolean }>('/api/auth/me', {}, fetchFn)

/** Opens the link from the verification email. The token comes from the address after the #. */
export const verifyEmail = (token: string, fetchFn: Fetch = fetch) =>
  call<void>('/api/auth/verify-email', { method: 'POST', body: JSON.stringify({ token }) }, fetchFn)

export const resendVerification = (fetchFn: Fetch = fetch) =>
  call<{ message: string }>('/api/auth/resend-verification', { method: 'POST' }, fetchFn)

/** Always succeeds from the user's point of view: the server does not say whether the address has an account. */
export const forgotPassword = (email: string, fetchFn: Fetch = fetch) =>
  call<{ message: string }>('/api/auth/forgot-password', { method: 'POST', body: JSON.stringify({ email }) }, fetchFn)

export const resetPassword = (token: string, newPassword: string, fetchFn: Fetch = fetch) =>
  call<void>('/api/auth/reset-password', { method: 'POST', body: JSON.stringify({ token, newPassword }) }, fetchFn)

/** Ends this browser's session on the server (so a copied cookie stops working) and clears the cookie. */
export const logout = (fetchFn: Fetch = fetch) => call<void>('/api/auth/logout', { method: 'POST' }, fetchFn)

export const logoutAll = (fetchFn: Fetch = fetch) => call<void>('/api/auth/logout-all', { method: 'POST' }, fetchFn)

export const getAiConsent = (fetchFn: Fetch = fetch) => call<{ enabled: boolean }>('/api/privacy/ai', {}, fetchFn)

export const setAiConsent = (enabled: boolean, fetchFn: Fetch = fetch) =>
  call<void>('/api/privacy/ai', { method: 'PUT', body: JSON.stringify({ enabled }) }, fetchFn)

export const getSecurityEvents = (fetchFn: Fetch = fetch) => call<SecurityEvent[]>('/api/security/events', {}, fetchFn)
