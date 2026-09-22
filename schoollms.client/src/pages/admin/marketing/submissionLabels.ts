/**
 * Uzbek labels and formatting for the submissions register (SM-9).
 *
 * They live beside the screens rather than in `api/services/surveySubmissions.ts`
 * for the same reason `pages/admin/finance/reportLabels.ts` exists: the service
 * carries the contract, the page layer carries the words. Both the register and
 * the detail drawer read from here, so a grade or a status can never be spelled
 * two different ways on two halves of the same screen.
 */
import type { SubmissionGender, SubmissionStatus } from '@/api/services/surveySubmissions'

/** Shown when a value was never collected — one dash everywhere, not "—"/"-"/"". */
export const DASH = '—'

/**
 * `timestamptz` → "DD.MM.YYYY HH:mm", in the reader's own timezone.
 * The same shape `ParentsPage`/`reportLabels.ts` print.
 */
export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return DASH
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/**
 * §2.7 — `lead` means a lead was created, `duplicate` means the same parent and
 * the same child arrived twice inside 24 hours and the existing lead was kept
 * (§2.6 step 4). The register must say which, or the counts look wrong.
 */
export const statusLabels: Record<SubmissionStatus, string> = {
  lead: 'Lid yaratildi',
  duplicate: 'Takror',
}

export const statusTones: Record<SubmissionStatus, string> = {
  lead: 'bg-emerald-50 text-emerald-700',
  duplicate: 'bg-amber-50 text-amber-700',
}

/** An unknown status from the server still has to render as something readable. */
export function statusLabel(status: SubmissionStatus): string {
  return statusLabels[status] ?? status
}

export function statusTone(status: SubmissionStatus): string {
  return statusTones[status] ?? 'bg-slate-100 text-slate-600'
}

export const genderLabels: Record<SubmissionGender, string> = {
  male: "O'g'il bola",
  female: 'Qiz bola',
}

export function genderLabel(gender: SubmissionGender | null): string {
  if (!gender) return DASH
  return genderLabels[gender] ?? gender
}

/**
 * `0` is nol sinf (preparatory), not "unknown" (§2.1) — printing "0-sinf"
 * would read as a missing value.
 */
export function gradeLabel(grade: number | null): string {
  if (grade === null || grade === undefined) return DASH
  return grade === 0 ? 'Nol sinf' : `${grade}-sinf`
}

/** Empty strings and nulls print as the dash, never as a blank cell. */
export function orDash(value: string | null | undefined): string {
  return value && value.trim() ? value : DASH
}
