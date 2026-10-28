/**
 * Mavsumiy baholash (seasonal assessment) — helpers for the teacher's Mini App screen.
 * Contract: docs/modules/admission-and-testing.md §5.12, §6.6, §8.6, §13 Q5.
 *
 * A copy of the teacher PWA's `pages/teacher/ui-web/src/lib/seasonal.js` (each app is a separate
 * build and the Mini App's Docker build sees only its own folder). Keep the two in step.
 *
 * SCALE. The score is 0–100 with two decimals. It is never converted to or from the 1–5 journal
 * scale, so it is never coloured like a journal grade either.
 */

/** Teacher permission key of the module (TeacherPermissions.SeasonalMarks). */
export const SEASONAL_PERM = 'seasonalMarks'

/** §13 Q5: every score column carries exactly this label. */
export const SCORE_LABEL = 'Ball (0–100)'

const SCORE_MIN = 0
const SCORE_MAX = 100
/** §8.6: a comment is either absent or at least three characters after trim. */
const COMMENT_MIN_LENGTH = 3

export const PERIOD_KIND_OPTIONS = [
  { value: 'monthly', label: 'Oylik' },
  { value: 'quarterly', label: 'Choraklik' },
  { value: 'yearly', label: 'Yillik' },
]

export const QUARTER_OPTIONS = [1, 2, 3, 4].map((q) => ({ value: q, label: `${q}-ch` }))

const MONTH_NAMES = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

export function periodKindLabel(kind) {
  return PERIOD_KIND_OPTIONS.find((o) => o.value === kind)?.label || ''
}

/** The school quarter a calendar month usually falls in — only a starting value the teacher can change. */
function guessQuarter(month) {
  if (month === 9 || month === 10) return 1
  if (month === 11 || month === 12) return 2
  if (month >= 1 && month <= 3) return 3
  return 4
}

/** A complete period: `{ kind, year, month, quarter }` — month/quarter kept even when unused. */
export function defaultPeriod(now = new Date()) {
  const month = now.getMonth() + 1
  return { kind: 'monthly', year: now.getFullYear(), month, quarter: guessQuarter(month) }
}

/** The years the stepper may reach: five back, one ahead (as the admin year list). */
export function yearBounds(now = new Date()) {
  const y = now.getFullYear()
  return { min: y - 5, max: y + 1 }
}

/** Moves a monthly period by `delta` months, crossing years; stays inside `yearBounds`. */
export function shiftMonth(period, delta) {
  const { min, max } = yearBounds()
  const index = period.year * 12 + (period.month - 1) + delta
  const year = Math.floor(index / 12)
  const month = (index % 12) + 1
  if (year < min || year > max) return period
  return { ...period, year, month }
}

export function shiftYear(period, delta) {
  const { min, max } = yearBounds()
  const year = period.year + delta
  if (year < min || year > max) return period
  return { ...period, year }
}

/** The §6.6 query fields of a period: `month` only with monthly, `quarter` only with quarterly. */
export function periodQuery(p) {
  return {
    periodKind: p.kind,
    year: p.year,
    month: p.kind === 'monthly' ? p.month : undefined,
    quarter: p.kind === 'quarterly' ? p.quarter : undefined,
  }
}

/** "Sentabr 2026", "2-chorak 2026", "2026-yil". */
export function periodCaption(p) {
  if (p.kind === 'monthly') return `${MONTH_NAMES[p.month - 1] || ''} ${p.year}`
  if (p.kind === 'quarterly') return `${p.quarter}-chorak ${p.year}`
  return `${p.year}-yil`
}

/** Stable identity of a period — for fetch dependencies. */
export function periodKey(p) {
  const q = periodQuery(p)
  return `${q.periodKind}:${q.year}:${q.month ?? ''}:${q.quarter ?? ''}`
}

// ---------------------------------------------------------------------------
//  Score and comment — §8.6
// ---------------------------------------------------------------------------

/** 87.5 → "87,5"; 90 → "90"; null → "". Comma, as in the other grade reports. */
export function formatScore(score) {
  if (score === null || score === undefined || Number.isNaN(Number(score))) return ''
  const rounded = Math.round(Number(score) * 100) / 100
  return String(rounded).replace('.', ',')
}

