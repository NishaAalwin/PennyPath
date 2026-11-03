// Browser end-to-end check: drives the REAL app in Edge/Chrome over the DevTools protocol and fails if anything is off.
// Covers sign-up, the hardened session cookie, import, summary, charts (hover and keyboard), search/filter/edit,
// the privacy panel, CSP probes, logout and replay, and every console error the browser reports.
//
// Usage (the stack must be running: docker compose up -d --build):
//   1. Start a browser with remote debugging on port 9333, ignoring the local certificate:
//      msedge --headless=new --remote-debugging-port=9333 --user-data-dir=<empty temp dir> --ignore-certificate-errors about:blank
//   2. node scripts/browser-e2e.mjs [output-dir-for-screenshots]     (Node 22+; it creates a new user each run)
// Not wired into CI (needs a browser and the full stack); see docs/operations.md.
import { tmpdir } from 'node:os'
import { writeFileSync } from 'node:fs'

const BASE = 'https://localhost:8443/'
const SP = process.argv[2] ?? tmpdir()
const EMAIL = `e2e${Date.now()}@example.com`
const PASSWORD = 'Sup3r-Secret-Pass!9x'

const targets = await (await fetch('http://127.0.0.1:9333/json')).json()
const page = targets.find((t) => t.type === 'page')
const ws = new WebSocket(page.webSocketDebuggerUrl)
await new Promise((r) => (ws.onopen = r))

let nextId = 0
const pending = new Map()
const events = []
ws.onmessage = (e) => {
  const m = JSON.parse(e.data)
  if (m.id) {
    pending.get(m.id)?.(m)
    pending.delete(m.id)
  } else events.push(m)
}
const send = (method, params = {}) =>
  new Promise((res, rej) => {
    const id = ++nextId
    pending.set(id, (m) => (m.error ? rej(new Error(method + ': ' + JSON.stringify(m.error))) : res(m.result)))
    ws.send(JSON.stringify({ id, method, params }))
  })

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
async function ev(expression) {
  const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true })
  if (r.exceptionDetails) throw new Error('evaluate failed: ' + (r.exceptionDetails.exception?.description ?? r.exceptionDetails.text) + '\n' + expression.slice(0, 200))
  return r.result.value
}
async function waitFor(expression, label, timeout = 10000) {
  const t0 = Date.now()
  while (Date.now() - t0 < timeout) {
    if (await ev(`Boolean(${expression})`)) return
    await sleep(100)
  }
  throw new Error('timeout waiting for: ' + label)
}

const results = []
const check = (name, ok, detail = '') => {
  results.push({ name, ok: Boolean(ok), detail })
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  -> ' + detail : ''}`)
}

// Helpers that run inside the page. React inputs need the native setter plus an input event.
const HELPERS = `
  window.__setValue = (el, v) => {
    const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : el instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, v);
    el.dispatchEvent(new Event(el instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }));
  };
  window.__btn = (text) => [...document.querySelectorAll('button')].find((b) => b.textContent.trim() === text);
  window.__text = () => document.body.innerText;
  window.__txRows = () => { const t = [...document.querySelectorAll('table')].find((x) => x.querySelector('thead')?.textContent.includes('Where / what')); return t ? [...t.querySelectorAll('tbody tr')] : []; };
`
async function install() { await ev(HELPERS) }

await send('Page.enable')
await send('Runtime.enable')
await send('Log.enable')
await send('Network.enable')
await send('Emulation.setDeviceMetricsOverride', { width: 520, height: 1800, deviceScaleFactor: 1, mobile: false })

// ---------------------------------------------------------------- load
await send('Page.navigate', { url: BASE })
await waitFor(`document.body && document.body.innerText.includes('PennyPath')`, 'page loads')
await install()
await waitFor(`document.querySelector('input[type=email]')`, 'login form')
check('Landing page shows the log-in form to a signed-out visitor', await ev(`__text().includes('Log in')`))
check('Page is served over HTTPS', (await ev('location.protocol')) === 'https:')

// ---------------------------------------------------------------- sign up
await ev(`__btn('Need an account? Sign up').click()`)
await waitFor(`__btn('Sign up')`, 'sign-up form')
await ev(`__setValue(document.querySelector('input[type=email]'), ${JSON.stringify(EMAIL)}); __setValue(document.querySelector('input[type=password]'), ${JSON.stringify(PASSWORD)});`)
await ev(`__btn('Sign up').click()`)
await waitFor(`__text().includes('Signed in as')`, 'signed in')
check('Sign-up through the real form signs the user in', await ev(`__text().includes(${JSON.stringify(EMAIL)})`))

