import { api } from '../client'

// O'rindosh ustoz (client, 2026-10-07) — `/api/admin/substitutions`, gated by the `teachers` key.

export interface SubstitutionLesson {
  period: number
  startTime: string | null
  endTime: string | null
  ownerKind: string
  ownerId: string
  ownerName: string
  subjectId: string
  subjectName: string
  subGroup: number
  substitutionId: string | null
  substituteTeacherId: string | null
  substituteTeacherName: string | null
}

export interface SubstitutionTeacherOption {
  id: string
  fullName: string
  /** Periods this teacher already teaches (or covers) that day. */
  busyPeriods: number[]
}

export interface SubstitutionDay {
  date: string
  teacherId: string
  teacherName: string
  lessons: SubstitutionLesson[]
  teachers: SubstitutionTeacherOption[]
}

export interface Substitution {
  id: string
  date: string
  period: number
  ownerKind: string
  ownerId: string
  ownerName: string
  subjectId: string
  subjectName: string
  subGroup: number
  absentTeacherId: string
  absentTeacherName: string
  substituteTeacherId: string
  substituteTeacherName: string
  reason: string | null
  createdByName: string
  createdAt: string
}

export interface SubstitutionCreateItem {
  period: number
  ownerId: string
  subjectId: string
  subGroup: number
  substituteTeacherId: string
}

export async function getSubstitutionDay(teacherId: string, date: string): Promise<SubstitutionDay> {
  const { data } = await api.get<SubstitutionDay>('/admin/substitutions/day', { params: { teacherId, date } })
  return data
}

export async function getSubstitutions(params: { from?: string; to?: string; teacherId?: string }): Promise<Substitution[]> {
  const { data } = await api.get<Substitution[]>('/admin/substitutions', { params })
  return data
}

export async function createSubstitutions(payload: {
  date: string
  absentTeacherId: string
  items: SubstitutionCreateItem[]
  reason?: string | null
}): Promise<Substitution[]> {
  const { data } = await api.post<Substitution[]>('/admin/substitutions', payload)
  return data
}

export async function cancelSubstitution(id: string): Promise<void> {
  await api.post(`/admin/substitutions/${id}/cancel`)
}
