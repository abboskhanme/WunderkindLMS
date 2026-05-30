import type { SchoolSettings } from '@/types'

export const settingsMock: SchoolSettings = {
  quarters: [
    { quarter: 1, startDate: '2025-09-02', endDate: '2025-10-26', gradesOpen: true },
    { quarter: 2, startDate: '2025-11-03', endDate: '2025-12-28', gradesOpen: true },
    { quarter: 3, startDate: '2026-01-12', endDate: '2026-03-22', gradesOpen: true },
    { quarter: 4, startDate: '2026-04-01', endDate: '2026-05-25', gradesOpen: false },
  ],
  lessonTimes: [
    { period: 1, startTime: '08:30', endTime: '09:15' },
    { period: 2, startTime: '09:25', endTime: '10:10' },
    { period: 3, startTime: '10:25', endTime: '11:10' },
    { period: 4, startTime: '11:20', endTime: '12:05' },
    { period: 5, startTime: '12:15', endTime: '13:00' },
    { period: 6, startTime: '13:10', endTime: '13:55' },
  ],
  absenceReasons: [
    { id: 'ar1', name: 'Kasal', short: 'K', isLate: false },
    { id: 'ar2', name: 'Sababli', short: 'S', isLate: false },
    { id: 'ar3', name: 'Sababsiz', short: 'SS', isLate: false },
    { id: 'ar4', name: 'Kech keldi', short: 'Kch', isLate: true },
  ],
}
