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

/**
 * Server matn bermagan holatlar uchun odam tilidagi sabab. Ota-ona "Xatolik (403)"
 * dan hech narsa tushunmaydi; ASP.NET esa rol yoki ruxsat yetmaganda tanasiz
 * 403, topilmaganda `{ title: "Not Found" }` qaytaradi.
 */
const STATUS_TEXT = {
  403: "Bu bo'limni ko'rishga ruxsatingiz yo'q. Maktab ma'muriyatiga murojaat qiling.",
  404: "Ma'lumot topilmadi.",
  429: "So'rovlar juda ko'p. Birozdan keyin qayta urinib ko'ring.",
}

function statusText(status) {
  if (STATUS_TEXT[status]) return STATUS_TEXT[status]
  if (status >= 500) return "Serverda nosozlik. Birozdan keyin qayta urinib ko'ring."
  return `Xatolik (${status})`
}

export class ApiError extends Error {
  constructor(status, body) {
    super(body?.message || statusText(status))
    this.status = status
    this.code = body?.code
    this.body = body
  }
}

/** Mobil tarmoqda osilib qolgan so'rov abadiy "Yuklanmoqda…" ko'rsatib turmasin. */
const TIMEOUT_MS = 25_000

/** Tarmoq xatosi (internet yo'q, vaqt tugadi) — `status: 0`. */
async function send(url, init) {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), TIMEOUT_MS)
  try {
    return await fetch(url, { ...init, signal: controller.signal })
  } catch (e) {
    throw new ApiError(0, {
      message:
        e?.name === 'AbortError'
          ? "Server javob bermadi. Internetni tekshirib, qayta urinib ko'ring."
          : "Internet aloqasi yo'q. Tarmoqni tekshirib, qayta urinib ko'ring.",
    })
  } finally {
    clearTimeout(timer)
  }
}

let onUnauthorized = null
/** 401 bo'lganda ilovani kirish ekraniga qaytaradigan handler. */
export function setUnauthorizedHandler(fn) {
  onUnauthorized = fn
}

async function request(method, path, body, headers) {
  const token = tokenStore.get()
  const res = await send('/api' + path, {
    method,
    headers: {
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { Authorization: 'Bearer ' + token } : {}),
      ...headers,
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  // Faqat TOKEN bilan ketgan so'rovning 401 i — "sessiya eskirdi". Tokensiz
  // so'rovlar (kirish, bog'lash) 401 ni o'zi hal qiladi: aks holda noto'g'ri
  // parol ekranni qayta yuklab, xato matnini o'chirib yuborardi, eskirgan
  // Telegram imzosi esa `/tg/auth` ni cheksiz qayta chaqirardi.
  if (res.status === 401 && token) {
    tokenStore.clear()
    onUnauthorized?.()
  }

  const text = await res.text()
  const parsed = text ? safeJson(text) : null

  if (!res.ok) throw new ApiError(res.status, parsed)
  return parsed
}

/**
 * Multipart POST (a photo with a form). Same token and 401 rule as `request`; no `Content-Type`
 * header — `FormData` sets it with its boundary.
 */
async function requestForm(path, formData) {
  const token = tokenStore.get()
  const res = await send('/api' + path, {
    method: 'POST',
    headers: token ? { Authorization: 'Bearer ' + token } : {},
    body: formData,
  })
  if (res.status === 401 && token) {
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
  del: (p, headers) => request('DELETE', p, undefined, headers),
  /** Multipart POST — matn + rasm (taklif va shikoyatlar). */
  form: (p, formData) => requestForm(p, formData),
}
