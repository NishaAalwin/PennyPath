import BarList from './BarList'
import type { MonthlySummary } from './transactions'

const inr = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' })

export default function SummaryPanel({ summary }: { summary: MonthlySummary | null }) {
  return (
    <section className="card">
      <h2>Summary</h2>
      {summary === null ? (
        <p className="muted">Loading...</p>
      ) : (
        <>
          <p className="stats">
            <span className="stat income"><strong>Income</strong> <span className="amount">{inr.format(summary.income)}</span></span>
            <span className="stat"><strong>Spent</strong> <span className="amount">{inr.format(summary.expense)}</span></span>
            <span className="stat saved"><strong>Saved</strong> <span className="amount">{inr.format(summary.savings)}</span></span>
          </p>
          {summary.excluded > 0 && (
            <p className="muted">
              {summary.excluded} possible duplicate or transfer {summary.excluded === 1 ? 'entry is' : 'entries are'} left out
              of these totals (see the Transactions tab).
            </p>
          )}
          <div className="viz-pair">
            <BarList title="Where it went" lines={summary.byMerchant} />
            <BarList title="By category" lines={summary.byCategory} />
          </div>
        </>
      )}
    </section>
  )
}