// ---------------------------------------------------------------- where is the token?
const cookies = (await send('Network.getAllCookies')).cookies.filter((c) => c.name.includes('pp_session'))
check('Session cookie exists with the hardened flags',
  cookies.length === 1 && cookies[0].name === '__Host-pp_session' && cookies[0].httpOnly && cookies[0].secure && cookies[0].sameSite === 'Strict' && cookies[0].path === '/',
  cookies[0] && `${cookies[0].name} httpOnly=${cookies[0].httpOnly} secure=${cookies[0].secure} sameSite=${cookies[0].sameSite} path=${cookies[0].path}`)
check('Page scripts cannot read the session cookie (document.cookie)', !(await ev('document.cookie')).includes('pp_session'), `document.cookie="${await ev('document.cookie')}"`)
const storage = await ev(`JSON.stringify({ local: Object.keys(localStorage), session: Object.keys(sessionStorage) })`)
check('No token or session data in localStorage / sessionStorage', storage === '{"local":[],"session":[]}', storage)

// ---------------------------------------------------------------- import
const SMS = [
  'Dear Customer, Rs. 15,000.00 credited to your A/c XX5678 on 01-11-2026 by salary',
  'Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26 Ref 4567890123',
  'INR 250.00 debited from A/c XX1234 on 02-11-26 to SWIGGY. UPI Ref 4567890123',
  'Rs 450 spent on card XX9876 at AMAZON on 02-Nov-26. Avl bal Rs 12,000.00',
  'Refund of Rs 100.00 credited to A/c XX1234 on 03-11-26 from AMAZON.',
  'Hello, your parcel is out for delivery',
].join('\n\n')
await ev(`__setValue(document.querySelector('textarea'), ${JSON.stringify(SMS)})`)
await ev(`__btn('Import').click()`)
await waitFor(`__text().includes('Imported 5')`, 'import result')
const importLine = await ev(`__text().split('\\n').find((l) => l.startsWith('Imported'))`)
check('Import reports 5 imported, 1 possible duplicate, 1 unreadable', /Imported 5/.test(importLine) && /1 look like duplicates/.test(importLine) && /could not read 1/.test(importLine), importLine)

// ---------------------------------------------------------------- summary
await waitFor(`__text().includes('Saved')`, 'summary')
const summaryLine = await ev(`__text().split('\\n').find((l) => l.includes('Income') && l.includes('Spent'))`)
check('Summary: income 15,000, spent 600, saved 14,400', /15,000\.00/.test(summaryLine) && /₹600\.00/.test(summaryLine) && /14,400\.00/.test(summaryLine), summaryLine)
check('Summary says one duplicate is left out of the totals', await ev(`__text().includes('1 possible duplicate or transfer entry is left out')`))

// ---------------------------------------------------------------- charts
const chart = await ev(`JSON.stringify({ svg: document.querySelectorAll('svg[role=group]').length, slots: document.querySelectorAll('.viz-slot').length, bars: document.querySelectorAll('.viz-bar-row').length, legend: [...document.querySelectorAll('.viz-legend li')].map((l) => l.textContent), tables: document.querySelectorAll('.viz details').length })`)
const c = JSON.parse(chart)
check('Trend chart: one SVG, 6 month slots, legend Income/Spent', c.svg === 1 && c.slots === 6 && c.legend.join() === 'Income,Spent', chart)
check('Ranked bars rendered for place and category, each with a table view', c.bars >= 4 && c.tables >= 3, `bars=${c.bars} tables=${c.tables}`)

await ev(`(() => { const s = [...document.querySelectorAll('.viz-slot')].at(-1); s.dispatchEvent(new PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' })); })()`)
await waitFor(`document.querySelector('.viz-tooltip')`, 'tooltip on pointer')
check('Hover tooltip appears with income / spent / saved', await ev(`(() => { const t = document.querySelector('.viz-tooltip').innerText; return t.includes('Income') && t.includes('Spent') && t.includes('Saved') && t.includes('15,000.00') })()`),
  (await ev(`document.querySelector('.viz-tooltip').innerText.replace(/\\n/g, ' | ')`)))
