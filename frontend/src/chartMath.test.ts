import { describe, expect, it } from 'vitest'
import { axisFormatter, columnPath, monthLabel, niceScale, topWithOther } from './chartMath'

describe('niceScale', () => {
  it('rounds the top up to a clean number and covers the maximum', () => {
    const s = niceScale(15250)
    expect(s.top).toBeGreaterThanOrEqual(15250)
    expect(s.ticks[0]).toBe(0)
    expect(s.ticks.at(-1)).toBe(s.top)
    // evenly spaced
    const steps = s.ticks.slice(1).map((t, i) => t - s.ticks[i])
    expect(new Set(steps.map((v) => v.toFixed(6))).size).toBe(1)
  })

  it('uses round steps', () => {
    expect(niceScale(900).ticks).toEqual([0, 250, 500, 750, 1000])
    expect(niceScale(20000).ticks).toEqual([0, 5000, 10000, 15000, 20000])
  })

  it('has a sane axis when there is no data', () => {
    expect(niceScale(0)).toEqual({ top: 1, ticks: [0, 1] })
    expect(niceScale(-5).top).toBe(1)
  })
})

describe('axisFormatter', () => {
  it('prints full numbers below 10,000 and compact ones from there', () => {
    expect(axisFormatter(5000)(2500)).toBe('2,500')
    expect(axisFormatter(20000)(10000)).toBe('10K')
  })
})

describe('columnPath', () => {
  it('draws nothing for a zero or negative height', () => {
    expect(columnPath(0, 100, 20, 0)).toBe('')
    expect(columnPath(0, 100, 20, -5)).toBe('')
  })

  it('keeps the base square and rounds only the top', () => {
    const d = columnPath(10, 100, 20, 50)
    expect(d.startsWith('M10,100')).toBe(true) // bottom-left corner is a plain point
    expect(d.endsWith('L30,100 Z')).toBe(true) // bottom-right corner is a plain point
    expect(d.match(/Q/g)).toHaveLength(2) // two rounded corners, both at the top
  })

  it('never rounds more than the column allows', () => {
    expect(columnPath(0, 100, 20, 2)).toContain('Q0,98 2,98') // radius shrinks to the height
  })
})

describe('monthLabel', () => {
  it('formats a month', () => expect(monthLabel('2026-11')).toEqual({ short: 'Nov', long: 'Nov 2026' }))
  it('returns odd input unchanged', () => expect(monthLabel('junk')).toEqual({ short: 'junk', long: 'junk' }))
})

describe('topWithOther', () => {
  const lines = [3, 9, 1, 7, 5].map((amount, i) => ({ name: `m${i}`, amount }))

  it('sorts largest first and keeps everything when it fits', () => {
    expect(topWithOther(lines, 5).map((l) => l.amount)).toEqual([9, 7, 5, 3, 1])
  })

  it('folds the tail into one Other row that keeps the total', () => {
    const r = topWithOther(lines, 3)
    expect(r).toHaveLength(3)
    expect(r[2]).toEqual({ name: 'Other (3)', amount: 3 + 1 + 5 })
    expect(r.reduce((s, l) => s + l.amount, 0)).toBe(25)
  })
})
