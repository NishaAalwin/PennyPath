import { useEffect, useState } from 'react'
import { getHealth, getMe, type Health } from './api'
import AuthForm from './AuthForm'
import { clearToken, loadToken, saveToken } from './session'

export default function App() {
  const [health, setHealth] = useState<Health | null>(null)
  const [healthError, setHealthError] = useState<string | null>(null)
  const [token, setToken] = useState<string | null>(loadToken)
  const [email, setEmail] = useState<string | null>(null)

  useEffect(() => {
    getHealth().then(setHealth).catch((e: Error) => setHealthError(e.message))
  }, [])

  useEffect(() => {
    if (!token) return
    getMe(token).then((me) => setEmail(me.email)).catch(() => logout())
  }, [token])

  function onAuthenticated(t: string) {
    saveToken(t)
    setToken(t)
  }

  function logout() {
    clearToken()
    setToken(null)
    setEmail(null)
  }

  return (
    <main style={{ fontFamily: 'system-ui', maxWidth: 480, margin: '4rem auto', padding: '0 1rem' }}>
      <h1>PennyPath</h1>
      <p>Your path to financial growth.</p>
      {token ? (
        <section>
          <p>Signed in as <strong>{email ?? '...'}</strong></p>
          <button onClick={logout}>Log out</button>
        </section>
      ) : (
        <AuthForm onAuthenticated={onAuthenticated} />
      )}
      <p style={{ marginTop: '2rem', color: '#666' }}>
        API: {healthError ? `unreachable (${healthError})` : health ? `${health.status} (database: ${health.database})` : 'checking...'}
      </p>
    </main>
  )
}
