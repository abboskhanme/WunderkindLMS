/**
 * Pure helpers of the exam form (`ExamFormModal`, `BankSectionsEditor`) and
 * the entry grid. Input hygiene only — nothing here scores anything.
 */

/** One section row while the form is open. Strings, because they are inputs. */
export interface SectionDraft {
  /** Stable React key; not sent. */
  key: string
  subjectId: string
  /** Manual exams: the entry-grid ceiling. */
  maxScore: string
  /** Online exams. */
  bankId: string
  /** Online: drawn from the bank. Manual: the DTM shape, with `pointsPerCorrect`. */
  questionCount: string
  /** Manual only — weight of one correct answer (30 × 3.1); blank = a plain ceiling. */
  pointsPerCorrect: string
}

export function newSectionDraft(patch: Partial<SectionDraft> = {}): SectionDraft {
  return {
    key: crypto.randomUUID(),
    subjectId: '',
    maxScore: '',
    bankId: '',
    questionCount: '',
    pointsPerCorrect: '',
    ...patch,
  }
}

/** `numeric(6,2)` / `numeric(8,2)`: at most two decimals. A comma is accepted. */
const DECIMAL = /^\d+(\.\d{1,2})?$/

/**
 * `"42,5"` → `42.5`; blank → `null`; anything else → `NaN`.
 * The caller decides whether `null` is allowed.
 */
export function parseDecimal(raw: string): number | null {
  const text = raw.trim().replace(',', '.')
  if (!text) return null
  return DECIMAL.test(text) ? Number(text) : Number.NaN
}

/** A positive whole number, or `null` when blank, or `NaN`. */
export function parsePositiveInt(raw: string): number | null {
  const text = raw.trim()
  if (!text) return null
  return /^\d+$/.test(text) && Number(text) > 0 ? Number(text) : Number.NaN
}

/** Largest value a `numeric(6,2)` section ceiling holds. */
export const MAX_SECTION_SCORE = 9999.99

/** Move one item of a list up or down; out-of-range moves return the list unchanged. */
export function move<T>(list: T[], index: number, delta: -1 | 1): T[] {
  const target = index + delta
  if (target < 0 || target >= list.length) return list
  const next = [...list]
  const [item] = next.splice(index, 1)
  next.splice(target, 0, item)
  return next
}

/**
 * A manual section's DTM shape (client, 2026-10-31: "1-blok 30 × 3,1"): both the question
 * count and the weight typed → `{ count, weight, max }`; neither → `null` (a plain ceiling);
 * only one, or a bad value → `'bad'`.
 */
export function weightedSection(s: SectionDraft): { count: number; weight: number; max: number } | null | 'bad' {
  if (!s.questionCount.trim() && !s.pointsPerCorrect.trim()) return null
  const count = parsePositiveInt(s.questionCount)
  const weight = parseDecimal(s.pointsPerCorrect)
  if (count === null || Number.isNaN(count) || weight === null || Number.isNaN(weight) || weight <= 0) return 'bad'
  return { count, weight, max: Math.round(count * weight * 100) / 100 }
}

/**
 * DTM block test (client, 2026-10-31): blocks 1–2 are the track's own subjects (30 × 3.1 and
 * 30 × 2.1), blocks 3–5 are the same for every track (10 × 1.1 each) — 189 in total.
 */
export const DTM_BLOCKS: { count: number; weight: number; match: RegExp | null }[] = [
  { count: 30, weight: 3.1, match: null },
  { count: 30, weight: 2.1, match: null },
  { count: 10, weight: 1.1, match: /ona\s*tili/i },
  { count: 10, weight: 1.1, match: /matem/i },
  { count: 10, weight: 1.1, match: /tarix/i },
]
