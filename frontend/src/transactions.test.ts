import { describe, expect, it, vi } from 'vitest'
import { deleteTransaction, getSummary, getTrend, transactionsQuery, updateTransaction, notDuplicate, setTransfer, importSms, listTransactions, setCategory, splitMessages } from './transactions'

const asFetch = (f: unknown) => f as unknown as typeof fetch

describe('splitMessages', () => {
  it('splits on blank lines and keeps multi-line messages together', () => {
    const text = 'Rs 100 debited\nfrom A/c XX1\n\n\n  Rs 200 credited  \r\n\r\nRs 300 spent'
    expect(splitMessages(text)).toEqual(['Rs 100 debited\nfrom A/c XX1', 'Rs 200 credited', 'Rs 300 spent'])
  })

  it('returns an empty list for blank input', () => {
    expect(splitMessages('  \n\n  ')).toEqual([])
  })
})

describe('transactions api', () => {
  it('importSms posts messages with the session cookie and CSRF header, never a token', async () => {
    const f = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ imported: 1, duplicates: 0, unparsed: [] }) })
    const res = await importSms(['a', 'b'], asFetch(f))
    expect(res.imported).toBe(1)
    const [url, init] = f.mock.calls[0]
    expect(url).toMatch(/\/api\/sms\/import$/)
    expect(init.headers.Authorization).toBeUndefined()
    expect(init.headers['X-Requested-With']).toBe('PennyPath')
    expect(init.credentials).toBe('same-origin')
    expect(JSON.parse(init.body)).toEqual({ messages: ['a', 'b'] })
  })

  it('listTransactions adds the month filter', async () => {
    const f = vi.fn().mockResolvedValue({ ok: true, json: async () => [] })
    await listTransactions({ month: '2026-11' }, asFetch(f))
    expect(f.mock.calls[0][0]).toMatch(/\/api\/transactions\?month=2026-11$/)
  })

  it('surfaces the server error', async () => {
    const f = vi.fn().mockResolvedValue({ ok: false, status: 400, json: async () => ({ error: 'bad' }) })
    await expect(importSms(['x'], asFetch(f))).rejects.toThrow('bad')
  })

  it('setCategory PUTs the category and accepts an empty 204 response', async () => {
    const f = vi.fn().mockResolvedValue({ ok: true, status: 204, json: async () => { throw new Error('no body') } })
    await expect(setCategory('abc', 'Groceries', asFetch(f))).resolves.toBeUndefined()
    const [url, init] = f.mock.calls[0]
    expect(url).toMatch(/\/api\/transactions\/abc\/category$/)
    expect(init.method).toBe('PUT')
    expect(JSON.parse(init.body)).toEqual({ category: 'Groceries' })
  })

  const noContent = () => vi.fn().mockResolvedValue({ ok: true, status: 204, json: async () => { throw new Error('no body') } })

  it('setTransfer, notDuplicate and deleteTransaction use the right verbs and paths', async () => {
    const f = noContent()
    await setTransfer('a', true, asFetch(f))
    await notDuplicate('b', asFetch(f))
    await deleteTransaction('c', asFetch(f))

    const calls = f.mock.calls.map(([url, init]) => [init.method, String(url).replace(/^.*?\/api/, '/api')])
    expect(calls).toEqual([
      ['PUT', '/api/transactions/a/transfer'],
      ['POST', '/api/transactions/b/not-duplicate'],
      ['DELETE', '/api/transactions/c'],
    ])
    expect(JSON.parse(f.mock.calls[0][1].body)).toEqual({ isTransfer: true })
  })

  it('getSummary asks for the month and returns the summary', async () => {
    const body = { month: '2026-11', income: 1, expense: 2, savings: -1, byCategory: [], byMerchant: [], counted: 3, excluded: 0 }
    const f = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => body })
    expect(await getSummary('2026-11', asFetch(f))).toEqual(body)
    expect(f.mock.calls[0][0]).toMatch(/\/api\/summary\?month=2026-11$/)
  })

  it('getTrend asks for the months ending at the chosen month', async () => {
    const f = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] })
    await getTrend('2026-11', 6, asFetch(f))
    expect(f.mock.calls[0][0]).toMatch(/\/api\/trend\?months=6&to=2026-11$/)
  })

  it('transactionsQuery sends only the filters that are set and encodes them', () => {
    expect(transactionsQuery({})).toBe('')
    expect(transactionsQuery({ search: 'a&b=c d', type: 'Income', page: 2, pageSize: 25 })).toBe('?q=a%26b%3Dc+d&type=Income&page=2&pageSize=25')
    expect(transactionsQuery({ month: '2026-11', category: 'Food & Dining' })).toBe('?month=2026-11&category=Food+%26+Dining')
  })

  it('updateTransaction PUTs only the given changes and returns the row', async () => {
    const row = { id: 'abc', amount: 300 }
    const f = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => row })
    expect(await updateTransaction('abc', { amount: 300 }, asFetch(f))).toEqual(row)
    const [url, init] = f.mock.calls[0]
    expect(url).toMatch(/\/api\/transactions\/abc$/)
    expect(init.method).toBe('PUT')
    expect(JSON.parse(init.body)).toEqual({ amount: 300 })
  })

  it('updateTransaction surfaces the server message on a refused edit', async () => {
    const f = vi.fn().mockResolvedValue({ ok: false, status: 400, json: async () => ({ error: 'Amount must be more than 0.' }) })
    await expect(updateTransaction('abc', { amount: 0 }, asFetch(f))).rejects.toThrow('Amount must be more than 0.')
  })
})
