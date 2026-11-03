import type { MonthlySummary, SummaryLine } from './transactions'

const inr = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' })

type Props = { summary: MonthlySummary | null; month: string; onMonthChange: (month: string) => void }

function Breakdown({ title, lines }: { title: string; lines: SummaryLine[] }) {
  return (
    <div>
      <h3 style={{ margin: '0 0 0.25rem' }}>{title}</h3>
      {lines.length === 0 ? (
        <p style={{ margin: 0 }}>Nothing yet.</p>
      ) : (
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <tbody>
            {lines.map((l) => (
              <tr key={l.name}>
                <td>{l.name}</td>
                <td align="right">{inr.format(l.amount)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}

export default function SummaryPanel({ summary, month, onMonthChange }: Props) {
  return (
    <section style={{ display: 'grid', gap: '0.75rem' }}>
      <h2>Summary</h2>
      <label>
        Month <input type="month" value={month} onChange={(e) => e.target.value && onMonthChange(e.target.value)} />
      </label>
      {summary && (
        <>
          <p style={{ margin: 0 }}>
            <strong>Income</strong> {inr.format(summary.income)} · <strong>Spent</strong> {inr.format(summary.expense)} ·{' '}
            <strong>Saved</strong> {inr.format(summary.savings)}
          </p>
          {summary.excluded > 0 && (
            <p style={{ margin: 0 }}>
              {summary.excluded} possible duplicate or transfer {summary.excluded === 1 ? 'entry is' : 'entries are'} left out
              of these totals (see the list below).
            </p>
          )}
          <Breakdown title="Where it went" lines={summary.byMerchant} />
          <Breakdown title="By category" lines={summary.byCategory} />
        </>
      )}
    </section>
  )
}
