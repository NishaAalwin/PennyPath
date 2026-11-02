export const API_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5000'

export type Health = { status: string; database: boolean }
export type AuthResponse = { token: string; expiresAt: string; email: string }

export class ApiError extends Error {
  status: number
  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

type Fetch = typeof fetch

export async function getHealth(fetchFn: Fetch = fetch): Promise<Health> {
  const res = await fetchFn(`${API_URL}/api/health`)
  if (!res.ok) throw new Error(`Health check failed: ${res.status}`)
  return res.json()
}

async function postAuth(path: string, email: string, password: string, fetchFn: Fetch): Promise<AuthResponse> {
  const res = await fetchFn(`${API_URL}/api/auth/${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  })
  if (!res.ok) {
    const body = await res.json().catch(() => null)
    const message = res.status === 429 ? 'Too many attempts. Try again in a minute.' : body?.error ?? 'Something went wrong.'
    throw new ApiError(message, res.status)
  }
  return res.json()
}

export const register = (email: string, password: string, fetchFn: Fetch = fetch) =>
  postAuth('register', email, password, fetchFn)

export const login = (email: string, password: string, fetchFn: Fetch = fetch) =>
  postAuth('login', email, password, fetchFn)

export async function getMe(token: string, fetchFn: Fetch = fetch): Promise<{ id: string; email: string }> {
  const res = await fetchFn(`${API_URL}/api/auth/me`, { headers: { Authorization: `Bearer ${token}` } })
  if (!res.ok) throw new ApiError('Session expired', res.status)
  return res.json()
}
