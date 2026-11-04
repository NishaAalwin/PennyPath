import { useRef, type KeyboardEvent, type ReactNode } from 'react'

export type TabDef = { id: string; label: string; content: ReactNode }

type Props = { tabs: TabDef[]; active: string; onChange: (id: string) => void }

/**
 * Accessible tabs (WAI-ARIA tabs pattern): arrow keys, Home and End move between tabs.
 * Every panel stays mounted and only the active one is shown, so filters, drafts and charts keep their state when you switch.
 */
export default function Tabs({ tabs, active, onChange }: Props) {
  const refs = useRef<Record<string, HTMLButtonElement | null>>({})

  function onKeyDown(e: KeyboardEvent, index: number) {
    const last = tabs.length - 1
    const next = e.key === 'ArrowRight' ? (index === last ? 0 : index + 1)
      : e.key === 'ArrowLeft' ? (index === 0 ? last : index - 1)
      : e.key === 'Home' ? 0
      : e.key === 'End' ? last
      : null
    if (next === null) return
    e.preventDefault()
    onChange(tabs[next].id)
    refs.current[tabs[next].id]?.focus()
  }

  return (
    <>
      <div className="tabs" role="tablist" aria-label="Sections">
        {tabs.map((t, i) => (
          <button key={t.id} id={`tab-${t.id}`} ref={(el) => { refs.current[t.id] = el }} type="button" role="tab" className="tab"
            aria-selected={active === t.id} aria-controls={`panel-${t.id}`} tabIndex={active === t.id ? 0 : -1}
            onClick={() => onChange(t.id)} onKeyDown={(e) => onKeyDown(e, i)}>
            {t.label}
          </button>
        ))}
      </div>
      {tabs.map((t) => (
        <div key={t.id} id={`panel-${t.id}`} role="tabpanel" aria-labelledby={`tab-${t.id}`} hidden={active !== t.id} className="tab-panel">
          {t.content}
        </div>
      ))}
    </>
  )
}