await ev(`(() => { const s = [...document.querySelectorAll('.viz-slot')].at(-1); s.dispatchEvent(new PointerEvent('pointerout', { bubbles: true, pointerType: 'mouse' })); })()`)
await waitFor(`!document.querySelector('.viz-tooltip')`, 'tooltip hides')
check('Tooltip hides when the pointer leaves', true)
await ev(`[...document.querySelectorAll('.viz-slot')][3].focus()`)
await waitFor(`document.querySelector('.viz-tooltip')`, 'tooltip on keyboard focus')
check('Keyboard focus on a month shows the same tooltip', true)
await ev(`document.activeElement.blur()`)

// ---------------------------------------------------------------- transaction list
await waitFor(`__txRows().length >= 5`, 'list rows')
const rowCount = await ev(`__txRows().length`)
check('List shows this month\'s 5 rows including the flagged duplicate', rowCount >= 5, `rows=${rowCount}`)
check('Duplicate row is labelled with the two actions', await ev(`__text().includes('Possible duplicate') && Boolean(__btn('Not a duplicate')) && Boolean(__btn('Delete'))`))
check('Refund row is labelled as a refund', await ev(`__text().includes('Refund')`))

await ev(`__setValue(document.querySelector('input[type=search]'), 'swig')`)
await sleep(700)
const searched = await ev(`JSON.stringify(__txRows().map((r) => r.children[1]?.textContent))`)
check('Search "swig" narrows the list to the two Swiggy rows', JSON.parse(searched).length === 2 && JSON.parse(searched).every((m) => m === 'Swiggy'), searched)
await ev(`__setValue(document.querySelector('input[type=search]'), '')`)
await sleep(700)
await ev(`__setValue(document.querySelector('select[aria-label="Filter by money in or out"]'), 'Income')`)
await sleep(500)
const incomeRows = await ev(`JSON.stringify(__txRows().map((r) => r.children[1]?.textContent))`)
check('Filter money-in shows only the salary and the refund', JSON.parse(incomeRows).length === 2, incomeRows)
await ev(`__setValue(document.querySelector('select[aria-label="Filter by money in or out"]'), '')`)
await sleep(500)

// edit: change Amazon 450 -> 500
await ev(`(() => { const tr = __txRows().find((r) => r.children[1]?.textContent === 'Amazon' && r.textContent.includes('₹450.00')); [...tr.querySelectorAll('button')].find((b) => b.textContent === 'Edit').click(); })()`)
await waitFor(`__btn('Save')`, 'edit form')
await ev(`__setValue(document.querySelector('input[type=number]'), '-5')`)
await ev(`__btn('Save').click()`)
await waitFor(`document.querySelector('[role=alert]')`, 'validation error shown')
check('An invalid edit (-5) is refused with the server message', await ev(`document.querySelector('[role=alert]').textContent.includes('Amount must be more than 0')`), await ev(`document.querySelector('[role=alert]').textContent`))
await ev(`__setValue(document.querySelector('input[type=number]'), '500')`)
await ev(`__btn('Save').click()`)
await waitFor(`!__btn('Save')`, 'edit saved')
await waitFor(`__text().includes('₹650.00')`, 'summary reflects the edit')
check('A valid edit (450 -> 500) updates the row and the summary (spent 250+500-100 = 650)', await ev(`__text().includes('₹500.00') && __text().includes('₹650.00')`))

// ---------------------------------------------------------------- category, duplicate, transfer
await ev("(() => { const tr = __txRows().find((r) => r.children[1]?.textContent === 'Amazon' && r.textContent.includes('₹500.00')); __setValue(tr.querySelector('select'), 'Entertainment'); })()")
await waitFor("[...document.querySelectorAll('.viz-bar-name')].some((n) => n.textContent === 'Entertainment')", 'category change reaches the summary')
check('Changing a row\'s category in the dropdown moves its spending to that category in the summary', true)

await ev("(() => { const tr = __txRows().find((r) => r.textContent.includes('Possible duplicate')); [...tr.querySelectorAll('button')].find((b) => b.textContent === 'Not a duplicate').click(); })()")
await waitFor("__text().includes('Spent ₹900.00')", 'duplicate counted after "Not a duplicate"')
check('"Not a duplicate" puts the row back into the totals (spent 650 -> 900)', await ev("!__text().includes('Possible duplicate')"))

await ev("(() => { const tr = __txRows().find((r) => r.children[1]?.textContent === 'Swiggy'); [...tr.querySelectorAll('button')].find((b) => b.textContent === 'Mark as transfer').click(); })()")
await waitFor("__text().includes('Spent ₹650.00') && __text().includes('Not a transfer')", 'transfer excluded from totals')
check('"Mark as transfer" takes the row out of the totals (spent 900 -> 650) and shows "Not a transfer"', true)

