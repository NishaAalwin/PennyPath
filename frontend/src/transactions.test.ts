import { describe, expect, it, vi } from 'vitest'
import { importSms, listTransactions, setCategory, splitMessages } from './transactions'

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
  it('importSms posts messages with the bearer token', async () => {
    const f = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ imported: 1, duplicates: 0, unparsed: [] }) })
    const res = await importSms('tok', ['a', 'b'], asFetch(f))
    expect(res.imported).toBe(1)
    const [url, init] = f.mock.calls[0]
    expect(url).toMatch(/\/api\/sms\/import$/)
    expect(init.headers.Authorization).toBe('Bearer tok')
    expect(JSON.parse(init.body)).toEqual({ messages: ['a', 'b'] })
  })

  it('listTransactions adds the month filter', async () => {
    const f = vi.fn().mockResolvedValue({ ok: true, json: async () => [] })
    await listTransactions('tok', '2026-11', asFetch(f))
    expect(f.mock.calls[0][0]).toMatch(/\/api\/transactions\?month=2026-11$/)
  })

  it('surfaces the server error', async () => {
    const f = vi.fn().mockResolvedValue({ ok: false, status: 400, json: async () => ({ error: 'bad' }) })
    await expect(importSms('tok', ['x'], asFetch(f))).rejects.toThrow('bad')
  })

  it('setCategory PUTs the category and accepts an empty 204 response', async () => {
    const f = vi.fn().mockResolvedValue({ ok: true, status: 204, json: async () => { throw new Error('no body') } })
    await expect(setCategory('tok', 'abc', 'Groceries', asFetch(f))).resolves.toBeUndefined()
    const [url, init] = f.mock.calls[0]
    expect(url).toMatch(/\/api\/transactions\/abc\/category$/)
    expect(init.method).toBe('PUT')
    expect(JSON.parse(init.body)).toEqual({ category: 'Groceries' })
  })
})
