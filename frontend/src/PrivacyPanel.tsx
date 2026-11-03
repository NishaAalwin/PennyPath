import { useCallback, useEffect, useState } from 'react'
import { getAiConsent, getSecurityEvents, logoutAll, setAiConsent, type SecurityEvent } from './api'

const LABELS: Record<string, string> = {
  Registered: 'Account created',
  LoginSucceeded: 'Signed in',
  LoginFailed: 'Failed sign-in attempt',
  LoginBlockedByLockout: 'Sign-in blocked (account temporarily locked)',
  LoggedOut: 'Signed out',
  LoggedOutEverywhere: 'Signed out of all devices',
  AiConsentGranted: 'AI features allowed',
  AiConsentRevoked: 'AI features turned off',
}

type Props = { onSessionExpired: () => void }

export default function PrivacyPanel({ onSessionExpired }: Props) {
  const [ai, setAi] = useState<boolean | null>(null)
  const [events, setEvents] = useState<SecurityEvent[]>([])
  const [error, setError] = useState<string | null>(null)

  const fail = useCallback(
    (e: unknown) => {
      if (e instanceof Error && 'status' in e && e.status === 401) return onSessionExpired()
      setError(e instanceof Error ? e.message : 'Something went wrong.')
    },
    [onSessionExpired],
  )

  const load = useCallback(
    () => Promise.all([getAiConsent().then((c) => setAi(c.enabled)), getSecurityEvents().then(setEvents)]).catch(fail),
    [fail],
  )

  useEffect(() => {
    load()
  }, [load])

  async function toggle(enabled: boolean) {
    setError(null)
    try {
      await setAiConsent(enabled)
      await load()
    } catch (e) {
      fail(e)
    }
  }

  async function signOutEverywhere() {
    setError(null)
    try {
      await logoutAll()
      onSessionExpired()
    } catch (e) {
      fail(e)
    }
  }

  return (
    <section style={{ display: 'grid', gap: '0.75rem', marginTop: '1.5rem' }}>
      <h2>Privacy and security</h2>
      {error && <p role="alert" style={{ color: '#b00020', margin: 0 }}>{error}</p>}

      <label>
        <input type="checkbox" checked={ai === true} disabled={ai === null} onChange={(e) => toggle(e.target.checked)} />{' '}
        Allow AI features to use my data
      </label>
      <p style={{ margin: 0 }}>
        Off by default. No AI feature is active yet. If you turn this on, only merchant names and monthly totals could ever be
        sent, never account numbers, balances or message text, and you can turn it off at any time.
      </p>

      <button onClick={signOutEverywhere}>Sign out of all devices</button>

      <details>
        <summary>Recent security activity ({events.length})</summary>
        {events.length === 0 ? (
          <p>No activity yet.</p>
        ) : (
          <ul>
            {events.map((e) => (
              <li key={e.id}>
                {LABELS[e.type] ?? e.type} <small>{new Date(e.at).toLocaleString()}{e.ip ? `, from ${e.ip}` : ''}</small>
              </li>
            ))}
          </ul>
        )}
      </details>
    </section>
  )
}
