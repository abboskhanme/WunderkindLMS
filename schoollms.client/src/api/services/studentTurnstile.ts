import { api } from '../client'

export interface StudentTurnstileRow {
  studentId: string
  fullName: string
  className: string
  /** Turniket qurilma ID (employeeNo) — bo'sh bo'lsa hali biriktirilmagan */
  deviceUserId: string
  /** "HH:mm" — birinchi o'tish (kirgan) */
  checkIn: string
  /** "HH:mm" — oxirgi o'tish (chiqqan) */
  checkOut: string
  /** O'sha kungi o'tishlar soni */
  passes: number
}

export interface StudentTurnstileDashboard {
  /** "yyyy-MM-dd" */
  date: string
  turnstileEnabled: boolean
  lastSync: string
  present: number
  total: number
  rows: StudentTurnstileRow[]
}

export interface SyncResult {
  ok: boolean
  message: string
  eventsFetched: number
  updated: number
  lastSync: string
}

export async function getStudentTurnstile(date: string): Promise<StudentTurnstileDashboard> {
  const { data } = await api.get<StudentTurnstileDashboard>('/admin/students/turnstile/dashboard', {
    params: { date },
  })
  return data
}

export async function syncStudentTurnstile(): Promise<SyncResult> {
  const { data } = await api.post<SyncResult>('/admin/students/turnstile/sync')
  return data
}

export async function setStudentDevice(studentId: string, deviceUserId: string): Promise<void> {
  await api.put('/admin/students/turnstile/device', { studentId, deviceUserId })
}
