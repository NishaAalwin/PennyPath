import BarList from './BarList'
import TrendChart from './TrendChart'
import type { MonthlySummary, TrendMonth } from './transactions'

const inr = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' })

type Props = {
  summary: MonthlySummary | null
  trend: TrendMonth[]
  month: string
  onMonthChange: (month: string) => void
}

export default function SummaryPanel({ summary, trend, month, onMonthChange }: Props) {
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
          <BarList title="Where it went" lines={summary.byMerchant} />
          <BarList title="By category" lines={summary.byCategory} />
        </>
      )}
      {trend.length > 0 && <TrendChart months={trend} />}
    </section>
  )
}
