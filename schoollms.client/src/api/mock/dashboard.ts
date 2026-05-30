import type { AdminDashboard } from '@/types'

// Vaqtinchalik mock ma'lumotlar (backend tayyor bo'lguncha)
export const adminDashboardMock: AdminDashboard = {
  stats: {
    studentsCount: 1248,
    teachersCount: 86,
    averageGrade: 4.2,
    attendanceRate: 94,
  },
  classPerformance: [
    { classId: '1', className: '5-A', averageGrade: 4.5, attendanceRate: 97 },
    { classId: '2', className: '5-B', averageGrade: 4.1, attendanceRate: 92 },
    { classId: '3', className: '6-A', averageGrade: 3.9, attendanceRate: 89 },
    { classId: '4', className: '7-A', averageGrade: 4.3, attendanceRate: 95 },
    { classId: '5', className: '8-B', averageGrade: 4.0, attendanceRate: 91 },
    { classId: '6', className: '9-A', averageGrade: 4.6, attendanceRate: 98 },
    { classId: '7', className: '10-A', averageGrade: 3.8, attendanceRate: 88 },
    { classId: '8', className: '11-A', averageGrade: 4.4, attendanceRate: 96 },
  ],
  topClasses: [
    { id: '6', name: '9-A', studentsCount: 28, averageGrade: 4.6 },
    { id: '1', name: '5-A', studentsCount: 30, averageGrade: 4.5 },
    { id: '8', name: '11-A', studentsCount: 26, averageGrade: 4.4 },
    { id: '4', name: '7-A', studentsCount: 29, averageGrade: 4.3 },
    { id: '2', name: '5-B', studentsCount: 31, averageGrade: 4.1 },
    { id: '5', name: '8-B', studentsCount: 27, averageGrade: 4.0 },
  ],
}
