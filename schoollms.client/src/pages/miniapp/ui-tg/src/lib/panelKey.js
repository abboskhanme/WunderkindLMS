/**
 * "🏫 Panelni ochish" tugmasining shaxsiy kaliti (`?k=…`).
 *
 * Telegram botdagi DOIMIY klaviatura tugmasidan ochilgan Mini App'ga `initData`
 * bermaydi (rasmiy qoida). Bot shuning uchun tugma manziliga shu chat egasining
 * kalitini qo'yadi (`SchoolLms.Application/Services/TelegramPanelKeys.cs`), ilova
 * esa uni `POST /api/tg/auth/key` da oddiy sessiyaga almashtiradi.
 *
 * ⚠️ Kalit PAROL kabi: modul yuklanishi bilan manzildan OLIB TASHLANADI
 * (`replaceState`) — sahifa ulashilsa yoki nusxalansa ham ketmasin. Xotirada
 * qoladi: token eskirsa (401) ilova shu kalit bilan jimgina qayta kiradi.
 * Hash (`#tgWebApp…` — Telegram parametrlari) tegilmaydi.
 */

const PARAM = 'k'

function capture() {
  try {
    const url = new URL(window.location.href)
    const key = url.searchParams.get(PARAM)
    if (key === null) return ''
    url.searchParams.delete(PARAM)
    window.history.replaceState(window.history.state, '', url.pathname + url.search + url.hash)
    return key.trim()
  } catch {
    return ''
  }
}

export let panelKey = capture()

/**
 * "Chiqish" dan keyin: kalit egasining bog'lanishi uzildi, kalit endi hech narsa ochmaydi. Xotirada
 * qolsa, keyingi 401 da ilova shu kalit bilan qayta kirishga urinib, "Tugma eskirgan" ko'rsatardi.
 */
export function forgetPanelKey() {
  panelKey = ''
}

/**
 * Tugmadan ochilgan ilovada qayta ulangach (2026-10-07): server yangi tugmaning kalitini ham
 * qaytaradi — token eskirsa, ilova shu kalit bilan o'zi qayta kiradi.
 */
export function setPanelKey(key) {
  panelKey = typeof key === 'string' ? key.trim() : ''
}
