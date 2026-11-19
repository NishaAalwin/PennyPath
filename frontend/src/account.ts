import { API_URL, ApiError, APP_HEADERS } from './api'
import { authed } from './transactions'

type Fetch = typeof fetch

export type AccountInfo = { email: string; emailVerified: boolean; mfaEnabled: boolean; gmailConnected: boolean }
export type MfaSetup = { secret: string; otpAuthUri: string }

export const getAccount = (fetchFn: Fetch = fetch) => authed<AccountInfo>('/api/account', {}, fetchFn)

export const mfaSetup = (fetchFn: Fetch = fetch) => authed<MfaSetup>('/api/account/mfa/setup', { method: 'POST' }, fetchFn)

/** Returns the one-time recovery codes. They are never shown again. */
export const mfaEnable = (code: string, fetchFn: Fetch = fetch) =>
  authed<{ recoveryCodes: string[] }>('/api/account/mfa/enable', { method: 'POST', body: JSON.stringify({ code }) }, fetchFn)

export const mfaDisable = (password: string, code: string, fetchFn: Fetch = fetch) =>
  authed<void>('/api/account/mfa/disable', { method: 'POST', body: JSON.stringify({ password, code }) }, fetchFn)

/** Deletes the account and everything in it. Needs the password (and a code when two-factor is on). */
export const deleteAccount = (password: string, code: string | undefined, fetchFn: Fetch = fetch) =>
  authed<void>('/api/account/delete', { method: 'POST', body: JSON.stringify(code ? { password, code } : { password }) }, fetchFn)

/** Fetches the user's data as a file the page can offer for download. */
export async function fetchExport(fetchFn: Fetch = fetch): Promise<Blob> {
  const res = await fetchFn(`${API_URL}/api/account/export`, { credentials: 'same-origin', headers: { ...APP_HEADERS } })
  if (!res.ok) {
    const body = await res.json().catch(() => null)
    throw new ApiError(body?.error ?? 'Could not prepare your data.', res.status)
  }
  return res.blob()
}

/** Saves a blob through a temporary link, then forgets it. */
export function saveBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
