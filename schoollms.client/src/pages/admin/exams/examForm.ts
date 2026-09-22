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
  questionCount: string
}

export function newSectionDraft(patch: Partial<SectionDraft> = {}): SectionDraft {
  return {
    key: crypto.randomUUID(),
    subjectId: '',
    maxScore: '',
    bankId: '',
    questionCount: '',
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
