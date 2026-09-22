import { api } from '../client'

/**
 * Kechki dars va yotoqxona davomati — `/api/admin/boarding-attendance` (mijoz, 2026-09-23).
 * Faqat shu kuni faol yotoqxona abonementi bor o'quvchilar belgilanadi.
 */
export type BoardingSession = 'evening' | 'dorm'
export type BoardingStatus = 'present' | 'absent' | 'excused'

export interface BoardingStudent {
  studentId: string
  fullName: string
  className: string
  /** false — shu kuni yotoqxona abonementi yo'q: kulrang, belgilanmaydi */
  eligible: boolean
  status: BoardingStatus | null
}

export interface BoardingSection {
  key: string
  title: string
  /** group — yo'nalish guruhi; class — guruhsizlar sinf bo'yicha */
  kind: 'group' | 'class'
  students: BoardingStudent[]
  eligible: number
  marked: number
  absent: number
}

export interface BoardingDay {
  date: string
  session: BoardingSession
  sections: BoardingSection[]
  eligible: number
  marked: number
  absent: number
}

export async function getBoardingDay(date: string, session: BoardingSession): Promise<BoardingDay> {
  const { data } = await api.get<BoardingDay>('/admin/boarding-attendance', { params: { date, session } })
  return data
}

export async function saveBoarding(
  date: string,
  session: BoardingSession,
  marks: { studentId: string; status: BoardingStatus }[],
): Promise<{ saved: number; notified: number }> {
  const { data } = await api.put<{ saved: number; notified: number }>('/admin/boarding-attendance', {
    date,
    session,
    marks,
  })
  return data
}
