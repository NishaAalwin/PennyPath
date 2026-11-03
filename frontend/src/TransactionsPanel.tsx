import { useCallback, useEffect, useState } from 'react'
import { importSms, listCategories, listTransactions, setCategory, splitMessages, type ImportResult, type Transaction } from './transactions'

type Props = { token: string; onSessionExpired: () => void }

const inr = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' })

export default function Transactions({ token, onSessionExpired }: Props) {
  const [text, setText] = useState('')
  const [rows, setRows] = useState<Transaction[]>([])
  const [result, setResult] = useState<ImportResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [categories, setCategories] = useState<string[]>([])

  const handleError = useCallback(
    (e: unknown) => {
      if (e instanceof Error && 'status' in e && e.status === 401) return onSessionExpired()
      setError(e instanceof Error ? e.message : 'Something went wrong.')
    },
    [onSessionExpired],
  )

  const refresh = useCallback(
    () => listTransactions(token).then(setRows).catch(handleError),
    [token, handleError],
  )

  useEffect(() => {
    refresh()
    listCategories(token).then(setCategories).catch(handleError)
  }, [refresh, token, handleError])

  async function changeCategory(id: string, category: string) {
    setError(null)
    try {
      await setCategory(token, id, category)
      await refresh()
    } catch (e) {
      handleError(e)
    }
  }

  async function submit() {
    const messages = splitMessages(text)
    if (messages.length === 0) return setError('Paste at least one message.')
    setError(null)
    setBusy(true)
    try {
      setResult(await importSms(token, messages))
      setText('')
      await refresh()
    } catch (e) {
      handleError(e)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section style={{ display: 'grid', gap: '0.75rem' }}>
      <h2>Import SMS</h2>
      <p style={{ margin: 0 }}>Paste your bank SMS messages here, with a blank line between messages.</p>
      <textarea rows={6} value={text} onChange={(e) => setText(e.target.value)}
        placeholder="Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26" />
      <button onClick={submit} disabled={busy}>{busy ? 'Importing...' : 'Import'}</button>
      {error && <p role="alert" style={{ color: '#b00020', margin: 0 }}>{error}</p>}
      {result && (
        <p style={{ margin: 0 }}>
          Imported {result.imported}, skipped {result.duplicates} already imported
          {result.unparsed.length > 0 && `, could not read ${result.unparsed.length}`}.
        </p>
      )}
      {result && result.unparsed.length > 0 && (
        <details>
          <summary>Messages that were not recognised</summary>
          <ul>{result.unparsed.map((m, i) => <li key={i}>{m}</li>)}</ul>
        </details>
      )}

      <h2>Transactions</h2>
      {rows.length === 0 ? (
        <p>No transactions yet.</p>
      ) : (
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr><th align="left">Date</th><th align="left">Merchant</th><th align="left">Category</th><th align="right">Amount</th></tr>
          </thead>
          <tbody>
            {rows.map((t) => (
              <tr key={t.id}>
                <td>{t.date}</td>
                <td>{t.merchant || '-'}</td>
                <td>
                  <select aria-label={`Category for ${t.merchant || 'transaction'}`} value={t.category}
                    onChange={(e) => changeCategory(t.id, e.target.value)}>
                    {(categories.length ? categories : [t.category]).map((c) => <option key={c} value={c}>{c}</option>)}
                  </select>
                </td>
                <td align="right" style={{ color: t.type === 'Income' ? '#1b7f3b' : undefined }}>
                  {t.type === 'Income' ? '+' : '-'}{inr.format(t.amount)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}
