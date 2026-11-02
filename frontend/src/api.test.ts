import { describe, expect, it, vi } from 'vitest'
import { ApiError, getHealth, getMe, login, register } from './api'

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
  it('login posts credentials as JSON and returns the token', async () => {
    const f = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ token: 't', expiresAt: 'x', email: 'a@b.com' }) })
    const res = await login('a@b.com', 'pw', asFetch(f))
    expect(res.token).toBe('t')
    const [url, init] = f.mock.calls[0]
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
    await expect(login('a@b.com', 'x', asFetch(f))).rejects.toThrow(/Too many attempts/)
  })

  it('getMe sends the bearer token', async () => {
    const f = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ id: '1', email: 'a@b.com' }) })
    await getMe('tok', asFetch(f))
    expect(f.mock.calls[0][1].headers.Authorization).toBe('Bearer tok')
  })
})
