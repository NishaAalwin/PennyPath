import { useEffect, useState, type FormEvent } from 'react'
import { forgotPassword, resendVerification, resetPassword, verifyEmail } from './api'
import { newPasswordProblem } from './links'

const message = (e: unknown) => (e instanceof Error ? e.message : 'Something went wrong.')

/** Shown while the link from the verification email is checked. */
export function VerifyEmailLanding({ token, onDone }: { token: string; onDone: () => void }) {
  const [state, setState] = useState<'working' | 'ok' | 'failed'>('working')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let live = true
    verifyEmail(token)
      .then(() => live && setState('ok'))
      .catch((e) => {
        if (!live) return
        setError(message(e))
        setState('failed')
      })
    return () => {
      live = false
    }
  }, [token])

  return (
    <section className="card auth-card">
      <h2>Confirming your email</h2>
      {state === 'working' && <p className="muted">One moment...</p>}
      {state === 'ok' && <p role="status">Your email is confirmed. Thank you.</p>}
      {state === 'failed' && <p role="alert">{error}</p>}
      {state !== 'working' && <div><button onClick={onDone}>Continue</button></div>}
    </section>
  )
}

/** Signed in, but the email address has not been confirmed yet: nothing else is available until it is. */
export function VerifyEmailScreen({ email, onRecheck }: { email: string; onRecheck: () => void }) {
  const [info, setInfo] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function resend() {
    setBusy(true)
    setError(null)
    try {
      setInfo((await resendVerification()).message)
    } catch (e) {
      setError(message(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="card auth-card">
      <h2>Confirm your email</h2>
      <p>
        We sent a link to <strong>{email}</strong>. Open it to start using PennyPath. The link works once and expires in 24 hours.
      </p>
      <p className="muted">Running locally? The email is in the Mailpit inbox at http://127.0.0.1:8025.</p>
      {info && <p role="status">{info}</p>}
      {error && <p role="alert">{error}</p>}
      <div className="actions">
        <button onClick={onRecheck}>I have confirmed it</button>
        <button className="btn-quiet" onClick={resend} disabled={busy}>Send the link again</button>
      </div>
    </section>
  )
}

/** Asks for an address and sends a reset link. The answer is the same whether or not the address has an account. */
export function ForgotPasswordForm({ onBack }: { onBack: () => void }) {
  const [email, setEmail] = useState('')
  const [sent, setSent] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      setSent((await forgotPassword(email)).message)
    } catch (err) {
      setError(message(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} className="card auth-card">
      <h2>Reset your password</h2>
      {sent ? (
        <p role="status">{sent} The link expires in 30 minutes.</p>
      ) : (
        <>
          <p className="muted">Enter your email and we will send you a link to choose a new password.</p>
          <label>
            Email
            <input type="email" required autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)}
              style={{ display: 'block', width: '100%' }} />
          </label>
          {error && <p role="alert">{error}</p>}
          <button type="submit" disabled={busy}>{busy ? 'Please wait...' : 'Send the link'}</button>
        </>
      )}
      <button type="button" onClick={onBack}>Back to log in</button>
    </form>
  )
}

/** The page the emailed reset link opens. */
export function ResetPasswordForm({ token, onDone }: { token: string; onDone: () => void }) {
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [done, setDone] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    const problem = newPasswordProblem(password, confirm)
    if (problem) return setError(problem)
    setBusy(true)
    setError(null)
    try {
      await resetPassword(token, password)
      setDone(true)
    } catch (err) {
      setError(message(err))
    } finally {
      setBusy(false)
    }
  }

  if (done) {
    return (
      <section className="card auth-card">
        <h2>Password changed</h2>
        <p role="status">Your password is changed and every device was signed out. Log in with the new password.</p>
        <div><button onClick={onDone}>Go to log in</button></div>
      </section>
    )
  }

  return (
    <form onSubmit={submit} className="card auth-card">
      <h2>Choose a new password</h2>
      <label>
        New password
        <input type="password" required minLength={8} autoComplete="new-password" value={password}
          onChange={(e) => setPassword(e.target.value)} style={{ display: 'block', width: '100%' }} />
      </label>
      <label>
        Type it again
        <input type="password" required minLength={8} autoComplete="new-password" value={confirm}
          onChange={(e) => setConfirm(e.target.value)} style={{ display: 'block', width: '100%' }} />
      </label>
      {error && <p role="alert">{error}</p>}
      <button type="submit" disabled={busy}>{busy ? 'Please wait...' : 'Change password'}</button>
      <button type="button" onClick={onDone}>Cancel</button>
    </form>
  )
}
