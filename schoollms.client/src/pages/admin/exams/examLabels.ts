/**
 * Uzbek labels and formatting for the Imtihonlar screens (unit C3).
 *
 * The service carries the contract, the page layer carries the words — the
 * `marketing/submissionLabels.ts` split. The register, the form, the entry
 * grid and the result drawer all read from here, so a status is never spelled
 * two ways on two screens of the same module.
 */
import type {
  BankState,
  ExamKind,
  ExamStatus,
  FinishReason,
  ParticipantStatus,
} from '@/api/services/exams'

/** One dash everywhere a value is missing. */
export const DASH = '—'

/** Shared filter-bar control, the same one the marketing registers use. */
export const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/** Page size of every paged list here; §6 caps `limit` at 200. */
export const PAGE_SIZE = 50

/** A keystroke must not become a request — the lists are server-side queries. */
export const SEARCH_DEBOUNCE_MS = 350

/** §13 Q7 — publish is refused above this many questions per sitting. */
export const MAX_QUESTIONS_PER_EXAM = 200

/**
 * Onlayn test (ochiq havola, qurilma qulfi, urinishni ko'rib chiqish — B3) hali
 * YO'Q: 2026-09-22 da keyinga qoldirildi. Hozircha har bir imtihon qog'ozda
 * o'tadi va natija qo'lda kiritiladi. Onlayn backend qo'shilgach — `true`.
 */
export const ONLINE_EXAMS_ENABLED = false

// ---- exam ----

export const kindLabels: Record<ExamKind, string> = {
  block: 'Blok test',
  admission: 'Qabul imtihoni',
}

export const kindTones: Record<ExamKind, string> = {
  block: 'bg-slate-100 text-slate-600',
  admission: 'bg-brand-50 text-brand-700',
}

export const examStatusLabels: Record<ExamStatus, string> = {
  draft: 'Qoralama',
  published: "E'lon qilingan",
  closed: 'Yopilgan',
  cancelled: 'Bekor qilingan',
}

const examStatusTones: Record<ExamStatus, string> = {
  draft: 'bg-slate-100 text-slate-600',
  published: 'bg-emerald-50 text-emerald-700',
  closed: 'bg-brand-50 text-brand-700',
  cancelled: 'bg-red-50 text-red-600',
}

/** An unknown value from the server still renders as something readable. */
export function examStatusLabel(status: ExamStatus): string {
  return examStatusLabels[status] ?? status
}

export function examStatusTone(status: ExamStatus): string {
  return examStatusTones[status] ?? 'bg-slate-100 text-slate-600'
}

export function kindLabel(kind: ExamKind): string {
  return kindLabels[kind] ?? kind
}

export function kindTone(kind: ExamKind): string {
  return kindTones[kind] ?? 'bg-slate-100 text-slate-600'
}

// ---- participant ----

export const participantStatusLabels: Record<ParticipantStatus, string> = {
  assigned: 'Kutilmoqda',
  in_progress: 'Topshirmoqda',
  finished: 'Baholangan',
  absent: 'Kelmadi',
  cancelled: 'Bekor qilingan',
}

const participantStatusTones: Record<ParticipantStatus, string> = {
  assigned: 'bg-slate-100 text-slate-600',
  in_progress: 'bg-amber-50 text-amber-700',
  finished: 'bg-emerald-50 text-emerald-700',
  absent: 'bg-red-50 text-red-600',
  cancelled: 'bg-slate-100 text-slate-400',
}

export function participantStatusLabel(status: ParticipantStatus): string {
  return participantStatusLabels[status] ?? status
}

export function participantStatusTone(status: ParticipantStatus): string {
  return participantStatusTones[status] ?? 'bg-slate-100 text-slate-600'
}

export const finishReasonLabels: Record<FinishReason, string> = {
  manual: "Nomzodning o'zi yakunladi",
  timer: 'Vaqt tugadi',
  admin: 'Xodim yakunladi',
}

// ---- bank ----

export const bankStateLabels: Record<BankState, string> = {
  ready: 'tayyor',
  notEnough: 'savol yetarli emas',
  unconfigured: 'sozlanmagan',
}

// ---- formatting ----

/**
 * `0` is nol sinf (preparatory), not "unknown" — printing "0-sinf" would read
 * as a missing value. Same rule as the submissions register.
 */
export function gradeLabel(grade: number | null | undefined): string {
  if (grade === null || grade === undefined) return DASH
  return grade === 0 ? 'Nol sinf' : `${grade}-sinf`
}

/** Grades 0–11 in picker order. */
export const GRADES: readonly number[] = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]

/** `YYYY-MM-DD` → `DD.MM.YYYY` without going through `Date` (no day shift). */
export function formatDay(iso: string | null | undefined): string {
  if (!iso) return DASH
  const [y, m, d] = iso.slice(0, 10).split('-')
  return y && m && d ? `${d}.${m}.${y}` : iso
}

/**
 * `yyyy-MM-ddTHH:mm:ss` (Tashkent wall clock, no offset — §6) →
 * `DD.MM.YYYY HH:mm`. Read as text, so the browser's own zone never shifts it.
 */
export function formatWallClock(value: string | null | undefined): string {
  if (!value) return DASH
  const day = formatDay(value)
  const time = value.slice(11, 16)
  return time.length === 5 ? `${day} ${time}` : day
}

/** Points as the server sent them: `42`, `42.5`, `42.25` — never `42.50`. */
export function formatPoints(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return DASH
  return Number.isInteger(value) ? String(value) : String(Number(value.toFixed(2)))
}

/** `total / max` with a dash when either side is missing. */
export function formatScore(total: number | null | undefined, max: number | null | undefined): string {
  if (total === null || total === undefined) return DASH
  return max === null || max === undefined
    ? formatPoints(total)
    : `${formatPoints(total)} / ${formatPoints(max)}`
}

export function formatPercent(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return DASH
  return `${formatPoints(value)}%`
}

/** Admin/superadmin carry no `permissions` list at all — they see everything. */
export function hasPerm(permissions: string[] | undefined, perm: string): boolean {
  return !permissions || permissions.includes(perm)
}
