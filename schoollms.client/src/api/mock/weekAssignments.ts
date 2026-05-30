import type { WeekAssignment } from '@/types'

// Barcha haftalarga bitta jadval biriktirish (12 hafta — ortig'i e'tiborga olinmaydi)
const allWeeks = (templateId: string): WeekAssignment[] =>
  Array.from({ length: 12 }, (_, i) => ({ week: i + 1, templateId }))

// `${classId}-${quarter}` -> haftalarga biriktirishlar
// Namuna: 9-A (c6) — barcha 4 chorak "Asosiy jadval" (tpl-1) ga biriktirilgan.
export const weekAssignmentsMock: Record<string, WeekAssignment[]> = {
  'c6-1': allWeeks('tpl-1'),
  'c6-2': allWeeks('tpl-1'),
  'c6-3': allWeeks('tpl-1'),
  'c6-4': allWeeks('tpl-1'),
}
