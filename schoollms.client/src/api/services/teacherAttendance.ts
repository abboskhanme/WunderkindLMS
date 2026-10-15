import { api } from '../client'

export interface TeacherAttendanceEntry {
  teacherId: string
  /** "yyyy-MM-dd" */
  date: string
  /** "present" | "absent" | "late" */
  status: string
  note: string
}

export interface TeacherAttendanceBoard {
  teachers: { id: string; fullName: string; startDate: string }[]
  entries: TeacherAttendanceEntry[]
  /** Chorak (dars jadvali) davrlari — davomat faqat shu kunlarda belgilanadi */
  quarters: { start: string; end: string }[]
}

export async function getTeacherAttendance(month: string): Promise<TeacherAttendanceBoard> {
  const { data } = await api.get<TeacherAttendanceBoard>('/admin/teacher-attendance', {
    params: { month },
  })
  return data
}

/** One calendar day of the range (Sundays included). */
export interface TeacherAttendanceDay {
  /** "yyyy-MM-dd" */
  date: string
  isSunday: boolean
  isHoliday: boolean
  holidayName: string
  /** Inside some quarter */
  inQuarter: boolean
  /** How many listed teachers have lessons that day */
  teachers: number
}

/** Lessons a teacher teaches on a date (timetable + substitutions). 0 = all of them went to a substitute. */
export interface TeacherLessonCount {
  teacherId: string
  date: string
  lessons: number
}

/** Board for a date range — only teachers with lessons (or an entry) in it. */
export interface TeacherAttendanceRangeBoard extends TeacherAttendanceBoard {
  from: string
  to: string
  days: TeacherAttendanceDay[]
  lessons: TeacherLessonCount[]
}

/** Board for a range "yyyy-MM-dd" … "yyyy-MM-dd" (at most 93 days). */
export async function getTeacherAttendanceRange(from: string, to: string): Promise<TeacherAttendanceRangeBoard> {
  const { data } = await api.get<TeacherAttendanceRangeBoard>('/admin/teacher-attendance', {
    params: { from, to },
  })
  return data
}

export async function setTeacherAttendance(
  teacherId: string,
  date: string,
  status: string | null,
  note?: string,
): Promise<void> {
  await api.put('/admin/teacher-attendance', { teacherId, date, status, note })
}

/** Marks one day for the teachers who teach that day (status=null → clears their marks). */
export async function setTeacherAttendanceDay(date: string, status: string | null): Promise<void> {
  await api.put('/admin/teacher-attendance/day', { date, status })
}

/* ---------- Dashboard (turniket/FaceID avtomatik) ---------- */

export interface DashboardRow {
  teacherId: string
  fullName: string
  photoUrl?: string | null
  deviceUserId: string
  /** "present" | "late" | "absent" | "" (kelmagan/kutilmoqda) */
  status: string
  /** "HH:mm" */
  checkIn: string
  checkOut: string
  /** Kutilgan kelish vaqti "HH:mm" */
  expected: string
  lateMinutes: number
  /** "manual" | "turnstile" | "" */
  source: string
  /** false — no lessons that day; listed only because an entry exists */
  hasLessons: boolean
  /** Lessons that day */
  lessons: number
}

export interface AttendanceDashboard {
  date: string
  turnstileEnabled: boolean
  lastSync: string
  inTeachingPeriod: boolean
  summary: { total: number; present: number; late: number; absent: number; notArrived: number }
  rows: DashboardRow[]
  /** Active teachers with no lessons that day (not listed) */
  noLessonTeachers: number
}

export interface SyncResult {
  ok: boolean
  message: string
  eventsFetched: number
  updated: number
  lastSync: string
}

export async function getAttendanceDashboard(date: string): Promise<AttendanceDashboard> {
  const { data } = await api.get<AttendanceDashboard>('/admin/teacher-attendance/dashboard', {
    params: { date },
  })
  return data
}

export async function syncTurnstile(): Promise<SyncResult> {
  const { data } = await api.post<SyncResult>('/admin/teacher-attendance/sync')
  return data
}