// ---------------------------------------------------------------- privacy panel
check('Privacy panel: AI consent is off by default', await ev(`(() => { const c = [...document.querySelectorAll('input[type=checkbox]')].find((i) => i.parentElement.textContent.includes('Allow AI')); return c && !c.checked })()`))
await ev(`[...document.querySelectorAll('input[type=checkbox]')].find((i) => i.parentElement.textContent.includes('Allow AI')).click()`)
await waitFor(`[...document.querySelectorAll('input[type=checkbox]')].find((i) => i.parentElement.textContent.includes('Allow AI')).checked`, 'consent on')
check('Turning AI consent on is saved by the server', true)
await ev(`document.querySelector('details > summary').scrollIntoView(); [...document.querySelectorAll('summary')].find((s) => s.textContent.includes('Recent security activity')).click()`)
await sleep(300)
const activity = await ev(`[...document.querySelectorAll('summary')].find((s) => s.textContent.includes('Recent security activity')).parentElement.innerText`)
check('Security activity lists the account creation and the consent change', /Account created/.test(activity) && /AI features allowed/.test(activity), activity.replace(/\n/g, ' | ').slice(0, 160))

// ---------------------------------------------------------------- screenshots
const shot = async (name) => writeFileSync(`${SP}/${name}.png`, Buffer.from((await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true })).data, 'base64'))
await ev(`window.scrollTo(0, 0)`)
await shot('app-light')
await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'dark' }] })
await sleep(300)
await shot('app-dark')
await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: 'light' }] })

// ---------------------------------------------------------------- CSP really blocks injected code
const header = await ev("fetch('/').then((r) => r.headers.get('content-security-policy'))")
check('CSP header has no unsafe-eval and no unsafe-inline for scripts', !/unsafe-eval/.test(header) && !/script-src[^;]*unsafe-inline/.test(header), header.slice(0, 120))
const blob = await ev("new Promise((resolve) => { const s = document.createElement('script'); s.src = URL.createObjectURL(new Blob(['window.__blob = 1'])); s.onload = () => resolve('ran'); s.onerror = () => resolve('blocked'); document.body.appendChild(s); })")
check('CSP blocks a script loaded from a blob: URL', blob === 'blocked', blob)
const injected = await ev(`(() => { window.__pwned = false; const s = document.createElement('script'); s.textContent = 'window.__pwned = true'; document.body.appendChild(s); return window.__pwned })()`)
check('CSP blocks an injected inline <script>', injected === false)
const xhr = await ev(`fetch('https://example.com/', { mode: 'no-cors' }).then(() => 'allowed', (e) => 'blocked: ' + e.message)`)
check('CSP blocks a request to another origin (connect-src self)', xhr.startsWith('blocked'), xhr)

// ---------------------------------------------------------------- paging
const MANY = Array.from({ length: 30 }, (_, i) => 'Rs 10.00 spent at PAGETEST' + (i + 1) + ' on 02-11-26').join('\n\n')
await ev(`__setValue(document.querySelector('textarea'), ${JSON.stringify(MANY)})`)
await ev("__btn('Import').click()")
await waitFor("__text().includes('Imported 30')", 'bulk import')
await waitFor("__text().includes('1-25 of 35')", 'first page of two')
check('Paging: 35 rows show as "1-25 of 35 (page 1 of 2)" with Previous disabled', await ev("__text().includes('page 1 of 2') && __btn('Previous').disabled && !__btn('Next').disabled") && (await ev('__txRows().length')) === 25)
await ev("__btn('Next').click()")
await waitFor("__text().includes('26-35 of 35')", 'second page')
const rows2 = await ev('__txRows().length'), nextOff = await ev("__btn('Next').disabled")
check('Next shows the remaining 10 rows ("26-35 of 35") with Next disabled', rows2 === 10 && nextOff, `rows=${rows2} nextDisabled=${nextOff}`)
await ev("__btn('Previous').click()")
await waitFor("__text().includes('1-25 of 35')", 'back to first page')
check('Previous returns to page 1', (await ev('__txRows().length')) === 25)
await ev("__setValue(document.querySelector('input[type=search]'), 'pagetest7')")
await sleep(700)
check('Searching while on a later page resets to page 1 and finds the one match', (await ev('__txRows().length')) === 1 && (await ev("__text().includes('1-1 of 1')")))
await ev("__setValue(document.querySelector('input[type=search]'), '')")
await sleep(700)

