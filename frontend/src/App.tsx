import { useEffect, useState } from 'react'
import { getHealth, getMe, logout as logoutRequest, type Health } from './api'
import AuthForm from './AuthForm'
import PrivacyPanel from './PrivacyPanel'
import TransactionsPanel from './TransactionsPanel'

export default function App() {
  const [health, setHealth] = useState<Health | null>(null)
  const [healthError, setHealthError] = useState<string | null>(null)
  // undefined while we ask the server whether the session cookie is still valid; null when signed out.
  const [email, setEmail] = useState<string | null | undefined>(undefined)

  useEffect(() => {
    getHealth().then(setHealth).catch((e: Error) => setHealthError(e.message))
  }, [])

  useEffect(() => {
    try {
      localStorage.removeItem('pennypath.token') // older versions kept the login token here; it is no longer used
    } catch {
      /* storage unavailable */
    }
    getMe().then((me) => setEmail(me.email)).catch(() => setEmail(null))
  }, [])

  function signedOut() {
    setEmail(null)
  }

  async function logout() {
    try {
      await logoutRequest()
    } finally {
      signedOut()
    }
  }

  return (
    <main style={{ fontFamily: 'system-ui', maxWidth: 480, margin: '4rem auto', padding: '0 1rem' }}>
      <h1>PennyPath</h1>
      <p>Your path to financial growth.</p>
      {email === undefined ? (
        <p>Checking your session...</p>
      ) : email ? (
        <section>
          <p>Signed in as <strong>{email}</strong></p>
          <button onClick={logout}>Log out</button>
          <TransactionsPanel onSessionExpired={signedOut} />
          <PrivacyPanel onSessionExpired={signedOut} />
        </section>
      ) : (
        <AuthForm onAuthenticated={setEmail} />
      )}
      <p style={{ marginTop: '2rem', color: '#666' }}>
        API: {healthError ? `unreachable (${healthError})` : health ? `${health.status} (database: ${health.database})` : 'checking...'}
      </p>
    </main>
  )
}
