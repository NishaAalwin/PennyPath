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
    <section className="card">
      <h2>Summary</h2>
      <label>
        Month <input type="month" value={month} onChange={(e) => e.target.value && onMonthChange(e.target.value)} />
      </label>
      {summary && (
        <>
          <p className="stats">
            <span className="stat income"><strong>Income</strong> <span className="amount">{inr.format(summary.income)}</span></span>
            <span className="stat"><strong>Spent</strong> <span className="amount">{inr.format(summary.expense)}</span></span>
            <span className="stat saved"><strong>Saved</strong> <span className="amount">{inr.format(summary.savings)}</span></span>
          </p>
          {summary.excluded > 0 && (
            <p className="muted">
              {summary.excluded} possible duplicate or transfer {summary.excluded === 1 ? 'entry is' : 'entries are'} left out
              of these totals (see the list below).
            </p>
          )}
          <div className="viz-pair">
            <BarList title="Where it went" lines={summary.byMerchant} />
            <BarList title="By category" lines={summary.byCategory} />
          </div>
        </>
      )}
      {trend.length > 0 && <TrendChart months={trend} />}
    </section>
  )
}
