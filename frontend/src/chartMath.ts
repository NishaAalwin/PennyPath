/** Pure helpers for the charts, kept apart from the components so they can be unit tested. */

/** Round a maximum up to a "nice" axis top (1, 2, 2.5, 5 or 10 times a power of ten) and return clean ticks. */
export function niceScale(max: number, tickCount = 4): { top: number; ticks: number[] } {
  if (!(max > 0)) return { top: 1, ticks: [0, 1] }
  const rough = max / tickCount
  const pow = 10 ** Math.floor(Math.log10(rough))
  const step = [1, 2, 2.5, 5, 10].map((m) => m * pow).find((s) => s >= rough) ?? 10 * pow
  const ticks: number[] = []
  for (let v = 0; v < max + step; v += step) {
    ticks.push(Number(v.toFixed(6)))
    if (v >= max) break
  }
  return { top: ticks[ticks.length - 1], ticks }
}

const full = new Intl.NumberFormat('en-IN')
const compact = new Intl.NumberFormat('en-IN', { notation: 'compact', maximumFractionDigits: 1 })

/** One style per axis: full numbers while the axis stays under 10,000, compact once it reaches it. */
export function axisFormatter(top: number): (v: number) => string {
  return top >= 10_000 ? (v) => compact.format(v) : (v) => full.format(v)
}

/** Path for a column with a 4px rounded top and a square base, or '' when there is nothing to draw. */
export function columnPath(x: number, base: number, width: number, height: number, radius = 4): string {
  if (!(height > 0) || !(width > 0)) return ''
  const r = Math.min(radius, width / 2, height)
  const y = base - height
  return `M${x},${base} L${x},${y + r} Q${x},${y} ${x + r},${y} L${x + width - r},${y} Q${x + width},${y} ${x + width},${y + r} L${x + width},${base} Z`
}

const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

/** "2026-11" -> { short: "Nov", long: "Nov 2026" }; unparseable input is returned unchanged. */
export function monthLabel(month: string): { short: string; long: string } {
  const m = /^(\d{4})-(\d{2})$/.exec(month)
  const name = m ? monthNames[Number(m[2]) - 1] : undefined
  return m && name ? { short: name, long: `${name} ${m[1]}` } : { short: month, long: month }
}

/** Largest rows first; everything past `max` folds into one "Other" row so the list stays readable. */
export function topWithOther<T extends { name: string; amount: number }>(lines: T[], max: number): { name: string; amount: number }[] {
  const sorted = [...lines].sort((a, b) => b.amount - a.amount)
  if (sorted.length <= max) return sorted
  const head = sorted.slice(0, max - 1)
  const rest = sorted.slice(max - 1)
  return [...head, { name: `Other (${rest.length})`, amount: rest.reduce((s, l) => s + l.amount, 0) }]
}
