import { API_URL, ApiError } from './api'

export type Transaction = {
  id: string
  date: string
  amount: number
  type: 'Income' | 'Expense'
  merchant: string
  category: string
  source: string
  isTransfer: boolean
  isRefund: boolean
  refundOfId: string | null
  duplicateOfId: string | null
}

export type SummaryLine = { name: string; amount: number }

export type MonthlySummary = {
  month: string
  income: number
  expense: number
  savings: number
  byCategory: SummaryLine[]
  byMerchant: SummaryLine[]
  counted: number
  excluded: number
}

export type TrendMonth = { month: string; income: number; expense: number; savings: number }

export type PagedTransactions = { items: Transaction[]; total: number; page: number; pageSize: number }

export type TransactionFilters = {
  month?: string
  search?: string
  category?: string
  type?: 'Income' | 'Expense'
  page?: number
  pageSize?: number
}

export type TransactionChanges = { merchant?: string; amount?: number; date?: string; type?: 'Income' | 'Expense' }

export type ImportResult = { imported: number; duplicates: number; unparsed: string[]; possibleDuplicates: number }

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
  return (res.status === 204 ? undefined : await res.json()) as T
}

export const importSms = (token: string, messages: string[], fetchFn: Fetch = fetch) =>
  authed<ImportResult>(token, '/api/sms/import', { method: 'POST', body: JSON.stringify({ messages }) }, fetchFn)

/** Query string for the list endpoint; only filters that are set are sent, and values are URL-encoded. */
export function transactionsQuery(f: TransactionFilters): string {
  const p = new URLSearchParams()
  if (f.month) p.set('month', f.month)
  if (f.search) p.set('q', f.search)
  if (f.category) p.set('category', f.category)
  if (f.type) p.set('type', f.type)
  if (f.page) p.set('page', String(f.page))
  if (f.pageSize) p.set('pageSize', String(f.pageSize))
  const q = p.toString()
  return q ? `?${q}` : ''
}

export const listTransactions = (token: string, filters: TransactionFilters = {}, fetchFn: Fetch = fetch) =>
  authed<PagedTransactions>(token, `/api/transactions${transactionsQuery(filters)}`, {}, fetchFn)

export const updateTransaction = (token: string, id: string, changes: TransactionChanges, fetchFn: Fetch = fetch) =>
  authed<Transaction>(token, `/api/transactions/${id}`, { method: 'PUT', body: JSON.stringify(changes) }, fetchFn)

export const listCategories = (token: string, fetchFn: Fetch = fetch) =>
  authed<string[]>(token, '/api/categories', {}, fetchFn)

export const setCategory = (token: string, id: string, category: string, fetchFn: Fetch = fetch) =>
  authed<void>(token, `/api/transactions/${id}/category`, { method: 'PUT', body: JSON.stringify({ category }) }, fetchFn)

export const setTransfer = (token: string, id: string, isTransfer: boolean, fetchFn: Fetch = fetch) =>
  authed<void>(token, `/api/transactions/${id}/transfer`, { method: 'PUT', body: JSON.stringify({ isTransfer }) }, fetchFn)

export const notDuplicate = (token: string, id: string, fetchFn: Fetch = fetch) =>
  authed<void>(token, `/api/transactions/${id}/not-duplicate`, { method: 'POST' }, fetchFn)

export const deleteTransaction = (token: string, id: string, fetchFn: Fetch = fetch) =>
  authed<void>(token, `/api/transactions/${id}`, { method: 'DELETE' }, fetchFn)

export const getSummary = (token: string, month?: string, fetchFn: Fetch = fetch) =>
  authed<MonthlySummary>(token, `/api/summary${month ? `?month=${month}` : ''}`, {}, fetchFn)

export const getTrend = (token: string, to?: string, months = 6, fetchFn: Fetch = fetch) =>
  authed<TrendMonth[]>(token, `/api/trend?months=${months}${to ? `&to=${to}` : ''}`, {}, fetchFn)
