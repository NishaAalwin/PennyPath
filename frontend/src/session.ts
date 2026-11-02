const KEY = 'pennypath.token'

export function loadToken(): string | null {
  try {
    return localStorage.getItem(KEY)
  } catch {
    return null
  }
}

export function saveToken(token: string) {
  try {
    localStorage.setItem(KEY, token)
  } catch {
    /* storage unavailable: session lasts until reload */
  }
}

export function clearToken() {
  try {
    localStorage.removeItem(KEY)
  } catch {
    /* ignore */
  }
}
