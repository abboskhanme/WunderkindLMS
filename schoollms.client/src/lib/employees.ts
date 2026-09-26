/**
 * One list of employees (employees-unified.md, client 2026-09-26): teachers (`teachers` table)
 * and other staff (`users`, role=staff) are shown together and told apart only by "Lavozim".
 * The same position filter is used on Boshqaruv → Xodimlar, Moliya → Ish haqi and the
 * finance salary tab.
 */
import type { EmployeeKind } from '@/types'

/** Position label every teacher row carries. */
export const TEACHER_POSITION = "O'qituvchi"

/**
 * Position filter value:
 *  - `''`            — everyone;
 *  - `'teacher'`     — teachers only (also the `?position=teacher` URL value);
 *  - `'p:<position>'` — staff with exactly that position.
 */
export const TEACHER_FILTER = 'teacher'
const STAFF_PREFIX = 'p:'

export interface Positioned {
  kind: EmployeeKind
  position: string
}

export interface PositionOptions {
  hasTeachers: boolean
  /** Distinct non-empty staff positions, sorted. */
  staffPositions: string[]
}

export const staffPositionFilter = (position: string) => `${STAFF_PREFIX}${position}`

/** `?position=` URL value → filter value (`teacher` or a staff position). */
export function positionFilterFromParam(param: string | null): string {
  const v = (param ?? '').trim()
  if (!v) return ''
  return v === TEACHER_FILTER ? TEACHER_FILTER : staffPositionFilter(v)
}

/** Label of a filter value: `''` → '', `'teacher'` → "O'qituvchi", `'p:Kassir'` → 'Kassir'. */
export function positionFilterLabel(filter: string): string {
  if (filter === TEACHER_FILTER) return TEACHER_POSITION
  return filter.startsWith(STAFF_PREFIX) ? filter.slice(STAFF_PREFIX.length) : filter
}

export function matchesPosition(row: Positioned, filter: string): boolean {
  if (!filter) return true
  if (filter === TEACHER_FILTER) return row.kind === 'teacher'
  if (filter.startsWith(STAFF_PREFIX)) return row.kind === 'staff' && row.position === filter.slice(STAFF_PREFIX.length)
  return true
}

export function positionOptions(rows: readonly Positioned[]): PositionOptions {
  const staff = new Set<string>()
  let hasTeachers = false
  for (const r of rows) {
    if (r.kind === 'teacher') hasTeachers = true
    else if (r.position.trim()) staff.add(r.position.trim())
  }
  return { hasTeachers, staffPositions: [...staff].sort((a, b) => a.localeCompare(b, 'uz')) }
}
