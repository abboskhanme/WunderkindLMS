import { api } from '../client'

// So'rovnomalar va NPS (client, 2026-10-07) — `/api/admin/polls`, gated by the `polls` key.
// Google Forms parity (2026-10-15, docs/modules/surveys-forms.md): more kinds, sections, the
// public link `/sorovnoma/{token}`, the "Javoblar" table and its Excel export.

export type PollAudience = 'parents' | 'teachers' | 'public'
export type PollStatus = 'draft' | 'open' | 'closed'
export type PollQuestionKind =
  | 'nps'
  | 'stars'
  | 'choice'
  | 'text'
  | 'multi'
  | 'dropdown'
  | 'short'
  | 'scale'
  | 'number'
  | 'date'
  | 'section'
export type PollTarget = 'school' | 'teachers' | 'management'

/** The settings a question kind may carry — the editor sends them, the server drops what does not apply. */
export interface PollQuestionSettings {
  /** Help text under the question; a section's description. */
  description: string | null
  /** Choice and multi: a "Boshqa: ___" option. */
  allowOther: boolean
  /** Scale: 0 or 1. Number: the smallest allowed answer. */
  minValue: number | null
  /** Scale: 2..10. Number: the largest allowed answer. */
  maxValue: number | null
  minLabel: string | null
  maxLabel: string | null
  /** Multi: at least / at most this many ticks. */
  minChoices: number | null
  maxChoices: number | null
}

export interface PollQuestion extends PollQuestionSettings {
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
  /** "Havola orqali ham ochiq" (always true for the public audience). */
  publicLink: boolean
  /** The public page asks for a name and a phone. */
  collectContact: boolean
  /** Set only while the link is open — the page is `/sorovnoma/{publicToken}`. */
  publicToken: string | null
}

export interface PollQuestionInput extends PollQuestionSettings {
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
  publicLink: boolean
  collectContact: boolean
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
  description: string | null
  /** Number questions: the smallest and the largest answer. */
  min: number | null
  max: number | null
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

/** A column of the "Javoblar" table — every question except sections. */
export interface PollResponseColumn {
  questionId: string
  kind: PollQuestionKind
  text: string
}

export interface PollResponseRow {
  id: string
  submittedAt: string
  /** null on anonymous polls. */
  respondent: string | null
  phone: string | null
  className: string | null
  viaLink: boolean
  /** One per column, formatted as in the Excel file. */
  cells: string[]
}

export interface PollResponses {
  pollId: string
  title: string
  showRespondent: boolean
  showPhone: boolean
  /** A parents/teachers poll that has link answers too. */
  showSource: boolean
  total: number
  page: number
  pageSize: number
  classes: string[]
  columns: PollResponseColumn[]
  rows: PollResponseRow[]
}

export const POLL_AUDIENCE: Record<PollAudience, string> = {
  parents: 'Ota-onalar',
  teachers: "O'qituvchilar",
  public: 'Havola orqali (hamma)',
}
export const POLL_STATUS: Record<PollStatus, string> = { draft: 'Qoralama', open: 'Ochiq', closed: 'Yopilgan' }
/** In the editor's kind select, in this order. */
export const POLL_KIND: Record<PollQuestionKind, string> = {
  nps: 'NPS (0–10)',
  stars: 'Yulduzcha (1–5)',
  scale: 'Shkala',
  choice: 'Variantlardan biri',
  multi: 'Bir nechta variant',
  dropdown: "Ochiladigan ro'yxat",
  short: 'Qisqa javob',
  text: 'Erkin javob (abzas)',
  number: 'Son',
  date: 'Sana',
  section: "Bo'lim",
}
/** Kinds whose answer is one or more of the options. */
export const hasOptions = (kind: PollQuestionKind) => kind === 'choice' || kind === 'multi' || kind === 'dropdown'
export const POLL_OTHER = 'Boshqa'
/** The public page of a poll whose link is open. */
export const pollLink = (token: string) => `${window.location.origin}/sorovnoma/${token}`
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

/** "Nusxa olish" — the same form as a new draft. */
export async function duplicatePoll(id: string): Promise<Poll> {
  const { data } = await api.post<Poll>(`/admin/polls/${id}/duplicate`)
  return data
}

export async function getPollResponses(id: string, page: number, pageSize: number, className?: string): Promise<PollResponses> {
  const { data } = await api.get<PollResponses>(`/admin/polls/${id}/responses`, {
    params: { page, pageSize, ...(className ? { className } : {}) },
  })
  return data
}

/** Every response (class filter as on screen) as .xlsx, saved by the browser. */
export async function downloadPollResponses(id: string, className?: string): Promise<void> {
  const res = await api.get<Blob>(`/admin/polls/${id}/responses/export`, {
    params: className ? { className } : {},
    responseType: 'blob',
  })
  const url = URL.createObjectURL(res.data)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^";]+)"?/)
  a.download = m?.[1] ?? 'sorovnoma-javoblar.xlsx'
  document.body.appendChild(a)
  a.click()
  a.remove()
  window.setTimeout(() => URL.revokeObjectURL(url), 30_000)
}
