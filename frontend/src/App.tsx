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
    <main className="app">
      <header className="hero">
        <div className="brand">
          <span className="logo" aria-hidden="true">
            <svg viewBox="0 0 32 32" fill="none" stroke="#fff" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round">
              <path d="M4 25 L12 16 L18 21 L28 8" />
              <path d="M21 8 H28 V15" />
            </svg>
          </span>
          <div>
            <h1>PennyPath</h1>
            <p className="tagline">Your path to financial growth.</p>
          </div>
        </div>
        {email && (
          <div className="user-chip">
            <span>Signed in as <strong>{email}</strong></span>
            <button onClick={logout}>Log out</button>
          </div>
        )}
      </header>
      {email === undefined ? (
        <p className="api-status">Checking your session...</p>
      ) : email ? (
        <div className="stack">
          <TransactionsPanel onSessionExpired={signedOut} />
          <PrivacyPanel onSessionExpired={signedOut} />
        </div>
      ) : (
        <AuthForm onAuthenticated={setEmail} />
      )}
      <p className="api-status">
        API: {healthError ? `unreachable (${healthError})` : health ? `${health.status} (database: ${health.database})` : 'checking...'}
      </p>
    </main>
  )
}
