import { useState, type FormEvent } from 'react'
import { login, register } from './api'

type Props = { onAuthenticated: (email: string) => void; onForgot: () => void }

export default function AuthForm({ onAuthenticated, onForgot }: Props) {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  // True once the server said the password was right and the account also needs its two-factor code.
  const [needsCode, setNeedsCode] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      const res = mode === 'login' ? await login(email, password, needsCode ? code : undefined) : await register(email, password)
      if ('mfaRequired' in res) setNeedsCode(true)
      else onAuthenticated(res.email)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  function switchMode() {
    setMode(mode === 'login' ? 'register' : 'login')
    setNeedsCode(false)
    setCode('')
    setError(null)
  }

  return (
    <form onSubmit={submit} className="card auth-card">
      <h2>{mode === 'login' ? 'Log in' : 'Create your account'}</h2>
      <label>
        Email
        <input type="email" required autoComplete="email" value={email} disabled={needsCode}
          onChange={(e) => setEmail(e.target.value)} style={{ display: 'block', width: '100%' }} />
      </label>
      <label>
        Password
        <input type="password" required minLength={8} disabled={needsCode}
          autoComplete={mode === 'login' ? 'current-password' : 'new-password'} value={password}
          onChange={(e) => setPassword(e.target.value)} style={{ display: 'block', width: '100%' }} />
      </label>
      {mode === 'register' && <small className="muted">At least 8 characters. We will email you a link to confirm your address.</small>}
      {needsCode && (
        <label>
          Verification code
          <input inputMode="numeric" autoComplete="one-time-code" required autoFocus value={code} maxLength={16}
            onChange={(e) => setCode(e.target.value)} style={{ display: 'block', width: '100%' }} />
          <small className="muted">The 6-digit code from your authenticator app, or one of your recovery codes.</small>
        </label>
      )}
      {error && <p role="alert">{error}</p>}
      <button type="submit" disabled={busy}>{busy ? 'Please wait...' : mode === 'login' ? (needsCode ? 'Verify and log in' : 'Log in') : 'Sign up'}</button>
      <button type="button" onClick={switchMode}>
        {mode === 'login' ? 'Need an account? Sign up' : 'Have an account? Log in'}
      </button>
      {mode === 'login' && <button type="button" onClick={onForgot}>Forgot your password?</button>}
    </form>
  )
}
