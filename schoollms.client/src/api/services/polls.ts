import { api } from '../client'

// So'rovnomalar va NPS (client, 2026-10-07) — `/api/admin/polls`, gated by the `polls` key.

export type PollAudience = 'parents' | 'teachers'
export type PollStatus = 'draft' | 'open' | 'closed'
export type PollQuestionKind = 'nps' | 'stars' | 'choice' | 'text'
export type PollTarget = 'school' | 'teachers' | 'management'

export interface PollQuestion {
  id: string
  position: number
  kind: PollQuestionKind
  text: string
  options: string[]
  required: boolean
  target: PollTarget
}

export interface Poll {
  id: string
  title: string
  description: string | null
  audience: PollAudience
  anonymous: boolean
  status: PollStatus
  closesOn: string | null
  createdAt: string
  openedAt: string | null
  closedAt: string | null
  notifiedAt: string | null
  responses: number
  questions: PollQuestion[]
}

export interface PollQuestionInput {
  kind: PollQuestionKind
  text: string
  options: string[] | null
  required: boolean
  target: PollTarget
}

export interface PollInput {
  title: string
  description: string | null
  audience: PollAudience
  anonymous: boolean
  closesOn: string | null
  questions: PollQuestionInput[]
}

export interface PollCount {
  label: string
  count: number
}

export interface PollSubjectResult {
  id: string
  name: string
  answers: number
  average: number | null
  nps: number | null
}

export interface PollTextAnswer {
  text: string
  subjectName: string | null
  className: string | null
  respondentName: string | null
  at: string
}

export interface PollQuestionResult {
  questionId: string
  kind: PollQuestionKind
  text: string
  target: PollTarget
  answers: number
  average: number | null
  nps: number | null
  promoters: number
  passives: number
  detractors: number
  distribution: PollCount[]
  subjects: PollSubjectResult[]
  texts: PollTextAnswer[]
}

export interface PollResults {
  pollId: string
  title: string
  audience: PollAudience
  anonymous: boolean
  responses: number
  className: string | null
  classes: string[]
  questions: PollQuestionResult[]
}

export const POLL_AUDIENCE: Record<PollAudience, string> = { parents: 'Ota-onalar', teachers: "O'qituvchilar" }
export const POLL_STATUS: Record<PollStatus, string> = { draft: 'Qoralama', open: 'Ochiq', closed: 'Yopilgan' }
export const POLL_KIND: Record<PollQuestionKind, string> = {
  nps: 'NPS (0–10)',
  stars: 'Yulduzcha (1–5)',
  choice: 'Variantlardan biri',
  text: 'Erkin javob',
}
export const POLL_TARGET: Record<PollTarget, string> = {
  school: 'Maktab haqida',
  teachers: 'Farzandning har bir ustozi',
  management: 'Rahbariyatning har bir a\'zosi',
}

/** NPS colour: green from +30, amber 0..29, red below zero. */
export function npsTone(nps: number | null): string {
  if (nps === null) return 'text-slate-300'
  if (nps >= 30) return 'text-emerald-600'
  if (nps >= 0) return 'text-amber-600'
  return 'text-red-600'
}

export const formatNps = (nps: number | null) => (nps === null ? '—' : nps > 0 ? `+${nps}` : String(nps))

export async function getPolls(): Promise<Poll[]> {
  const { data } = await api.get<Poll[]>('/admin/polls')
  return data
}

export async function createPoll(input: PollInput): Promise<Poll> {
  const { data } = await api.post<Poll>('/admin/polls', input)
  return data
}

export async function updatePoll(id: string, input: PollInput): Promise<Poll> {
  const { data } = await api.put<Poll>(`/admin/polls/${id}`, input)
  return data
}

export async function openPoll(id: string): Promise<Poll> {
  const { data } = await api.post<Poll>(`/admin/polls/${id}/open`)
  return data
}

export async function closePoll(id: string): Promise<Poll> {
  const { data } = await api.post<Poll>(`/admin/polls/${id}/close`)
  return data
}

export async function deletePoll(id: string): Promise<void> {
  await api.delete(`/admin/polls/${id}`)
}

export async function getPollRecipients(id: string): Promise<{ count: number; botConfigured: boolean }> {
  const { data } = await api.get<{ count: number; botConfigured: boolean }>(`/admin/polls/${id}/recipients`)
  return data
}

export async function notifyPoll(id: string): Promise<{ total: number; sent: number }> {
  const { data } = await api.post<{ total: number; sent: number }>(`/admin/polls/${id}/notify`)
  return data
}

export async function getPollResults(id: string, className?: string): Promise<PollResults> {
  const { data } = await api.get<PollResults>(`/admin/polls/${id}/results`, { params: className ? { className } : {} })
  return data
}
