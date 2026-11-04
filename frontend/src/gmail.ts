import { authed } from './transactions'

type Fetch = typeof fetch

export type GmailStatus = { configured: boolean; connected: boolean; connectedAt: string | null; lastSyncAt: string | null }

export type GmailSyncResult = {
  checked: number
  imported: number
  duplicates: number
  possibleDuplicates: number
  notRecognised: number
  notRecognisedSubjects: string[]
  moreAvailable: boolean
}

export const getGmailStatus = (fetchFn: Fetch = fetch) => authed<GmailStatus>('/api/gmail', {}, fetchFn)

/** Returns Google's consent address; the page then navigates there. */
export const startGmailConnect = (fetchFn: Fetch = fetch) =>
  authed<{ url: string }>('/api/gmail/connect', { method: 'POST' }, fetchFn)

export const syncGmail = (fetchFn: Fetch = fetch) => authed<GmailSyncResult>('/api/gmail/sync', { method: 'POST' }, fetchFn)

export const disconnectGmail = (fetchFn: Fetch = fetch) => authed<void>('/api/gmail', { method: 'DELETE' }, fetchFn)

/** Where the sync result is explained in words. */
export function describeSync(r: GmailSyncResult): string {
  if (r.checked === 0 && r.duplicates === 0 && r.notRecognised === 0) return 'No new bank emails found.'
  const parts = [`Imported ${r.imported}`]
  if (r.duplicates > 0) parts.push(`skipped ${r.duplicates} already imported`)
  if (r.possibleDuplicates > 0) parts.push(`${r.possibleDuplicates} look like duplicates (check the list)`)
  if (r.notRecognised > 0) parts.push(`could not read ${r.notRecognised}`)
  return parts.join(', ') + (r.moreAvailable ? '. There are more emails: sync again to continue.' : '.')
}

/** The result of the Google redirect is carried back in the address (?gmail=connected or ?gmail=failed). */
export function readGmailReturn(search: string): 'connected' | 'failed' | null {
  const v = new URLSearchParams(search).get('gmail')
  return v === 'connected' || v === 'failed' ? v : null
}
