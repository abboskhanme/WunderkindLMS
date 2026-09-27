/**
 * HR topshiriqlari — sana, miqdor va isbot fayllari bilan ishlash.
 *
 * Eski xodim ilovasidagi `hrapp/lib/{date,format,proofs,attachments}.ts` ning
 * JS ko'chirmasi, mantiq o'zgarmagan.
 *
 * SANALAR — xodim mintaqasida (`Asia/Tashkent`), qurilma soatiga TAYANILMAYDI:
 * telefon soati noto'g'ri bo'lishi mumkin, "bugun" esa serverdan keladi
 * (`GET /hr/me` → `today`). Kun qo'shish `Date.UTC` ustida — yoz vaqti siljishi
 * sanani sakratmasin.
 */

/* ------------------------------------------------------------------- sanalar */

const MONTHS_GENITIVE = [
  'yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun',
  'iyul', 'avgust', 'sentabr', 'oktabr', 'noyabr', 'dekabr',
]

/** `Date.getUTCDay()` tartibida (0 = yakshanba). */
const WEEKDAYS_SHORT = ['Ya', 'Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh']
const WEEKDAYS_LONG = ['yakshanba', 'dushanba', 'seshanba', 'chorshanba', 'payshanba', 'juma', 'shanba']

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/

function toUtcDate(iso) {
  const match = ISO_DATE.exec(String(iso ?? '').slice(0, 10))
  if (!match) return new Date(Number.NaN)
  return new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])))
}

