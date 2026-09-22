/**
 * Period and score helpers shared by every Mavsumiy baholash screen
 * (`docs/modules/admission-and-testing.md` §5.12, §8.6, §13 Q5).
 *
 * No component lives here, so the React Refresh boundary of the `.tsx` files
 * stays clean.
 */
import type { PeriodKind, PeriodQuery } from '@/api/services/seasonalMarks'

/**
 * §13 Q5: every screen labels the score column exactly this way, so nobody
 * reads a 4 as "yaxshi" — this is the 0–100 scale, not the 1–5 journal.
 */
export const SCORE_COLUMN_LABEL = 'Ball (0–100)'

export const SCORE_MIN = 0
export const SCORE_MAX = 100
/** §8.6: a comment is either absent or at least three characters after trim. */
export const COMMENT_MIN_LENGTH = 3

/** The select / input look of every filter bar on these screens (as in the journal). */
export const controlClass =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none focus:border-brand-400 disabled:cursor-not-allowed disabled:bg-slate-50 disabled:text-slate-400'

/** The permission key of the whole module, admin and teacher alike (§4). */
export const SEASONAL_PERM = 'seasonalMarks'

export const PERIOD_KINDS: readonly PeriodKind[] = ['monthly', 'quarterly', 'yearly']

export const periodKindLabels: Record<PeriodKind, string> = {
  monthly: 'Oylik',
  quarterly: 'Choraklik',
  yearly: 'Yillik',
}

export const MONTH_NAMES: readonly string[] = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

export const QUARTERS: readonly number[] = [1, 2, 3, 4]

/** A fully chosen period. `month` and `quarter` are kept even when unused. */
export interface PeriodSelection {
  kind: PeriodKind
  year: number
  /** 1–12 */
  month: number
  /** 1–4 */
  quarter: number
}

/**
 * The school quarter a calendar month usually falls in — only a starting
 * value for the picker; the user sees and can change it. The real dates live
 * in Sozlamalar → Choraklar, which a teacher account cannot read.
 */
export function guessQuarter(month: number): number {
  if (month === 9 || month === 10) return 1
  if (month === 11 || month === 12) return 2
  if (month >= 1 && month <= 3) return 3
  return 4
}

export function defaultPeriod(now: Date = new Date()): PeriodSelection {
  const month = now.getMonth() + 1
  return { kind: 'monthly', year: now.getFullYear(), month, quarter: guessQuarter(month) }
}

export function toPeriodQuery(p: PeriodSelection): PeriodQuery {
  return {
    periodKind: p.kind,
    year: p.year,
    month: p.kind === 'monthly' ? p.month : undefined,
    quarter: p.kind === 'quarterly' ? p.quarter : undefined,
  }
}

/** A caption for the user's own selection, e.g. "Mart 2026", "2-chorak 2026". */
export function periodCaption(p: PeriodSelection): string {
  if (p.kind === 'monthly') return `${MONTH_NAMES[p.month - 1] ?? ''} ${p.year}`
  if (p.kind === 'quarterly') return `${p.quarter}-chorak ${p.year}`
  return `${p.year}-yil`
}

/** Stable string identity of a period — for request keys. */
export function periodKey(p: PeriodSelection): string {
  const q = toPeriodQuery(p)
  return `${q.periodKind}:${q.year}:${q.month ?? ''}:${q.quarter ?? ''}`
}

/** Newest first: next year down to five years back (§5.12 allows 2000–2100). */
export function yearOptions(selected?: number, now: Date = new Date()): number[] {
  const y = now.getFullYear()
  const years = Array.from({ length: 7 }, (_, i) => y + 1 - i)
  if (selected !== undefined && !years.includes(selected)) years.push(selected)
  return years.sort((a, b) => b - a)
}

export function isPeriodKind(value: string | null): value is PeriodKind {
  return value === 'monthly' || value === 'quarterly' || value === 'yearly'
}

