import { describe, expect, it, vi } from 'vitest'
import { describeSync, disconnectGmail, getGmailStatus, readGmailReturn, startGmailConnect, syncGmail, type GmailSyncResult } from './gmail'

const asFetch = (f: unknown) => f as unknown as typeof fetch
const result = (over: Partial<GmailSyncResult> = {}): GmailSyncResult => ({
  checked: 5, imported: 3, duplicates: 1, possibleDuplicates: 0, notRecognised: 1, unverifiedSender: 0, notRecognisedSubjects: ['Statement'], moreAvailable: false, ...over,
})

describe('gmail api', () => {
  it('sends changing requests with the app header and the session cookie, and never an Authorization token', async () => {
    const f = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ url: 'https://accounts.google.com/x' }) })

    await startGmailConnect(asFetch(f))
    await syncGmail(asFetch(f))

    for (const [, init] of f.mock.calls) {
      expect(init.method).toBe('POST')
      expect(init.headers['X-Requested-With']).toBe('PennyPath')
      expect(init.headers.Authorization).toBeUndefined()
      expect(init.credentials).toBe('same-origin')
    }
    expect(f.mock.calls[0][0]).toMatch(/\/api\/gmail\/connect$/)
    expect(f.mock.calls[1][0]).toMatch(/\/api\/gmail\/sync$/)
  })

  it('reads the status with GET and disconnects with DELETE', async () => {
    const f = vi.fn().mockResolvedValueOnce({ ok: true, status: 200, json: async () => ({ configured: true, connected: false }) })
      .mockResolvedValueOnce({ ok: true, status: 204 })

    expect(await getGmailStatus(asFetch(f))).toEqual({ configured: true, connected: false })
    await disconnectGmail(asFetch(f))

    expect(f.mock.calls[0][1].method).toBeUndefined()
    expect(f.mock.calls[1][1].method).toBe('DELETE')
  })

  it('shows the server message and status when a sync is refused', async () => {
    const f = vi.fn().mockResolvedValue({ ok: false, status: 409, json: async () => ({ error: 'Connect Gmail first.' }) })

    await expect(syncGmail(asFetch(f))).rejects.toMatchObject({ message: 'Connect Gmail first.', status: 409 })
  })
})

describe('describeSync', () => {
  it('says plainly what happened', () => {
    expect(describeSync(result())).toBe('Imported 3, skipped 1 already imported, could not read 1.')
  })

  it('mentions possible duplicates and leftovers', () => {
    expect(describeSync(result({ possibleDuplicates: 2, moreAvailable: true }))).toContain('2 look like duplicates')
    expect(describeSync(result({ moreAvailable: true }))).toContain('sync again')
  })

  it('says when emails were skipped because the sender could not be verified', () => {
    expect(describeSync(result({ unverifiedSender: 2 }))).toContain('skipped 2 whose sender Gmail could not verify');
  })

  it('says so when there was nothing new', () => {
    expect(describeSync(result({ checked: 0, imported: 0, duplicates: 0, notRecognised: 0, unverifiedSender: 0, notRecognisedSubjects: [] }))).toBe('No new bank emails found.')
  })
})

describe('readGmailReturn', () => {
  it('reads only the two values the server sends back', () => {
    expect(readGmailReturn('?gmail=connected')).toBe('connected')
    expect(readGmailReturn('?gmail=failed')).toBe('failed')
    expect(readGmailReturn('?gmail=<script>')).toBeNull()
    expect(readGmailReturn('')).toBeNull()
  })
})
