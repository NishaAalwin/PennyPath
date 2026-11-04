import { describe, expect, it, vi } from 'vitest'
import { deleteAccount, fetchExport, mfaDisable, mfaEnable, mfaSetup } from './account'
import { forgotPassword, login, resendVerification, resetPassword, verifyEmail } from './api'
import { groupSecret, newPasswordProblem, parseHashAction } from './links'

const asFetch = (f: unknown) => f as unknown as typeof fetch
const ok = (body: unknown = {}, status = 200) => vi.fn().mockResolvedValue({ ok: true, status, json: async () => body })

describe('parseHashAction', () => {
  const token = 'abcdefghijklmnopqrstuvwxyz0123456789_-ABCDE'

  it('reads the verify and reset links from the part after the #', () => {
    expect(parseHashAction(`#verify=${token}`)).toEqual({ kind: 'verify', token })
    expect(parseHashAction(`#reset=${token}`)).toEqual({ kind: 'reset', token })
  })

  it('ignores anything else, including odd or oversized tokens', () => {
    expect(parseHashAction('')).toBeNull()
    expect(parseHashAction('#')).toBeNull()
    expect(parseHashAction('#verify=short')).toBeNull()
    expect(parseHashAction(`#verify=${token}&x=1`)).toBeNull()
    expect(parseHashAction(`#other=${token}`)).toBeNull()
    expect(parseHashAction(`#reset=${'a'.repeat(101)}`)).toBeNull()
    expect(parseHashAction('#reset=<script>alert(1)</script>aaaaaaaaaaaaaaaa')).toBeNull()
  })
})

describe('helpers', () => {
  it('groups a setup key in fours', () => {
    expect(groupSecret('GEZDGNBVGY3TQOJQ')).toBe('GEZD GNBV GY3T QOJQ')
  })

  it('checks a new password before it is sent', () => {
    expect(newPasswordProblem('short', 'short')).toMatch(/at least 8/)
    expect(newPasswordProblem('x'.repeat(129), 'x'.repeat(129))).toMatch(/at most 128/)
    expect(newPasswordProblem('long-enough-1', 'different-1')).toMatch(/do not match/)
    expect(newPasswordProblem('long-enough-1', 'long-enough-1')).toBeNull()
  })
})

describe('sign-in api', () => {
  it('sends the code only when there is one', async () => {
    const f = ok({ email: 'a@b.com', expiresAt: 'x' })

    await login('a@b.com', 'pw', undefined, asFetch(f))
    await login('a@b.com', 'pw', '123456', asFetch(f))

    expect(JSON.parse(f.mock.calls[0][1].body)).toEqual({ email: 'a@b.com', password: 'pw' })
    expect(JSON.parse(f.mock.calls[1][1].body)).toEqual({ email: 'a@b.com', password: 'pw', totpCode: '123456' })
  })

  it('passes the "code needed" answer through', async () => {
    expect(await login('a@b.com', 'pw', undefined, asFetch(ok({ mfaRequired: true })))).toEqual({ mfaRequired: true })
  })
})

describe('email and password api', () => {
  it('posts the token in the body with the app header, never in the address', async () => {
    const f = ok({}, 204)

    await verifyEmail('tok-123', asFetch(f))
    await resetPassword('tok-456', 'a-new-password', asFetch(f))

    const [verifyUrl, verifyInit] = f.mock.calls[0]
    expect(verifyUrl).toMatch(/\/api\/auth\/verify-email$/)
    expect(verifyUrl).not.toContain('tok-123')
    expect(verifyInit.method).toBe('POST')
    expect(verifyInit.headers['X-Requested-With']).toBe('PennyPath')
    expect(JSON.parse(verifyInit.body)).toEqual({ token: 'tok-123' })
    expect(JSON.parse(f.mock.calls[1][1].body)).toEqual({ token: 'tok-456', newPassword: 'a-new-password' })
    expect(f.mock.calls[1][0]).not.toContain('tok-456')
  })

  it('asks for a reset link and a new verification link', async () => {
    const f = ok({ message: 'ok' }, 202)

    expect(await forgotPassword('a@b.com', asFetch(f))).toEqual({ message: 'ok' })
    await resendVerification(asFetch(f))

    expect(JSON.parse(f.mock.calls[0][1].body)).toEqual({ email: 'a@b.com' })
    expect(f.mock.calls[1][0]).toMatch(/resend-verification$/)
  })

  it('shows the server message when a link is refused', async () => {
    const f = vi.fn().mockResolvedValue({ ok: false, status: 400, json: async () => ({ error: 'This link is invalid or has expired.' }) })

    await expect(verifyEmail('x', asFetch(f))).rejects.toMatchObject({ message: 'This link is invalid or has expired.', status: 400 })
  })
})

describe('account api', () => {
  it('sets up, enables and disables two-factor login', async () => {
    const f = ok({ secret: 'S', otpAuthUri: 'otpauth://x', recoveryCodes: ['a-b'] })

    expect((await mfaSetup(asFetch(f))).secret).toBe('S')
    expect((await mfaEnable('123456', asFetch(f))).recoveryCodes).toEqual(['a-b'])
    await mfaDisable('pw', '654321', asFetch(f))

    expect(f.mock.calls.map((c) => c[0])).toEqual([
      expect.stringMatching(/\/mfa\/setup$/), expect.stringMatching(/\/mfa\/enable$/), expect.stringMatching(/\/mfa\/disable$/),
    ])
    expect(JSON.parse(f.mock.calls[1][1].body)).toEqual({ code: '123456' })
    expect(JSON.parse(f.mock.calls[2][1].body)).toEqual({ password: 'pw', code: '654321' })
  })

  it('deletes with the password, and the code only when there is one', async () => {
    const f = ok({}, 204)

    await deleteAccount('pw', undefined, asFetch(f))
    await deleteAccount('pw', '123456', asFetch(f))

    expect(JSON.parse(f.mock.calls[0][1].body)).toEqual({ password: 'pw' })
    expect(JSON.parse(f.mock.calls[1][1].body)).toEqual({ password: 'pw', code: '123456' })
    expect(f.mock.calls[0][1].method).toBe('POST')
  })

  it('reports why deleting was refused, with the status', async () => {
    const f = vi.fn().mockResolvedValue({ ok: false, status: 409, json: async () => ({ error: 'Enter the code', mfaRequired: true }) })

    await expect(deleteAccount('pw', undefined, asFetch(f))).rejects.toMatchObject({ status: 409 })
  })

  it('fetches the export as a file with the cookie and the app header', async () => {
    const blob = new Blob(['{}'])
    const f = vi.fn().mockResolvedValue({ ok: true, blob: async () => blob })

    expect(await fetchExport(asFetch(f))).toBe(blob)

    const [url, init] = f.mock.calls[0]
    expect(url).toMatch(/\/api\/account\/export$/)
    expect(init.credentials).toBe('same-origin')
    expect(init.headers['X-Requested-With']).toBe('PennyPath')
  })

  it('shows an error when the export fails', async () => {
    const f = vi.fn().mockResolvedValue({ ok: false, status: 403, json: async () => ({ error: 'nope' }) })

    await expect(fetchExport(asFetch(f))).rejects.toMatchObject({ message: 'nope', status: 403 })
  })
})
