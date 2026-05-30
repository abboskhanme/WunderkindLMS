import type { ScheduleTemplate } from '@/types'

// classId -> jadval variantlari.
// Namuna uchun 9-A (c6) to'liq haftalik jadval bilan (API ulanguncha).
export const templatesMock: Record<string, ScheduleTemplate[]> = {
  c6: [
    {
      id: 'tpl-1',
      classId: 'c6',
      name: 'Asosiy jadval',
      lessons: [
        // Dushanba
        { day: 0, period: 1, subjectId: 'sb1', teacherId: 't2' },
        { day: 0, period: 2, subjectId: 'sb3', teacherId: 't1' },
        { day: 0, period: 3, subjectId: 'sb4', teacherId: 't1' },
        { day: 0, period: 4, subjectId: 'sb2', teacherId: 't2' },
        { day: 0, period: 5, subjectId: 'sb5', teacherId: 't7' },
        { day: 0, period: 6, subjectId: 'sb7', teacherId: 't4' },
        // Seshanba
        { day: 1, period: 1, subjectId: 'sb1', teacherId: 't2' },
        { day: 1, period: 2, subjectId: 'sb6', teacherId: 't4' },
        { day: 1, period: 3, subjectId: 'sb8', teacherId: 't6' },
        { day: 1, period: 4, subjectId: 'sb3', teacherId: 't1' },
        { day: 1, period: 5, subjectId: 'sb4', teacherId: 't3' },
        { day: 1, period: 6, subjectId: 'sb2', teacherId: 't8' },
        // Chorshanba
        { day: 2, period: 1, subjectId: 'sb1', teacherId: 't2' },
        { day: 2, period: 2, subjectId: 'sb8', teacherId: 't6' },
        { day: 2, period: 3, subjectId: 'sb5', teacherId: 't7' },
        { day: 2, period: 4, subjectId: 'sb7', teacherId: 't4' },
        { day: 2, period: 5, subjectId: 'sb3', teacherId: 't1' },
        { day: 2, period: 6, subjectId: 'sb6', teacherId: 't4' },
        // Payshanba
        { day: 3, period: 1, subjectId: 'sb2', teacherId: 't2' },
        { day: 3, period: 2, subjectId: 'sb1', teacherId: 't6' },
        { day: 3, period: 3, subjectId: 'sb4', teacherId: 't1' },
        { day: 3, period: 4, subjectId: 'sb3', teacherId: 't5' },
        { day: 3, period: 5, subjectId: 'sb8', teacherId: 't8' },
        { day: 3, period: 6, subjectId: 'sb5', teacherId: 't7' },
        // Juma
        { day: 4, period: 1, subjectId: 'sb1', teacherId: 't2' },
        { day: 4, period: 2, subjectId: 'sb7', teacherId: 't4' },
        { day: 4, period: 3, subjectId: 'sb6', teacherId: 't4' },
        { day: 4, period: 4, subjectId: 'sb2', teacherId: 't8' },
        { day: 4, period: 5, subjectId: 'sb4', teacherId: 't3' },
        { day: 4, period: 6, subjectId: 'sb3', teacherId: 't1' },
        // Shanba
        { day: 5, period: 1, subjectId: 'sb3', teacherId: 't1' },
        { day: 5, period: 2, subjectId: 'sb1', teacherId: 't2' },
        { day: 5, period: 3, subjectId: 'sb8', teacherId: 't6' },
        { day: 5, period: 4, subjectId: 'sb5', teacherId: 't7' },
        { day: 5, period: 5, subjectId: 'sb4', teacherId: 't1' },
      ],
    },
  ],
}
