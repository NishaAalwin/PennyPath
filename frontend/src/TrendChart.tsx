import { useState } from 'react'
import './charts.css'
import { axisFormatter, columnPath, monthLabel, niceScale } from './chartMath'
import type { TrendMonth } from './transactions'

const inr = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' })

const W = 480
const H = 220
const M = { left: 44, right: 8, top: 10, bottom: 30 }
const MAX_BAR = 24
const GAP = 2

export default function TrendChart({ months }: { months: TrendMonth[] }) {
  const [active, setActive] = useState<number | null>(null)

  const plotW = W - M.left - M.right
  const plotH = H - M.top - M.bottom
  const base = M.top + plotH
  const { top, ticks } = niceScale(Math.max(0, ...months.flatMap((m) => [m.income, m.expense])))
  const fmt = axisFormatter(top)
  const y = (v: number) => base - (Math.max(v, 0) / top) * plotH
  const slot = months.length > 0 ? plotW / months.length : plotW
  const barW = Math.min(MAX_BAR, (slot * 0.8 - GAP) / 2)
  const hovered = active === null ? null : months[active]

  const label = (m: TrendMonth) =>
    `${monthLabel(m.month).long}: income ${inr.format(m.income)}, spent ${inr.format(m.expense)}, saved ${inr.format(m.savings)}`

  return (
    <div className="viz">
      <p className="viz-sub">Last {months.length} months, in rupees.</p>
      <ul className="viz-legend" aria-label="Legend">
        <li><span className="viz-swatch s1" aria-hidden="true" />Income</li>
        <li><span className="viz-swatch s2" aria-hidden="true" />Spent</li>
      </ul>

      <svg viewBox={`0 0 ${W} ${H}`} role="group" aria-label="Income and spending by month, one pair of columns per month">
        {ticks.map((t) => (
          <g key={t}>
            <line className="viz-grid" x1={M.left} x2={W - M.right} y1={y(t)} y2={y(t)} />
            <text className="viz-tick" x={M.left - 6} y={y(t) + 4} textAnchor="end">{fmt(t)}</text>
          </g>
        ))}

        {months.map((m, i) => {
          const left = M.left + i * slot
          const center = left + slot / 2
          const x1 = center - GAP / 2 - barW
          const x2 = center + GAP / 2
          const { short } = monthLabel(m.month)
          const showYear = i === 0 || m.month.endsWith('-01')
          return (
            <g
              key={m.month}
              className={`viz-slot${active === i ? ' is-active' : ''}`}
              tabIndex={0}
              role="img"
              aria-label={label(m)}
              onPointerEnter={() => setActive(i)}
              onPointerLeave={() => setActive(null)}
              onFocus={() => setActive(i)}
              onBlur={() => setActive(null)}
            >
              <rect className="viz-slot-band" x={left} y={M.top} width={slot} height={plotH} />
              <path className="viz-col-1" d={columnPath(x1, base, barW, base - y(m.income))} />
              <path className="viz-col-2" d={columnPath(x2, base, barW, base - y(m.expense))} />
              <text className="viz-xlabel" x={center} y={base + 14} textAnchor="middle">{short}</text>
              {showYear && <text className="viz-xlabel" x={center} y={base + 26} textAnchor="middle">{m.month.slice(0, 4)}</text>}
              {/* hit area is the whole slot, taller than any mark */}
              <rect x={left} y={M.top} width={slot} height={plotH + M.bottom} fill="transparent" />
            </g>
          )
        })}
        <line className="viz-grid" x1={M.left} x2={W - M.right} y1={base} y2={base} />
      </svg>

      {hovered && active !== null && (
        <div className="viz-tooltip" role="status" style={{ left: `${((M.left + (active + 0.5) * slot) / W) * 100}%` }}>
          <div className="head">{monthLabel(hovered.month).long}</div>
          <div className="row"><span className="name"><span className="viz-key s1" />Income</span><strong>{inr.format(hovered.income)}</strong></div>
          <div className="row"><span className="name"><span className="viz-key s2" />Spent</span><strong>{inr.format(hovered.expense)}</strong></div>
          <div className="row"><span className="name">Saved</span><strong>{inr.format(hovered.savings)}</strong></div>
        </div>
      )}

      <details>
        <summary>Show as table</summary>
        <table>
          <thead>
            <tr><th align="left">Month</th><th className="num">Income</th><th className="num">Spent</th><th className="num">Saved</th></tr>
          </thead>
          <tbody>
            {months.map((m) => (
              <tr key={m.month}>
                <td>{monthLabel(m.month).long}</td>
                <td className="num">{inr.format(m.income)}</td>
                <td className="num">{inr.format(m.expense)}</td>
                <td className="num">{inr.format(m.savings)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </div>
  )
}
