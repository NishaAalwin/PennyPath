import { API_URL, ApiError } from './api'

export type Transaction = {
  id: string
  date: string
  amount: number
  type: 'Income' | 'Expense'
  merchant: string
  category: string
  account: string | null
  source: string
}

export type ImportResult = { imported: number; duplicates: number; unparsed: string[] }

type Fetch = typeof fetch

/** SMS messages are separated by a blank line when pasted; a single message may span lines. */
export function splitMessages(text: string): string[] {
  return text
    .split(/\r?\n\s*\r?\n/)
    .map((m) => m.trim())
    .filter(Boolean)
}

async function authed<T>(token: string, path: string, init: RequestInit, fetchFn: Fetch): Promise<T> {
  const res = await fetchFn(`${API_URL}${path}`, {
    ...init,
    headers: { ...init.headers, Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
  })
  if (!res.ok) {
    const body = await res.json().catch(() => null)
    throw new ApiError(body?.error ?? 'Something went wrong.', res.status)
  }
  return res.json()
}

export const importSms = (token: string, messages: string[], fetchFn: Fetch = fetch) =>
  authed<ImportResult>(token, '/api/sms/import', { method: 'POST', body: JSON.stringify({ messages }) }, fetchFn)

export const listTransactions = (token: string, month?: string, fetchFn: Fetch = fetch) =>
  authed<Transaction[]>(token, `/api/transactions${month ? `?month=${month}` : ''}`, {}, fetchFn)
