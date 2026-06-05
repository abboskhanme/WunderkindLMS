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

export async function setTeacherAttendance(
  teacherId: string,
  date: string,
  status: string | null,
  note?: string,
): Promise<void> {
  await api.put('/admin/teacher-attendance', { teacherId, date, status, note })
}

/** Bitta kun uchun barcha o'qituvchini belgilash (status=null → o'sha kun tozalanadi). */
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
}

export interface AttendanceDashboard {
  date: string
  turnstileEnabled: boolean
  lastSync: string
  inTeachingPeriod: boolean
  summary: { total: number; present: number; late: number; absent: number; notArrived: number }
  rows: DashboardRow[]
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