function fromUtcDate(date) {
  const y = String(date.getUTCFullYear()).padStart(4, '0')
  const m = String(date.getUTCMonth() + 1).padStart(2, '0')
  const d = String(date.getUTCDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

/** Mintaqadagi bugungi sana. Mintaqa noto'g'ri bo'lsa — qurilma sanasi. */
export function todayIn(timezone) {
  try {
    return new Intl.DateTimeFormat('en-CA', {
      timeZone: timezone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).format(new Date())
  } catch {
    const now = new Date()
    return fromUtcDate(new Date(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate())))
  }
}

export function addDays(iso, days) {
  const date = toUtcDate(iso)
  date.setUTCDate(date.getUTCDate() + days)
  return fromUtcDate(date)
}

/** `a` va `b` orasidagi to'liq kunlar (`a - b`). */
export function diffDays(a, b) {
  return Math.round((toUtcDate(a).getTime() - toUtcDate(b).getTime()) / 86_400_000)
}

/** Oyning kuni — sana lentasi uchun. */
export function dayOfMonth(iso) {
  return toUtcDate(iso).getUTCDate()
}

/** `Du`, `Se`, … */
export function shortWeekday(iso) {
  return WEEKDAYS_SHORT[toUtcDate(iso).getUTCDay()] ?? ''
}

/** `1-sentabr` */
export function dayMonthLabel(iso) {
  const date = toUtcDate(iso)
  return `${date.getUTCDate()}-${MONTHS_GENITIVE[date.getUTCMonth()] ?? ''}`
}

/** `1-sentabr 2026` — davr sarlavhasi uchun. */
export function dayMonthYearLabel(iso) {
  return `${dayMonthLabel(iso)} ${toUtcDate(iso).getUTCFullYear()}`
}

/** «Bugun», «Kecha», «Ertaga», aks holda «1-sentabr, dushanba». */
export function dateHeadline(iso, today, labels) {
  const delta = diffDays(iso, today)
  if (delta === 0) return labels.today
  if (delta === -1) return labels.yesterday
  if (delta === 1) return labels.tomorrow
  return `${dayMonthLabel(iso)}, ${WEEKDAYS_LONG[toUtcDate(iso).getUTCDay()] ?? ''}`
}

/**
 * Mintaqaning `at` payti uchun siljishi, daqiqada (sharqqa musbat).
 * `timeZoneName: 'longOffset'` ATAYIN yo'q — eski WebView'larda ishlamaydi.
 */
function offsetMinutes(timezone, at) {
  try {
    const parts = new Intl.DateTimeFormat('en-US', {
      timeZone: timezone,
      hour12: false,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    }).formatToParts(at)
    const read = (type) => {
      const found = parts.find((p) => p.type === type)
      return found ? Number(found.value) : 0
    }
    const wall = Date.UTC(read('year'), read('month') - 1, read('day'), read('hour') % 24, read('minute'), read('second'))
    return Math.round((wall - at.getTime()) / 60_000)
  } catch {
    // Mintaqa noma'lum — Toshkent (UTC+5, yoz vaqti yo'q).
    return 300
  }
}

function offsetLabel(minutes) {
  const sign = minutes < 0 ? '-' : '+'
  const abs = Math.abs(minutes)
  return `${sign}${String(Math.floor(abs / 60)).padStart(2, '0')}:${String(abs % 60).padStart(2, '0')}`
}

/** `('2026-09-02', 23, 59, 'Asia/Tashkent')` → `2026-09-02T23:59:00+05:00`. */
export function localIsoAt(iso, hours, minutes, timezone) {
  const hh = String(hours).padStart(2, '0')
  const mm = String(minutes).padStart(2, '0')
  const guess = new Date(`${iso}T${hh}:${mm}:00Z`)
  const rough = offsetMinutes(timezone, guess)
  const actual = new Date(guess.getTime() - rough * 60_000)
  return `${iso}T${hh}:${mm}:00${offsetLabel(offsetMinutes(timezone, actual))}`
}

function parseInstant(value) {
  if (!value) return null
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date
}

/** Paytning mintaqadagi sanasi (`YYYY-MM-DD`). */
export function instantDate(value, timezone) {
  const date = parseInstant(value)
  if (!date) return null
  try {
    return new Intl.DateTimeFormat('en-CA', {
      timeZone: timezone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).format(date)
  } catch {
    return null
  }
}

/** Paytning mintaqadagi soati (`18:00`). */
export function instantTime(value, timezone) {
  const date = parseInstant(value)
  if (!date) return null
  try {
    return new Intl.DateTimeFormat('en-GB', {
      timeZone: timezone,
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    }).format(date)
  } catch {
    return null
  }
}

/** Muddat yorlig'i: ko'rilayotgan kunda — faqat soat, boshqa kunda — `14-sentabr, 18:00`. */
export function dueLabel(dueAt, timezone, viewedDate) {
  const date = instantDate(dueAt, timezone)
  const time = instantTime(dueAt, timezone)
  if (date === null || time === null) return null
  if (date === viewedDate) return time
  return `${dayMonthLabel(date)}, ${time}`
}

/* ------------------------------------------------------------------ miqdorlar */

/** Satr (`"147.00"`) yoki son — songa. Noma'lum bo'lsa 0. */
export function toNumber(value) {
  if (typeof value === 'number') return Number.isFinite(value) ? value : 0
  if (typeof value === 'string') {
    const parsed = Number(value.trim().replace(',', '.'))
    return Number.isFinite(parsed) ? parsed : 0
  }
  return 0
}

/** `147`, `12,5`, `147 200` — minglar probel bilan. */
export function formatAmount(value) {
  const num = typeof value === 'number' ? value : toNumber(value ?? null)
  const rounded = Math.round(num * 100) / 100
  const [whole = '0', fraction] = Math.abs(rounded).toFixed(2).split('.')
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, ' ')
  const sign = rounded < 0 ? '−' : ''
  if (fraction === undefined || fraction === '00') return `${sign}${grouped}`
  return `${sign}${grouped},${fraction.replace(/0$/, '')}`
}

/** 0..100 oralig'idagi butun foiz (`max <= 0` bo'lsa — 0). */
export function percentOf(value, max) {
  if (max <= 0) return 0
  return Math.min(100, Math.max(0, Math.round((value / max) * 100)))
}

/** Bayt → MB (bir xonali). */
export function toMegabytes(bytes) {
  return (bytes / (1024 * 1024)).toFixed(1).replace(/\.0$/, '')
}

/** Soniya → `1:23`; noma'lum/nol — `null` ("0:00" deb yolg'on yozilmaydi). */
export function formatDuration(seconds) {
  if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds <= 0) return null
  const total = Math.round(seconds)
  const mm = Math.floor(total / 60) % 60
  const ss = total % 60
  const hh = Math.floor(total / 3600)
  const tail = `${String(mm).padStart(hh > 0 ? 2 : 1, '0')}:${String(ss).padStart(2, '0')}`
  return hh > 0 ? `${hh}:${tail}` : tail
}

/** Kiritilgan miqdor. Manfiy — ATAYIN ruxsat (ortiqcha yozilganini tuzatish uchun). */
export function parseAmountInput(raw) {
  const cleaned = String(raw).trim().replace(',', '.')
  if (cleaned.length === 0) return null
  if (!/^-?\d+(\.\d{1,2})?$/.test(cleaned)) return null
  const value = Number(cleaned)
  return Number.isFinite(value) ? value : null
}

/** `Decimal` uchun so'rov qiymati — float aniqligi yo'qolmasin (`"12.50"`). */
export function amountToPayload(value) {
  return (Math.round(value * 100) / 100).toFixed(2)
}

/* -------------------------------------------------------------- isbot fayllari */

/** Backenddagi `MAX_PROOFS_PER_TASK`, `MAX_PROOF_BYTES`, `MAX_VOICE_SECONDS` bilan bir xil. */
export const MAX_PROOFS = 5
export const MAX_PROOF_BYTES = 10 * 1024 * 1024
export const MAX_VOICE_SECONDS = 300

/**
 * Brauzerda IJRO ETILADIGAN turlar — server rad etadi (saqlangan XSS). Bu —
 * o'sha rad javobining mijozdagi OLDINDAN ogohlantirishi (10 MB ni bekorga
 * yuklab, keyin xato ko'rmaslik uchun). Haqiqiy himoya har doim serverda.
 */
const EXECUTABLE_TYPES = [
  'text/html',
  'application/xhtml+xml',
  'application/xhtml',
  'image/svg+xml',
  'text/xml',
  'application/xml',
  'text/javascript',
  'application/javascript',
  'application/x-javascript',
]

const EXECUTABLE_EXTENSIONS = /\.(html?|xhtml|svgz?|xml|m?js|jsx)$/i

/**
 * Faylni tekshiradi: `null` — joyida, aks holda o'zbekcha sabab. `file.type`
 * bo'sh bo'lsa ham o'tkaziladi — ba'zi Android WebView'lar turni umuman bermaydi.
 */
export function validateProofFile(file) {
  const type = String(file.type || '').toLowerCase().split(';')[0].trim()
  if ((type && EXECUTABLE_TYPES.includes(type)) || EXECUTABLE_EXTENSIONS.test(file.name)) {
    return `«${file.name}» — bu turdagi faylni isbot qilib bo‘lmaydi (HTML, SVG yoki skript). Boshqa formatda yuboring — rasm, hujjat, jadval, arxiv, ovoz yoki video.`
  }
  if (file.size === 0) return `«${file.name}» bo‘sh (0 bayt) — fayl to‘liq tanlanmadi.`
  if (file.size > MAX_PROOF_BYTES) {
    return `«${file.name}» juda katta (${toMegabytes(file.size)} MB). Chegara — ${toMegabytes(MAX_PROOF_BYTES)} MB.`
  }
  return null
}

/** Audio faylmi (tur yoki kengaytma bo'yicha). */
export function isAudioFile(file) {
  return String(file.type || '').startsWith('audio/') || /\.(m4a|mp3|ogg|oga|opus|webm|wav|aac|amr)$/i.test(file.name)
}

/**
 * Audio faylning davomiyligi (soniya) yoki `null` — o'qib bo'lmasa (qaror
 * serverga qoladi). 4 soniyada javob bo'lmasa ham `null`.
 */
export function audioDuration(file) {
  return new Promise((resolve) => {
    let url = null
    let done = false
    const finish = (value) => {
      if (done) return
      done = true
      if (url) URL.revokeObjectURL(url)
      resolve(value)
    }
    try {
      url = URL.createObjectURL(file)
      const audio = document.createElement('audio')
      audio.preload = 'metadata'
      audio.onloadedmetadata = () => finish(Number.isFinite(audio.duration) ? audio.duration : null)
      audio.onerror = () => finish(null)
      audio.src = url
      setTimeout(() => finish(null), 4000)
    } catch {
      finish(null)
    }
  })
}

/* ------------------------------------------------------------ rahbar ovozi */

/**
 * Topshiriqdagi rahbarning ovozli izohi (bo'lmasa `null`). Avval
 * `attachments` — faqat u yerda davomiylik bor; `voiceUrl` — zaxira.
 */
export function pickVoice(task) {
  const attachment = (task.attachments || []).find(
    (a) => a.kind === 'voice' && typeof a.url === 'string' && a.url.length > 0,
  )
  if (attachment) return { url: attachment.url, durationSeconds: attachment.durationSeconds ?? null }
  if (typeof task.voiceUrl === 'string' && task.voiceUrl.length > 0) {
    return { url: task.voiceUrl, durationSeconds: null }
  }
  return null
}