// ---------------------------------------------------------------- logout + stolen session
const sessionCookie = (await send('Network.getAllCookies')).cookies.find((c) => c.name === '__Host-pp_session')
await ev(`__btn('Log out').click()`)
await waitFor(`document.querySelector('input[type=email]')`, 'back at the log-in form')
check('Log out returns to the log-in form', true)
const afterCookie = (await send('Network.getAllCookies')).cookies.find((c) => c.name === '__Host-pp_session')
check('Log out clears the cookie in the browser', !afterCookie)
const replay = await ev(`fetch('/api/auth/me', { headers: { Cookie: 'x' } }).then((r) => r.status)`)
check('The API now answers 401 to this browser', replay === 401, String(replay))
// replay the old token with a hand-built request through CDP (as an attacker with a copied cookie would)
const stolen = await ev(`fetch('/api/auth/me').then((r) => r.status)`)
check('No session is left after logout', stolen === 401)

// ---------------------------------------------------------------- log in again
await ev(`__setValue(document.querySelector('input[type=email]'), ${JSON.stringify(EMAIL)}); __setValue(document.querySelector('input[type=password]'), ${JSON.stringify(PASSWORD)});`)
await ev(`__btn('Log in').click()`)
await waitFor(`__text().includes('Signed in as')`, 'signed in again')
await waitFor(`__text().includes('₹950.00')`, 'data still there')
check('Logging in again works and all the data is still there (spent 650 + 300 imported = 950)', true)

// ---------------------------------------------------------------- wrong password shows a friendly error
await ev(`__btn('Log out').click()`)
await waitFor(`document.querySelector('input[type=email]')`, 'login form again')
await ev(`__setValue(document.querySelector('input[type=email]'), ${JSON.stringify(EMAIL)}); __setValue(document.querySelector('input[type=password]'), 'wrong-password-1');`)
await ev(`__btn('Log in').click()`)
await waitFor(`document.querySelector('[role=alert]')`, 'error shown')
check('A wrong password shows "Invalid email or password."', await ev(`document.querySelector('[role=alert]').textContent === 'Invalid email or password.'`))

// ---------------------------------------------------------------- everything the browser complained about
const problems = []
for (const m of events) {
  if (m.method === 'Runtime.exceptionThrown') problems.push('exception: ' + (m.params.exceptionDetails.exception?.description ?? m.params.exceptionDetails.text))
  if (m.method === 'Runtime.consoleAPICalled' && ['error', 'warning'].includes(m.params.type)) problems.push('console.' + m.params.type + ': ' + m.params.args.map((a) => a.value ?? a.description).join(' '))
  if (m.method === 'Log.entryAdded' && ['error', 'warning'].includes(m.params.entry.level)) problems.push(`log.${m.params.entry.level} [${m.params.entry.source}]: ${m.params.entry.text} ${m.params.entry.url ?? ''}`)
  if (m.method === 'Network.loadingFailed' && !m.params.canceled) problems.push('network failed: ' + m.params.errorText + ' ' + (m.params.blockedReason ?? ''))
}
console.log('\n--- browser complaints (' + problems.length + ') ---')
for (const p of problems) console.log(' ' + p.slice(0, 220))
// Anything the browser complained about must be one of: a status code I provoked on purpose (401 before sign-in or
// after logout, 400 for the invalid edit, 401 for the wrong password) or one of my four CSP probes. The log is also
// replayed from earlier runs of this page, so counting would be misleading; classifying each line is not.
const expected = [
  /status of 401 .*\/api\/auth\/(me|login)/,
  /status of 400 .*\/api\/transactions\//,
  /Executing inline script violates/,
  /Connecting to 'https:\/\/example\.com\/' violates/,
  /Fetch API cannot load https:\/\/example\.com/,
  /Loading the script 'blob:/,
  /^network failed: {1,2}csp$/,
];
const unexpected = problems.filter((p) => !expected.some((re) => re.test(p)));
check('Every browser console error/warning is one I provoked on purpose (no stray errors, no CSP violation from the app itself)', unexpected.length === 0, unexpected.join(' || ').slice(0, 400));
check('The app itself triggered no CSP violation (all CSP reports are my probes)',
  problems.filter((p) => /Content Security Policy|Refused to/i.test(p)).every((p) => /inline script|example\.com|blob:/.test(p)))

const failed = results.filter((r) => !r.ok)
console.log(`\n${results.length - failed.length}/${results.length} checks passed`)
writeFileSync(`${SP}/e2e-results.json`, JSON.stringify({ results, problems }, null, 2))
ws.close()
process.exit(failed.length ? 1 : 0)
