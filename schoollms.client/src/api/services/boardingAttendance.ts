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
  /** Aniq sabab: keldi — kechikish sababi yoki null; kelmadi — yo'qlik sababi (2026-09-26). */
  reasonId?: string | null
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
  marks: { studentId: string; status: BoardingStatus; reasonId?: string | null }[],
): Promise<{ saved: number; notified: number }> {
  const { data } = await api.put<{ saved: number; notified: number }>('/admin/boarding-attendance', {
    date,
    session,
    marks,
  })
  return data
}

/* ---------- Ro'yxat — qo'lda (2026-10-11: "abonomentga qaralmasin") ---------- */

export interface BoardingRosterRow {
  studentId: string
  fullName: string
  className: string
  /** Yo'nalish guruhi (bo'lmasa null) */
  trackName: string | null
  /** Ro'yxatga qo'shilgan kun ("YYYY-MM-DD") */
  addedOn: string
  addedByName: string | null
}

export interface BoardingCandidate {
  studentId: string
  fullName: string
  className: string
  trackName: string | null
}

export async function getBoardingRoster(session: BoardingSession): Promise<BoardingRosterRow[]> {
  const { data } = await api.get<BoardingRosterRow[]>('/admin/boarding-attendance/roster', { params: { session } })
  return data
}

export async function getBoardingCandidates(session: BoardingSession, q: string): Promise<BoardingCandidate[]> {
  const { data } = await api.get<BoardingCandidate[]>('/admin/boarding-attendance/roster/candidates', {
    params: { session, q: q || undefined },
  })
  return data
}

export async function addToBoardingRoster(session: BoardingSession, studentIds: string[]): Promise<{ added: number }> {
  const { data } = await api.post<{ added: number }>('/admin/boarding-attendance/roster', { session, studentIds })
  return data
}

export async function removeFromBoardingRoster(session: BoardingSession, studentId: string): Promise<void> {
  await api.post('/admin/boarding-attendance/roster/remove', { session, studentId })
}

/* ---------- Statistika — "Davomat analitikasi 2.0" (2026-10-11) ---------- */

export interface BoardingTally {
  studentDays: number
  present: number
  absent: number
  excused: number
  late: number
  unchecked: number
  presentPct: number | null
  absentPct: number | null
}

export interface BoardingAnalytics {
  session: BoardingSession
  from: string
  to: string
  /** Yo'qlama olingan kunlar */
  heldDays: number
  /** Bugun ro'yxatda */
  rosterNow: number
  total: BoardingTally
  trend: { date: string; tally: BoardingTally }[]
  groups: { key: string; name: string; students: number; tally: BoardingTally }[]
  reasons: { reasonId: string; name: string; isLate: boolean; count: number }[]
  students: {
    studentId: string
    fullName: string
    className: string
    groupName: string
    tally: BoardingTally
    lastAbsent: string | null
  }[]
  /** absent | excused | late | unchecked */
  events: { date: string; studentId: string; fullName: string; groupName: string; status: string; reasonId: string | null }[]
  eventsTruncated: boolean
}

export async function getBoardingAnalytics(session: BoardingSession, from: string, to: string): Promise<BoardingAnalytics> {
  const { data } = await api.get<BoardingAnalytics>('/admin/boarding-attendance/analytics', { params: { session, from, to } })
  return data
}

export async function downloadBoardingAnalytics(session: BoardingSession, from: string, to: string): Promise<void> {
  const res = await api.get('/admin/boarding-attendance/analytics/export', { params: { session, from, to }, responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^";]+)"?/)
  a.download = m?.[1] ?? `${session}_${from}_${to}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
