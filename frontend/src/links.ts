export type HashAction = { kind: 'verify' | 'reset'; token: string }

/**
 * Emailed links look like https://host/#verify=TOKEN or /#reset=TOKEN. The token sits after the # so the browser never
 * sends it to a server, a log or another site; the page reads it here and posts it in a request body.
 */
export function parseHashAction(hash: string): HashAction | null {
  const m = /^#(verify|reset)=([A-Za-z0-9_-]{20,100})$/.exec(hash)
  return m ? { kind: m[1] as HashAction['kind'], token: m[2] } : null
}

/** Groups a secret into blocks of four to make it easier to type into an authenticator app. */
export function groupSecret(secret: string): string {
  return secret.replace(/\s+/g, '').replace(/(.{4})/g, '$1 ').trim()
}

/** The password and code rules shown to the user before the server checks them. */
export function newPasswordProblem(password: string, confirm: string): string | null {
  if (password.length < 8) return 'Use at least 8 characters.'
  if (password.length > 128) return 'Use at most 128 characters.'
  if (password !== confirm) return 'The two passwords do not match.'
  return null
}
