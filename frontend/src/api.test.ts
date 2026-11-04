import { describe, expect, it, vi } from 'vitest'
import { ApiError, getAiConsent, getHealth, getMe, getSecurityEvents, login, logout, logoutAll, register, setAiConsent } from './api'

const asFetch = (f: unknown) => f as unknown as typeof fetch

describe('getHealth', () => {
  it('returns parsed health payload', async () => {
    const f = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ status: 'healthy', database: true }) })
    expect(await getHealth(asFetch(f))).toEqual({ status: 'healthy', database: true })
  })

  it('throws on a non-ok response', async () => {
    const f = vi.fn().mockResolvedValue({ ok: false, status: 500 })
    await expect(getHealth(asFetch(f))).rejects.toThrow('500')
  })
})

describe('auth api', () => {
  it('login posts credentials as JSON with the CSRF header and returns no token', async () => {
    const f = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ expiresAt: 'x', email: 'a@b.com' }) })
    const res = await login('a@b.com', 'pw', undefined, asFetch(f))
    expect(res).toEqual({ expiresAt: 'x', email: 'a@b.com' })
    const [url, init] = f.mock.calls[0]
    expect(init.headers['X-Requested-With']).toBe('PennyPath')
    expect(init.credentials).toBe('same-origin')
    expect(url).toMatch(/\/api\/auth\/login$/)
    expect(JSON.parse(init.body)).toEqual({ email: 'a@b.com', password: 'pw' })
  })

  it('register surfaces the server error message and status', async () => {
    const f = vi.fn().mockResolvedValue({ ok: false, status: 409, json: async () => ({ error: 'exists' }) })
    const err = await register('a@b.com', 'password1', asFetch(f)).catch((e) => e)
    expect(err).toBeInstanceOf(ApiError)
    expect(err.message).toBe('exists')
    expect(err.status).toBe(409)
  })

  it('shows a friendly message when rate limited', async () => {
    const f = vi.fn().mockResolvedValue({
      ok: false,
      status: 429,
      json: async () => {
        throw new Error('no body')
      },
    })
    await expect(login('a@b.com', 'x', undefined, asFetch(f))).rejects.toThrow(/Too many attempts/)
  })

  it('getMe relies on the session cookie, not a token', async () => {
    const f = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ id: '1', email: 'a@b.com' }) })
    await getMe(asFetch(f))
    const [url, init] = f.mock.calls[0]
    expect(url).toMatch(/\/api\/auth\/me$/)
    expect(init.headers.Authorization).toBeUndefined()
    expect(init.credentials).toBe('same-origin')
  })

  it('getMe rejects with the status when there is no valid session', async () => {
    const f = vi.fn().mockResolvedValue({ ok: false, status: 401, json: async () => null })
    await expect(getMe(asFetch(f))).rejects.toMatchObject({ status: 401 })
  })

  it('logout and logoutAll POST with the CSRF header and accept an empty response', async () => {
    const f = vi.fn().mockResolvedValue({ ok: true, status: 204, json: async () => { throw new Error('no body') } })
    await expect(logout(asFetch(f))).resolves.toBeUndefined()
    await expect(logoutAll(asFetch(f))).resolves.toBeUndefined()
    expect(f.mock.calls.map(([u, i]) => [i.method, String(u).replace(/^.*?\/api/, '/api')])).toEqual([
      ['POST', '/api/auth/logout'],
      ['POST', '/api/auth/logout-all'],
    ])
    expect(f.mock.calls.every(([, i]) => i.headers['X-Requested-With'] === 'PennyPath')).toBe(true)
  })

  it('AI consent and security events use the privacy endpoints', async () => {
    const f = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ enabled: false }) })
    expect(await getAiConsent(asFetch(f))).toEqual({ enabled: false })
    await setAiConsent(true, asFetch(f))
    await getSecurityEvents(asFetch(f))
    expect(f.mock.calls.map(([u, i]) => [i.method ?? 'GET', String(u).replace(/^.*?\/api/, '/api')])).toEqual([
      ['GET', '/api/privacy/ai'],
      ['PUT', '/api/privacy/ai'],
      ['GET', '/api/security/events'],
    ])
    expect(JSON.parse(f.mock.calls[1][1].body)).toEqual({ enabled: true })
  })
})
