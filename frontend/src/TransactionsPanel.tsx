import { useCallback, useEffect, useState } from 'react'
import SummaryPanel from './SummaryPanel'
import TransactionList from './TransactionList'
import {
  getSummary, getTrend, importSms, listCategories, splitMessages, type ImportResult, type MonthlySummary, type TrendMonth,
} from './transactions'

type Props = { token: string; onSessionExpired: () => void }

export default function Transactions({ token, onSessionExpired }: Props) {
  const [text, setText] = useState('')
  const [version, setVersion] = useState(0)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [categories, setCategories] = useState<string[]>([])
  const [summary, setSummary] = useState<MonthlySummary | null>(null)
  const [trend, setTrend] = useState<TrendMonth[]>([])
  const [month, setMonth] = useState(() => new Date().toISOString().slice(0, 7))

  const handleError = useCallback(
    (e: unknown) => {
      if (e instanceof Error && 'status' in e && e.status === 401) return onSessionExpired()
      setError(e instanceof Error ? e.message : 'Something went wrong.')
    },
    [onSessionExpired],
  )

  const refresh = useCallback(
    () => Promise.all([getSummary(token, month).then(setSummary), getTrend(token, month).then(setTrend)]).catch(handleError),
    [token, month, handleError],
  )

  useEffect(() => {
    refresh()
    listCategories(token).then(setCategories).catch(handleError)
  }, [refresh, token, handleError])

  async function submit() {
    const messages = splitMessages(text)
    if (messages.length === 0) return setError('Paste at least one message.')
    setError(null)
    setBusy(true)
    try {
      setResult(await importSms(token, messages))
      setText('')
      setVersion((v) => v + 1)
      await refresh()
    } catch (e) {
      handleError(e)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section style={{ display: 'grid', gap: '0.75rem' }}>
      <SummaryPanel summary={summary} trend={trend} month={month} onMonthChange={setMonth} />

      <h2>Import SMS</h2>
      <p style={{ margin: 0 }}>Paste your bank SMS messages here, with a blank line between messages. Only the amount, where it was spent, the date and whether it was money in or out are saved. The message text is not stored.</p>
      <textarea rows={6} value={text} onChange={(e) => setText(e.target.value)}
        placeholder="Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26" />
      <button onClick={submit} disabled={busy}>{busy ? 'Importing...' : 'Import'}</button>
      {error && <p role="alert" style={{ color: '#b00020', margin: 0 }}>{error}</p>}
      {result && (
        <p style={{ margin: 0 }}>
          Imported {result.imported}, skipped {result.duplicates} already imported
          {result.possibleDuplicates > 0 && `, ${result.possibleDuplicates} look like duplicates (check below)`}
          {result.unparsed.length > 0 && `, could not read ${result.unparsed.length}`}.
        </p>
      )}
      {result && result.unparsed.length > 0 && (
        <details>
          <summary>Messages that were not recognised (shown now only, not saved)</summary>
          <ul>{result.unparsed.map((m, i) => <li key={i}>{m}</li>)}</ul>
        </details>
      )}

      <TransactionList token={token} month={month} categories={categories} version={version} onChanged={refresh} onError={handleError} />
    </section>
  )
}
