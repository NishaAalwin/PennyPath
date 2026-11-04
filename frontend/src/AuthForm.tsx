import { useState, type FormEvent } from 'react'
import { login, register } from './api'

type Props = { onAuthenticated: (email: string) => void }

export default function AuthForm({ onAuthenticated }: Props) {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      const res = await (mode === 'login' ? login : register)(email, password)
      onAuthenticated(res.email)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} className="card auth-card">
      <h2>{mode === 'login' ? 'Log in' : 'Create your account'}</h2>
      <label>
        Email
        <input type="email" required autoComplete="email" value={email}
          onChange={(e) => setEmail(e.target.value)} style={{ display: 'block', width: '100%' }} />
      </label>
      <label>
        Password
        <input type="password" required minLength={8}
          autoComplete={mode === 'login' ? 'current-password' : 'new-password'} value={password}
          onChange={(e) => setPassword(e.target.value)} style={{ display: 'block', width: '100%' }} />
      </label>
      {mode === 'register' && <small className="muted">At least 8 characters.</small>}
      {error && <p role="alert">{error}</p>}
      <button type="submit" disabled={busy}>{busy ? 'Please wait...' : mode === 'login' ? 'Log in' : 'Sign up'}</button>
      <button type="button" onClick={() => { setMode(mode === 'login' ? 'register' : 'login'); setError(null) }}>
        {mode === 'login' ? 'Need an account? Sign up' : 'Have an account? Log in'}
      </button>
    </form>
  )
}