/** An integer in [min, max] from a query-string value, or undefined. */
export function intParam(value: string | null, min: number, max: number): number | undefined {
  if (value === null || value.trim() === '') return undefined
  const n = Number(value)
  return Number.isInteger(n) && n >= min && n <= max ? n : undefined
}

// ---------------------------------------------------------------------------
//  Score and comment
// ---------------------------------------------------------------------------

/** 87.5 → "87,5"; 90 → "90"; null → "". Comma, as in the other grade reports. */
export function formatScore(score: number | null | undefined): string {
  if (score === null || score === undefined || Number.isNaN(score)) return ''
  const rounded = Math.round(score * 100) / 100
  return String(rounded).replace('.', ',')
}

export type ScoreParse = { ok: true; value: number | null } | { ok: false; reason: string }

/**
 * Typed text → score. Empty is `null` (no score). Accepts "87,5" and "87.5".
 * Out-of-range values are refused, not silently clamped: 150 is more likely a
 * typo for 50 or 15 than a request for 100. Extra decimals are rounded to two.
 */
export function parseScore(text: string): ScoreParse {
  const t = text.trim().replace(',', '.')
  if (t === '') return { ok: true, value: null }
  if (!/^\d{1,3}(\.\d+)?$/.test(t)) {
    return { ok: false, reason: 'Ball — 0 dan 100 gacha son' }
  }
  const n = Number(t)
  if (!Number.isFinite(n) || n < SCORE_MIN || n > SCORE_MAX) {
    return { ok: false, reason: "Ball 0 dan 100 gacha bo'lishi kerak" }
  }
  return { ok: true, value: Math.round(n * 100) / 100 }
}

/** Trimmed comment, or null when empty. */
export function normalizeComment(text: string | null | undefined): string | null {
  const t = (text ?? '').trim()
  return t === '' ? null : t
}

/** Why a comment would be refused (§8.6), or null when it is fine. */
export function commentProblem(text: string): string | null {
  const t = text.trim()
  if (t.length > 0 && t.length < COMMENT_MIN_LENGTH) {
    return `Izoh kamida ${COMMENT_MIN_LENGTH} ta belgidan iborat bo'lsin`
  }
  return null
}

/** "yyyy-MM-ddTHH:mm:ss" → "21.05.2026 09:57". */
export function formatStamp(ts: string): string {
  const [d, t] = ts.split('T')
  const [y, m, day] = (d ?? '').split('-')
  if (!y || !m || !day) return ts
  return `${day}.${m}.${y}${t ? ` ${t.slice(0, 5)}` : ''}`
}

// ---------------------------------------------------------------------------
//  Query-string hand-over between the screens
// ---------------------------------------------------------------------------

/** Period fields read from `?periodKind=&year=&month=&quarter=`; invalid ones are dropped. */
export interface PeriodParams {
  kind?: PeriodKind
  year?: number
  month?: number
  quarter?: number
}

export function readPeriodParams(params: URLSearchParams): PeriodParams {
  const kind = params.get('periodKind')
  return {
    kind: isPeriodKind(kind) ? kind : undefined,
    year: intParam(params.get('year'), 2000, 2100),
    month: intParam(params.get('month'), 1, 12),
    quarter: intParam(params.get('quarter'), 1, 4),
  }
}

/** A complete period from the query string, the gaps filled from today. */
export function periodFromParams(params: URLSearchParams, now: Date = new Date()): PeriodSelection {
  const read = readPeriodParams(params)
  const base = defaultPeriod(now)
  const month = read.month ?? base.month
  return {
    kind: read.kind ?? base.kind,
    year: read.year ?? base.year,
    month,
    quarter: read.quarter ?? (read.month ? guessQuarter(month) : base.quarter),
  }
}

/** `periodKind=…&year=…[&month|&quarter]` for a link to another screen. */
export function periodSearch(p: PeriodSelection): URLSearchParams {
  const q = toPeriodQuery(p)
  const out = new URLSearchParams({ periodKind: q.periodKind, year: String(q.year) })
  if (q.month !== undefined) out.set('month', String(q.month))
  if (q.quarter !== undefined) out.set('quarter', String(q.quarter))
  return out
}
