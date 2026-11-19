import { useCallback, useEffect, useState } from 'react'
import {
  describeSync, disconnectGmail, getGmailStatus, readGmailReturn, startGmailConnect, syncGmail,
  type GmailStatus, type GmailSyncResult,
} from './gmail'

type Props = {
  /** Called after a sync that may have added rows, so the list and totals reload. */
  onImported: () => void
  onSessionExpired: () => void
}

export default function GmailPanel({ onImported, onSessionExpired }: Props) {
  const [status, setStatus] = useState<GmailStatus | null>(null)
  const [result, setResult] = useState<GmailSyncResult | null>(null)
  const [notice, setNotice] = useState<string | null>(() => {
    const back = readGmailReturn(window.location.search)
    if (back === 'connected') return 'Gmail connected. Choose "Sync Gmail" to import your bank emails.'
    if (back === 'failed') return 'Gmail was not connected (it was cancelled or could not be completed). You can try again.'
    return null
  })
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const fail = useCallback(
    (e: unknown) => {
      if (e instanceof Error && 'status' in e && e.status === 401) return onSessionExpired()
      setError(e instanceof Error ? e.message : 'Something went wrong.')
    },
    [onSessionExpired],
  )

  useEffect(() => {
    // The ?gmail= marker has been read; take it out of the address so a reload does not repeat the message.
    if (readGmailReturn(window.location.search)) window.history.replaceState(null, '', window.location.pathname)
    getGmailStatus().then(setStatus).catch(fail)
  }, [fail])

  async function run(action: () => Promise<void>) {
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await action()
    } catch (e) {
      fail(e)
      getGmailStatus().then(setStatus).catch(() => undefined) // e.g. "connect again" removed the saved link
    } finally {
      setBusy(false)
    }
  }

  const connect = () => run(async () => {
    const { url } = await startGmailConnect()
    window.location.assign(url)
  })

  const sync = () => run(async () => {
    const r = await syncGmail()
    setResult(r)
    setStatus(await getGmailStatus())
    onImported()
  })

  const disconnect = () => run(async () => {
    await disconnectGmail()
    setResult(null)
    setStatus(await getGmailStatus())
    setNotice('Gmail disconnected. The saved access was deleted. Transactions you already imported stay.')
  })

  return (
    <section className="card">
      <h2>Import from Gmail</h2>
      {status === null ? (
        <p className="muted">Checking...</p>
      ) : !status.configured ? (
        <p className="muted">
          Gmail import is not set up on this server yet. It needs a Google OAuth client (see docs/operations.md, "Connect Gmail").
        </p>
      ) : status.connected ? (
        <>
          <p className="muted">
            Connected{status.connectedAt && ` on ${new Date(status.connectedAt).toLocaleDateString()}`}
            {status.lastSyncAt ? `, last synced ${new Date(status.lastSyncAt).toLocaleString()}` : ', not synced yet'}.
            Only bank alert emails are searched, read-only. The email text is not saved, only the amount, where, date and in or out.
          </p>
          <div className="actions">
            <button onClick={sync} disabled={busy}>{busy ? 'Working...' : 'Sync Gmail'}</button>
            <button className="btn-quiet" onClick={disconnect} disabled={busy}>Disconnect Gmail</button>
          </div>
        </>
      ) : (
        <>
          <p className="muted">
            Connect your Gmail to import bank and UPI alert emails from the last 90 days. PennyPath asks for read-only access, only
            looks for emails from known banks and payment apps, and never saves the email text. You can disconnect at any time.
          </p>
          <div><button onClick={connect} disabled={busy}>{busy ? 'Opening Google...' : 'Connect Gmail'}</button></div>
        </>
      )}
      {notice && <p role="status">{notice}</p>}
      {error && <p role="alert">{error}</p>}
      {result && (
        <>
          <p>{describeSync(result)}</p>
          {result.notRecognisedSubjects.length > 0 && (
            <details>
              <summary>Emails that were not recognised (shown now only, not saved)</summary>
              <ul>{result.notRecognisedSubjects.map((s, i) => <li key={i}>{s}</li>)}</ul>
            </details>
          )}
        </>
      )}
    </section>
  )
}
