import { useCallback, useEffect, useState, type FormEvent } from 'react'
import {
  deleteAccount, fetchExport, getAccount, mfaDisable, mfaEnable, mfaSetup, saveBlob, type AccountInfo, type MfaSetup,
} from './account'
import { groupSecret } from './links'

type Props = { onSessionExpired: () => void; onDeleted: () => void }

const errorText = (e: unknown) => (e instanceof Error ? e.message : 'Something went wrong.')

export default function AccountPanel({ onSessionExpired, onDeleted }: Props) {
  const [info, setInfo] = useState<AccountInfo | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // two-factor setup
  const [setup, setSetup] = useState<MfaSetup | null>(null)
  const [setupCode, setSetupCode] = useState('')
  const [recovery, setRecovery] = useState<string[] | null>(null)

  // turning two-factor off, and deleting the account
  const [offPassword, setOffPassword] = useState('')
  const [offCode, setOffCode] = useState('')
  const [delPassword, setDelPassword] = useState('')
  const [delCode, setDelCode] = useState('')
  const [delSure, setDelSure] = useState(false)
  const [delNeedsCode, setDelNeedsCode] = useState(false)

  const fail = useCallback(
    (e: unknown) => {
      if (e instanceof Error && 'status' in e && e.status === 401) return onSessionExpired()
      setError(errorText(e))
    },
    [onSessionExpired],
  )

  const load = useCallback(() => getAccount().then(setInfo).catch(fail), [fail])

  useEffect(() => {
    load()
  }, [load])

  async function run(action: () => Promise<void>) {
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await action()
    } catch (e) {
      fail(e)
    } finally {
      setBusy(false)
    }
  }

  const startSetup = () => run(async () => {
    setSetup(await mfaSetup())
    setSetupCode('')
  })

  const enable = (e: FormEvent) => {
    e.preventDefault()
    return run(async () => {
      const r = await mfaEnable(setupCode)
      setRecovery(r.recoveryCodes)
      setSetup(null)
      await load()
    })
  }

  const disable = (e: FormEvent) => {
    e.preventDefault()
    return run(async () => {
      await mfaDisable(offPassword, offCode)
      setOffPassword('')
      setOffCode('')
      setNotice('Two-factor login is off.')
      await load()
    })
  }

  const download = () => run(async () => {
    saveBlob(await fetchExport(), 'pennypath-export.json')
    setNotice('Your data was downloaded as pennypath-export.json.')
  })

  const remove = (e: FormEvent) => {
    e.preventDefault()
    return run(async () => {
      try {
        await deleteAccount(delPassword, delNeedsCode || info?.mfaEnabled ? delCode : undefined)
        onDeleted()
      } catch (err) {
        if (err instanceof Error && 'status' in err && err.status === 409) setDelNeedsCode(true)
        throw err
      }
    })
  }

  return (
    <section className="card">
      <h2>Your account</h2>
      {info && <p className="muted">Signed in as <strong>{info.email}</strong>.</p>}
      {notice && <p role="status">{notice}</p>}
      {error && <p role="alert">{error}</p>}

      <h3>Two-factor login</h3>
      {recovery ? (
        <>
          <p role="status">Two-factor login is on. Save these recovery codes now: they are shown only once, and each works one time if you lose your phone.</p>
          <ul className="codes">{recovery.map((c) => <li key={c}><code>{c}</code></li>)}</ul>
          <div><button onClick={() => setRecovery(null)}>I have saved them</button></div>
        </>
      ) : info?.mfaEnabled ? (
        <form onSubmit={disable} className="stack-sm">
          <p className="muted">On. Signing in needs your password and a code from your authenticator app.</p>
          <label>Password <input type="password" autoComplete="current-password" required value={offPassword} onChange={(e) => setOffPassword(e.target.value)} /></label>
          <label>Code <input inputMode="numeric" autoComplete="one-time-code" required value={offCode} maxLength={16} onChange={(e) => setOffCode(e.target.value)} /></label>
          <div><button className="btn-quiet" type="submit" disabled={busy}>Turn off two-factor login</button></div>
        </form>
      ) : setup ? (
        <form onSubmit={enable} className="stack-sm">
          <p>1. In an authenticator app (Google Authenticator, Microsoft Authenticator, Authy, 1Password) add an account with this key:</p>
          <p><code className="secret" aria-label="Setup key">{groupSecret(setup.secret)}</code></p>
          <details>
            <summary>Or use the setup address</summary>
            <p className="muted"><code>{setup.otpAuthUri}</code></p>
          </details>
          <p>2. Type the 6-digit code the app shows:</p>
          <label>Code <input inputMode="numeric" autoComplete="one-time-code" required autoFocus value={setupCode} maxLength={8} onChange={(e) => setSetupCode(e.target.value)} /></label>
          <div className="actions">
            <button type="submit" disabled={busy}>Turn on</button>
            <button type="button" className="btn-quiet" onClick={() => setSetup(null)}>Cancel</button>
          </div>
        </form>
      ) : (
        <>
          <p className="muted">Off. Add a second step to signing in: a 6-digit code from an app on your phone.</p>
          <div><button onClick={startSetup} disabled={busy || !info}>Set up two-factor login</button></div>
        </>
      )}

      <h3>Your data</h3>
      <p className="muted">Download everything PennyPath stores about you (your details, transactions and security activity) as a file. It contains no passwords or access tokens.</p>
      <div><button className="btn-quiet" onClick={download} disabled={busy}>Download my data</button></div>

      <h3>Delete my account</h3>
      <details>
        <summary>Delete my account and all my data</summary>
        <form onSubmit={remove} className="stack-sm">
          <p>
            This removes your account, every transaction, your saved Gmail access (and revokes it at Google) and your security
            history. It cannot be undone and nobody, including us, can recover it.
          </p>
          <label>Password <input type="password" autoComplete="current-password" required value={delPassword} onChange={(e) => setDelPassword(e.target.value)} /></label>
          {(info?.mfaEnabled || delNeedsCode) && (
            <label>Code from your authenticator app <input inputMode="numeric" autoComplete="one-time-code" required value={delCode} maxLength={16} onChange={(e) => setDelCode(e.target.value)} /></label>
          )}
          <label><input type="checkbox" checked={delSure} onChange={(e) => setDelSure(e.target.checked)} /> I understand this cannot be undone</label>
          <div><button type="submit" className="danger" disabled={busy || !delSure}>Delete everything</button></div>
        </form>
      </details>
    </section>
  )
}
