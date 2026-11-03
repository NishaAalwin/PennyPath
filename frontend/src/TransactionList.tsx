import { useCallback, useEffect, useState } from 'react'
import {
  deleteTransaction, listTransactions, notDuplicate, setCategory, setTransfer, updateTransaction,
  type PagedTransactions, type Transaction, type TransactionChanges,
} from './transactions'

const inr = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' })
const PAGE_SIZE = 25

type Props = {
  month: string
  categories: string[]
  /** Changes when something outside the list (an import) added rows. */
  version: number
  /** The list changed a row, so totals elsewhere are out of date. */
  onChanged: () => void
  onError: (e: unknown) => void
}

type EditRowProps = {
  row: Transaction
  onSave: (changes: TransactionChanges) => Promise<void>
  onCancel: () => void
  error: string | null
}

function EditRow({ row, onSave, onCancel, error }: EditRowProps) {
  const [merchant, setMerchant] = useState(row.merchant)
  const [amount, setAmount] = useState(String(row.amount))
  const [date, setDate] = useState(row.date)
  const [type, setType] = useState(row.type)

  function save() {
    const changes: TransactionChanges = {}
    if (merchant.trim() !== row.merchant) changes.merchant = merchant.trim()
    if (Number(amount) !== row.amount) changes.amount = Number(amount)
    if (date !== row.date) changes.date = date
    if (type !== row.type) changes.type = type
    return onSave(changes)
  }

  return (
    <tr>
      <td colSpan={6}>
        <div style={{ display: 'grid', gap: '0.5rem', padding: '0.5rem 0' }}>
          <label>Where / what <input value={merchant} maxLength={200} onChange={(e) => setMerchant(e.target.value)} /></label>
          <label>Amount (INR) <input type="number" min="0.01" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} /></label>
          <label>Date <input type="date" value={date} min="2000-01-01" onChange={(e) => setDate(e.target.value)} /></label>
          <label>
            Money{' '}
            <select value={type} onChange={(e) => setType(e.target.value as Transaction['type'])}>
              <option value="Expense">Out (spent)</option>
              <option value="Income">In (received)</option>
            </select>
          </label>
          {error && <p role="alert" style={{ color: '#b00020', margin: 0 }}>{error}</p>}
          <div>
            <button onClick={save}>Save</button> <button onClick={onCancel}>Cancel</button>
          </div>
        </div>
      </td>
    </tr>
  )
}

