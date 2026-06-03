import type { DisciplineReason, DisciplineScoreRow, DisciplinePoint } from '@/types'
import { api, USE_MOCK } from '../client'

/* ---------- Ball sabablar ---------- */

export async function getDisciplineReasons(): Promise<DisciplineReason[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<DisciplineReason[]>('/admin/discipline/reasons')
  return data
}

export async function createDisciplineReason(name: string, points: number): Promise<DisciplineReason> {
  const { data } = await api.post<DisciplineReason>('/admin/discipline/reasons', { name, points })
  return data
}

export async function updateDisciplineReason(
  id: string,
  name: string,
  points: number,
): Promise<DisciplineReason> {
  const { data } = await api.put<DisciplineReason>(`/admin/discipline/reasons/${id}`, { name, points })
  return data
}

export async function deleteDisciplineReason(id: string): Promise<void> {
  await api.delete(`/admin/discipline/reasons/${id}`)
}

/** Davomat sababiga ball belgilash (jurnalda shu sabab bilan davomat qoldiga ta'sir qiladi). */
export async function setAttendanceReasonPoints(id: string, points: number): Promise<DisciplineReason> {
  const { data } = await api.put<DisciplineReason>(`/admin/discipline/reasons/attendance/${id}`, { points })
  return data
}

/* ---------- Ballar nazorati ---------- */

export async function getDisciplineScores(): Promise<DisciplineScoreRow[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<DisciplineScoreRow[]>('/admin/discipline/scores')
  return data
}

/** O'quvchiga sabab bo'yicha ball kiritish. */
export async function addDisciplinePoint(
  studentId: string,
  reasonId: string,
  note?: string,
): Promise<DisciplinePoint> {
  const { data } = await api.post<DisciplinePoint>('/admin/discipline/points', {
    studentId,
    reasonId,
    note,
  })
  return data
}

/** Bitta o'quvchining ball tarixi. */
export async function getStudentDisciplinePoints(studentId: string): Promise<DisciplinePoint[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<DisciplinePoint[]>('/admin/discipline/points', {
    params: { studentId },
  })
  return data
}

export async function deleteDisciplinePoint(id: string): Promise<void> {
  await api.delete(`/admin/discipline/points/${id}`)
}
