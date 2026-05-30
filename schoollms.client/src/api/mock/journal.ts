import type { JournalEntry } from '@/types'

// `${classId}-${subjectId}-${quarter}` -> baholar/davomat
// Namuna: 9-A (c6), 4-chorak, bugun (2026-05-20, chorshanba) bir nechta yo'qlama.
export const journalMock: Record<string, JournalEntry[]> = {
  'c6-sb1-4': [{ studentId: 's1', date: '2026-05-20', period: 1, reasonId: 'ar1' }],
  'c6-sb5-4': [{ studentId: 's2', date: '2026-05-20', period: 1, reasonId: 'ar3' }],
  'c6-sb8-4': [{ studentId: 's9', date: '2026-05-20', period: 1, reasonId: 'ar2' }],
}