export default function TransactionList({ month, categories, version, onChanged, onError }: Props) {
  const [search, setSearch] = useState('')
  const [debounced, setDebounced] = useState('')
  const [category, setCategoryFilter] = useState('')
  const [type, setType] = useState<'' | 'Income' | 'Expense'>('')
  const [allMonths, setAllMonths] = useState(false)
  const [page, setPage] = useState(1)
  const [data, setData] = useState<PagedTransactions | null>(null)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editError, setEditError] = useState<string | null>(null)

  useEffect(() => {
    const t = setTimeout(() => {
      setDebounced(search.trim())
      setPage(1)
    }, 300)
    return () => clearTimeout(t)
  }, [search])

  const load = useCallback(
    () =>
      listTransactions({
        month: allMonths ? undefined : month,
        search: debounced || undefined,
        category: category || undefined,
        type: type || undefined,
        page,
        pageSize: PAGE_SIZE,
      })
        .then(setData)
        .catch(onError),
    [month, allMonths, debounced, category, type, page, onError],
  )

  useEffect(() => {
    load()
  }, [load, version])

  async function act(action: () => Promise<unknown>) {
    try {
      await action()
      await load()
      onChanged()
    } catch (e) {
      onError(e)
    }
  }

  async function saveEdit(id: string, changes: TransactionChanges) {
    if (Object.keys(changes).length === 0) return setEditingId(null)
    setEditError(null)
    try {
      await updateTransaction(id, changes)
      setEditingId(null)
      await load()
      onChanged()
    } catch (e) {
      if (e instanceof Error && 'status' in e && e.status === 400) return setEditError(e.message)
      onError(e)
    }
  }

  const filtered = Boolean(debounced || category || type)
  const total = data?.total ?? 0
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))
  // The label describes the rows on screen (the page that was loaded), not the page just requested, so the two never disagree.
  const shownPage = data?.page ?? page
  const from = total === 0 ? 0 : (shownPage - 1) * PAGE_SIZE + 1
  const to = Math.min(shownPage * PAGE_SIZE, total)
  const resetPage = <T,>(set: (v: T) => void) => (v: T) => {
    set(v)
    setPage(1)
  }

  return (
    <section style={{ display: 'grid', gap: '0.75rem' }}>
      <h2>Transactions</h2>
      <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', alignItems: 'center' }}>
        <input type="search" aria-label="Search by where it was spent or amount" placeholder="Search place or amount" maxLength={100}
          value={search} onChange={(e) => setSearch(e.target.value)} />
        <select aria-label="Filter by category" value={category} onChange={(e) => resetPage(setCategoryFilter)(e.target.value)}>
          <option value="">All categories</option>
          {categories.map((c) => <option key={c} value={c}>{c}</option>)}
        </select>
        <select aria-label="Filter by money in or out" value={type}
          onChange={(e) => resetPage(setType)(e.target.value as '' | 'Income' | 'Expense')}>
          <option value="">In and out</option>
          <option value="Expense">Out (spent)</option>
          <option value="Income">In (received)</option>
        </select>
        <label>
          <input type="checkbox" checked={allMonths} onChange={(e) => resetPage(setAllMonths)(e.target.checked)} /> All months
        </label>
      </div>
      <p style={{ margin: 0 }}>{allMonths ? 'Showing every month.' : `Showing ${month}, the month chosen above.`}</p>

      {data === null ? (
        <p>Loading...</p>
      ) : total === 0 ? (
        <p>{filtered ? 'No transactions match these filters.' : allMonths ? 'No transactions yet.' : 'No transactions in this month.'}</p>
      ) : (
        <>
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th align="left">Date</th><th align="left">Where / what</th><th align="left">Category</th>
                <th align="left">Status</th><th align="right">Amount</th><th />
              </tr>
            </thead>
            <tbody>
              {data.items.map((t) =>
                editingId === t.id ? (
                  <EditRow key={t.id} row={t} error={editError} onSave={(c) => saveEdit(t.id, c)} onCancel={() => setEditingId(null)} />
                ) : (
                  <tr key={t.id} style={t.duplicateOfId ? { opacity: 0.6 } : undefined}>
                    <td>{t.date}</td>
                    <td>{t.merchant || '-'}</td>
                    <td>
                      <select aria-label={`Category for ${t.merchant || 'transaction'}`} value={t.category}
                        onChange={(e) => act(() => setCategory(t.id, e.target.value))}>
                        {(categories.length ? categories : [t.category]).map((c) => <option key={c} value={c}>{c}</option>)}
                      </select>
                    </td>
                    <td>
                      {t.duplicateOfId ? (
                        <>
                          Possible duplicate{' '}
                          <button onClick={() => act(() => notDuplicate(t.id))}>Not a duplicate</button>{' '}
                          <button onClick={() => act(() => deleteTransaction(t.id))}>Delete</button>
                        </>
                      ) : t.isTransfer ? (
                        <>
                          Transfer <button onClick={() => act(() => setTransfer(t.id, false))}>Not a transfer</button>
                        </>
                      ) : t.isRefund ? (
                        t.refundOfId ? 'Refund' : 'Refund (no matching purchase)'
                      ) : (
                        <button onClick={() => act(() => setTransfer(t.id, true))}>Mark as transfer</button>
                      )}
                    </td>
                    <td align="right" style={{ color: t.type === 'Income' ? '#1b7f3b' : undefined }}>
                      {t.type === 'Income' ? '+' : '-'}{inr.format(t.amount)}
                    </td>
                    <td>
                      <button onClick={() => { setEditError(null); setEditingId(t.id) }}>Edit</button>
                    </td>
                  </tr>
                ),
              )}
            </tbody>
          </table>
          <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
            <button onClick={() => setPage((p) => p - 1)} disabled={page <= 1}>Previous</button>
            <span>{from}-{to} of {total} (page {shownPage} of {pages})</span>
            <button onClick={() => setPage((p) => p + 1)} disabled={page >= pages}>Next</button>
          </div>
        </>
      )}
    </section>
  )
}
