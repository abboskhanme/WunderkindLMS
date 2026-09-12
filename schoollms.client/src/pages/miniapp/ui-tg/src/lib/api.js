/**
 * Mini App REST klienti.
 *
 * Bir origin (`/api`), token `sessionStorage` da: Mini App har ochilganda
 * Telegram imzosidan qaytadan token oladi, ya'ni uni brauzerda uzoq saqlashning
 * ma'nosi yo'q — va saqlamaslik xavfsizroq.
 */

const TOKEN_KEY = 'tg_token'

export const tokenStore = {
  get: () => {
    try {
      return sessionStorage.getItem(TOKEN_KEY)
    } catch {
      return null
    }
  },
  set: (t) => {
    try {
      sessionStorage.setItem(TOKEN_KEY, t)
    } catch {
      /* private rejim — token faqat xotirada qoladi */
    }
  },
  clear: () => {
    try {
      sessionStorage.removeItem(TOKEN_KEY)
    } catch {
      /* jim */
    }
  },
}

export class ApiError extends Error {
  constructor(status, body) {
    super(body?.message || `Xatolik (${status})`)
    this.status = status
    this.code = body?.code
    this.body = body
  }
}

let onUnauthorized = null
/** 401 bo'lganda ilovani kirish ekraniga qaytaradigan handler. */
export function setUnauthorizedHandler(fn) {
  onUnauthorized = fn
}

async function request(method, path, body) {
  const token = tokenStore.get()
  const res = await fetch('/api' + path, {
    method,
    headers: {
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { Authorization: 'Bearer ' + token } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  if (res.status === 401) {
    tokenStore.clear()
    onUnauthorized?.()
  }

  const text = await res.text()
  const parsed = text ? safeJson(text) : null

  if (!res.ok) throw new ApiError(res.status, parsed)
  return parsed
}

function safeJson(text) {
  try {
    return JSON.parse(text)
  } catch {
    return { message: text }
  }
}

export const api = {
  get: (p) => request('GET', p),
  post: (p, b) => request('POST', p, b ?? {}),
  put: (p, b) => request('PUT', p, b ?? {}),
  // DELETE tanasiz ketadi — parametrlar so'rov satrida (backend shunday kutadi).
  del: (p) => request('DELETE', p),
}
