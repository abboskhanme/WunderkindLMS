/**
 * Dropdown options derived from `ScopeDto` (§6.6). These are lists of names,
 * not report numbers — every count on these screens comes from the server.
 */
import type { ScopeClassDto, ScopeDto } from '@/api/services/seasonalMarks'

export interface NamedOption {
  id: string
  name: string
}

const byName = (a: NamedOption, b: NamedOption) =>
  a.name.localeCompare(b.name, 'uz', { numeric: true, sensitivity: 'base' })

/** Classes ordered by grade, then by name ("5-A", "5-B", "10-A"). */
export function classOptions(scope: ScopeDto): ScopeClassDto[] {
  return [...scope.classes].sort(
    (a, b) => a.grade - b.grade || a.name.localeCompare(b.name, 'uz', { numeric: true }),
  )
}

/** Distinct subjects taught in the given classes; `null` = in any class. */
export function subjectOptions(scope: ScopeDto, classIds: readonly string[] | null): NamedOption[] {
  const wanted = classIds === null ? null : new Set(classIds)
  const seen = new Map<string, NamedOption>()
  for (const p of scope.pairs) {
    if (wanted && !wanted.has(p.classId)) continue
    if (!seen.has(p.subjectId)) seen.set(p.subjectId, { id: p.subjectId, name: p.subjectName })
  }
  return [...seen.values()].sort(byName)
}

/** Distinct teachers of the schedule. */
export function teacherOptions(scope: ScopeDto): NamedOption[] {
  const seen = new Map<string, NamedOption>()
  for (const p of scope.pairs) {
    if (!seen.has(p.teacherId)) seen.set(p.teacherId, { id: p.teacherId, name: p.teacherFullName })
  }
  return [...seen.values()].sort(byName)
}

/** Who teaches this subject in this class — shown next to the subject choice. */
export function teachersOfPair(scope: ScopeDto, classId: string, subjectId: string): string[] {
  const names = scope.pairs
    .filter((p) => p.classId === classId && p.subjectId === subjectId)
    .map((p) => p.teacherFullName)
  return [...new Set(names)]
}
