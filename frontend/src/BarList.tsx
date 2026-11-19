import './charts.css'
import { topWithOther } from './chartMath'
import type { SummaryLine } from './transactions'

const inr = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' })
const MAX_ROWS = 8

/** Ranked horizontal bars, one hue, largest first, with the value at the tip and a table underneath. */
export default function BarList({ title, lines }: { title: string; lines: SummaryLine[] }) {
  const rows = topWithOther(lines, MAX_ROWS)
  const max = Math.max(0, ...rows.map((r) => r.amount))

  return (
    <div className="viz">
      <h3>{title}</h3>
      {rows.length === 0 ? (
        <p className="viz-sub">Nothing yet.</p>
      ) : (
        <ul className="viz-bars">
          {rows.map((r) => (
            <li key={r.name} className="viz-bar-row" title={`${r.name}: ${inr.format(r.amount)}`}>
              <span className="viz-bar-name">{r.name}</span>
              <span className="viz-bar-track">
                <span className="viz-bar-fill" style={{ display: 'block', width: `${max > 0 ? Math.max(r.amount, 0) / max * 100 : 0}%` }} />
              </span>
              <span className="viz-bar-value">{inr.format(r.amount)}</span>
            </li>
          ))}
        </ul>
      )}
      {lines.length > 0 && (
        <details>
          <summary>Show as table</summary>
          <table>
            <tbody>
              {lines.map((l) => (
                <tr key={l.name}><td>{l.name}</td><td className="num">{inr.format(l.amount)}</td></tr>
              ))}
            </tbody>
          </table>
        </details>
      )}
    </div>
  )
}