/**
 * Typed text → `{ ok: true, value }` (value null = no score) or `{ ok: false, reason }`.
 * Accepts "87,5" and "87.5". Out of range is refused, never clamped: 150 is more
 * likely a typo for 50 or 15 than a request for 100. Extra decimals round to two.
 */
export function parseScore(text) {
  const t = String(text ?? '').trim().replace(',', '.')
  if (t === '') return { ok: true, value: null }
  if (!/^\d{1,3}(\.\d+)?$/.test(t)) return { ok: false, reason: 'Ball — 0 dan 100 gacha son' }
  const n = Number(t)
  if (!Number.isFinite(n) || n < SCORE_MIN || n > SCORE_MAX) {
    return { ok: false, reason: "Ball 0 dan 100 gacha bo'lishi kerak" }
  }
  return { ok: true, value: Math.round(n * 100) / 100 }
}

/** Trimmed comment, or null when empty. */
export function normalizeComment(text) {
  const t = String(text ?? '').trim()
  return t === '' ? null : t
}

/** Why a comment would be refused, or null when it is fine. */
export function commentProblem(text) {
  const t = String(text ?? '').trim()
  if (t.length > 0 && t.length < COMMENT_MIN_LENGTH) {
    return `Izoh kamida ${COMMENT_MIN_LENGTH} ta belgidan iborat bo'lsin`
  }
  return null
}

/** `{ created, updated, deleted }` → one Uzbek sentence. */
export function saveSummary(r) {
  const parts = [
    r && r.created > 0 ? `${r.created} ta yangi` : '',
    r && r.updated > 0 ? `${r.updated} ta yangilandi` : '',
    r && r.deleted > 0 ? `${r.deleted} ta o'chirildi` : '',
  ].filter(Boolean)
  return parts.length > 0 ? `Saqlandi: ${parts.join(', ')}` : "Saqlandi — o'zgarish yo'q"
}

// ---------------------------------------------------------------------------
//  Errors — every failure as one Uzbek sentence
// ---------------------------------------------------------------------------

/** The server's own `{ message }`, when it is a real sentence (not an HTML error page). */
function serverMessage(err) {
  const m = err && err.body && typeof err.body.message === 'string' ? err.body.message.trim() : ''
  if (!m || m.startsWith('<') || m.length > 300) return null
  return m
}

/**
 * `ApiError` (lib/api.js) → Uzbek text. The server's message wins for a refused rule (it is Uzbek
 * by contract and more precise); a bare 403 (`Forbid()` has no body) gets the section's own words.
 */
export function seasonalErrorMessage(err) {
  const status = err && typeof err.status === 'number' ? err.status : -1
  const msg = serverMessage(err)
  if (msg && msg.toLowerCase().includes('chorak sozlanmagan')) {
    return 'Bunday chorak sozlanmagan — chorak sanalarini administrator kiritishi kerak.'
  }
  if (status === 0) return "Serverga ulanib bo'lmadi. Internet aloqasini tekshiring."
  if (status === 403) {
    return msg || "Mavsumiy baholash uchun ruxsatingiz yo'q yoki bu sinf va fan sizga biriktirilmagan."
  }
  if (status >= 500) return "Serverda xatolik. Birozdan so'ng qayta urinib ko'ring."
  if (msg) return msg
  if (status === 400 || status === 422) {
    return "Ma'lumotlarni tekshiring: ball 0 dan 100 gacha, izoh esa kamida 3 ta belgidan iborat bo'lsin."
  }
  if (status === 409) return "Baholar shu orada o'zgartirildi. Ro'yxatni yangilab, qayta urinib ko'ring."
  return err?.message || 'Xatolik yuz berdi'
}

/** Wraps a request so `useAsync` (which shows `error.message`) gets the Uzbek sentence. */
export function seasonalRequest(promise) {
  return promise.catch((e) => {
    throw new Error(seasonalErrorMessage(e))
  })
}
