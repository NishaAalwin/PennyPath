import { useCallback, useEffect, useState } from 'react'
import { getHealth, getMe, logout as logoutRequest, type Health } from './api'
import { ForgotPasswordForm, ResetPasswordForm, VerifyEmailLanding, VerifyEmailScreen } from './AccountScreens'
import AccountPanel from './AccountPanel'
import AuthForm from './AuthForm'
import { parseHashAction, type HashAction } from './links'
import PrivacyPanel from './PrivacyPanel'
import TransactionsPanel from './TransactionsPanel'

type Me = { email: string; emailVerified: boolean }

export default function App() {
  const [health, setHealth] = useState<Health | null>(null)
  const [healthError, setHealthError] = useState<string | null>(null)
  // undefined while we ask the server whether the session cookie is still valid; null when signed out.
  const [me, setMe] = useState<Me | null | undefined>(undefined)
  const [screen, setScreen] = useState<'login' | 'forgot'>('login')
  const [farewell, setFarewell] = useState<string | null>(null)
  // The link from an email (verify or reset). The token is read once and removed from the address bar straight away.
  const [action, setAction] = useState<HashAction | null>(() => {
    const a = parseHashAction(window.location.hash)
    if (window.location.hash) window.history.replaceState(null, '', window.location.pathname + window.location.search)
    return a
  })

  useEffect(() => {
    getHealth().then(setHealth).catch((e: Error) => setHealthError(e.message))
  }, [])

  const loadMe = useCallback(
    () => getMe().then((m) => setMe({ email: m.email, emailVerified: m.emailVerified })).catch(() => setMe(null)),
    [],
  )

  useEffect(() => {
    try {
      localStorage.removeItem('pennypath.token') // older versions kept the login token here; it is no longer used
    } catch {
      /* storage unavailable */
    }
    loadMe()
  }, [loadMe])

  const signedOut = useCallback(() => setMe(null), [])

  async function logout() {
    try {
      await logoutRequest()
    } finally {
      signedOut()
    }
  }

  function leaveAction() {
    setAction(null)
    setScreen('login')
    setMe(undefined) // a verified link may have changed what this session may do: ask again instead of showing stale screens
    loadMe()
  }

  const signedIn = me !== undefined && me !== null

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
        {signedIn && (
          <div className="user-chip">
            <span>Signed in as <strong>{me.email}</strong></span>
            <button onClick={logout}>Log out</button>
          </div>
        )}
      </header>
      {action?.kind === 'reset' ? (
        <ResetPasswordForm token={action.token} onDone={leaveAction} />
      ) : action?.kind === 'verify' ? (
        <VerifyEmailLanding token={action.token} onDone={leaveAction} />
      ) : me === undefined ? (
        <p className="api-status">Checking your session...</p>
      ) : me && !me.emailVerified ? (
        <VerifyEmailScreen email={me.email} onRecheck={loadMe} />
      ) : me ? (
        <div className="stack">
          <TransactionsPanel
            onSessionExpired={signedOut}
            privacy={
              <div className="stack">
                <PrivacyPanel onSessionExpired={signedOut} />
                <AccountPanel
                  onSessionExpired={signedOut}
                  onDeleted={() => {
                    setFarewell('Your account and all its data were deleted.')
                    signedOut()
                  }}
                />
              </div>
            }
          />
        </div>
      ) : screen === 'forgot' ? (
        <ForgotPasswordForm onBack={() => setScreen('login')} />
      ) : (
        <>
          {farewell && <p role="status" className="farewell">{farewell}</p>}
          <AuthForm onAuthenticated={() => { setFarewell(null); loadMe() }} onForgot={() => setScreen('forgot')} />
        </>
      )}
      <p className="api-status">
        API: {healthError ? `unreachable (${healthError})` : health ? `${health.status} (database: ${health.database})` : 'checking...'}
      </p>
    </main>
  )
}
