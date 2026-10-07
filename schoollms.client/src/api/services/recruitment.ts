import axios from 'axios'
import { api } from '../client'

// Ishga qabul va adaptatsiya (client, 2026-10-07) — `/api/admin/recruitment` (key `recruitment`,
// reads gated) and the public vacancy page `/api/public/vacancies`.

export type ApplicantStage =
  | 'new'
  | 'cv'
  | 'test'
  | 'demo'
  | 'hr_interview'
  | 'director_interview'
  | 'onboarding'
  | 'adapted'
  | 'rejected'

export const PIPELINE: ApplicantStage[] = ['cv', 'test', 'demo', 'hr_interview', 'director_interview']

export const STAGE_LABEL: Record<ApplicantStage, string> = {
  new: 'Ariza',
  cv: "Ma'lumot / CV",
  test: 'Test',
  demo: 'Demo dars',
  hr_interview: 'HR suhbat',
  director_interview: 'Direktor suhbati',
  onboarding: 'Adaptatsiya',
  adapted: 'Ishga qabul qilingan',
  rejected: 'Rad etilgan',
}

export interface Vacancy {
  id: string
  title: string
  department: string | null
  description: string | null
  requirements: string | null
  slug: string
  status: 'open' | 'closed'
  createdAt: string
  closedAt: string | null
  applicants: number
  active: number
}

export interface VacancyInput {
  title: string
  department: string | null
  description: string | null
  requirements: string | null
}

export interface ApplicantRow {
  id: string
  fullName: string
  phone: string
  email: string | null
  vacancyId: string | null
  vacancyTitle: string | null
  position: string | null
  source: 'public' | 'manual'
  stage: ApplicantStage
  stageLabel: string
  stageChangedAt: string
  createdAt: string
  hasCv: boolean
  lastScore: number | null
  rejectReason: string | null
  rejectedAtStage: ApplicantStage | null
  hiredOn: string | null
  stepsDone: number
  stepsTotal: number
  stepsOverdue: number
}

export interface ApplicantStageResult {
  stage: ApplicantStage
  stageLabel: string
  score: number | null
  comment: string | null
  byName: string
  at: string
}

export interface ApplicantEvent {
  fromStage: ApplicantStage | null
  toStage: ApplicantStage
  toLabel: string
  note: string | null
  byName: string
  at: string
}

export interface OnboardingStep {
  id: string
  position: number
  title: string
  description: string | null
  dueOn: string
  day: number
  doneAt: string | null
  doneByName: string | null
  comment: string | null
  overdue: boolean
}

export interface ApplicantDetail {
  row: ApplicantRow
  experience: string | null
  about: string | null
  cvName: string | null
  results: ApplicantStageResult[]
  events: ApplicantEvent[]
  onboarding: OnboardingStep[]
  onboardingDay: number | null
}

export interface ApplicantInput {
  fullName: string
  phone: string
  email: string | null
  vacancyId: string | null
  position: string | null
  experience: string | null
  about: string | null
}

export interface OnboardingTemplate {
  id: string
  position: number
  title: string
  description: string | null
  dueDay: number
  isActive: boolean
}

export interface RecruitmentCounts {
  new: number
  pipeline: Record<string, number>
  onboarding: number
  adapted: number
  rejected: number
}

const A = '/admin/recruitment'

export const getVacancies = async () => (await api.get<Vacancy[]>(`${A}/vacancies`)).data
export const createVacancy = async (input: VacancyInput) => (await api.post<Vacancy>(`${A}/vacancies`, input)).data
export const updateVacancy = async (id: string, input: VacancyInput) => {
  await api.put(`${A}/vacancies/${id}`, input)
}
export const setVacancyOpen = async (id: string, open: boolean) => {
  await api.post(`${A}/vacancies/${id}/${open ? 'open' : 'close'}`)
}

export const getRecruitmentCounts = async () => (await api.get<RecruitmentCounts>(`${A}/counts`)).data
export const getApplicants = async (tab: string, q?: string, vacancyId?: string) =>
  (await api.get<ApplicantRow[]>(`${A}/applicants`, { params: { tab, q: q || undefined, vacancyId: vacancyId || undefined } })).data
export const getApplicant = async (id: string) => (await api.get<ApplicantDetail>(`${A}/applicants/${id}`)).data

export async function createApplicant(input: ApplicantInput, cv: File | null): Promise<ApplicantDetail> {
  const fd = new FormData()
  for (const [k, v] of Object.entries(input)) if (v !== null && v !== '') fd.append(k, String(v))
  if (cv) fd.append('cv', cv)
  return (await api.post<ApplicantDetail>(`${A}/applicants`, fd)).data
}

export const updateApplicant = async (id: string, input: ApplicantInput) => {
  await api.put(`${A}/applicants/${id}`, input)
}

export async function uploadApplicantCv(id: string, cv: File): Promise<void> {
  const fd = new FormData()
  fd.append('cv', cv)
  await api.post(`${A}/applicants/${id}/cv`, fd)
}

/** The CV through the authenticated client — the file is never public. */
export async function openApplicantCv(id: string, name: string | null): Promise<void> {
  const { data } = await api.get<Blob>(`${A}/applicants/${id}/cv`, { responseType: 'blob' })
  const url = URL.createObjectURL(data)
  const a = document.createElement('a')
  a.href = url
  a.download = name || 'cv'
  a.click()
  setTimeout(() => URL.revokeObjectURL(url), 10_000)
}

export const moveApplicant = async (id: string, stage: ApplicantStage, note?: string) => {
  await api.post(`${A}/applicants/${id}/move`, { stage, note: note || null })
}
export const saveStageResult = async (id: string, stage: ApplicantStage, score: number | null, comment: string | null) => {
  await api.put(`${A}/applicants/${id}/results/${stage}`, { score, comment })
}
export const rejectApplicant = async (id: string, reason: string) => {
  await api.post(`${A}/applicants/${id}/reject`, { reason })
}
export const hireApplicant = async (id: string, hiredOn: string | null) => {
  await api.post(`${A}/applicants/${id}/hire`, { hiredOn })
}
export const setOnboardingStep = async (id: string, stepId: string, done: boolean, comment: string | null) => {
  await api.put(`${A}/applicants/${id}/onboarding/${stepId}`, { done, comment })
}
export const finishOnboarding = async (id: string, note?: string) => {
  await api.post(`${A}/applicants/${id}/adapted`, { stage: 'adapted', note: note || null })
}

export const getOnboardingTemplates = async () => (await api.get<OnboardingTemplate[]>(`${A}/onboarding-steps`)).data
export const saveOnboardingTemplates = async (
  steps: { id: string | null; title: string; description: string | null; dueDay: number; isActive: boolean }[],
) => (await api.put<OnboardingTemplate[]>(`${A}/onboarding-steps`, steps)).data

// ------------------------------------------------------------------ public page

/** Its own instance: no session, no 401 redirect — the page is opened from a Telegram/Instagram link. */
const publicApi = axios.create({ baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api', timeout: 30_000 })

export interface PublicVacancy {
  slug: string
  title: string
  department: string | null
  description: string | null
  requirements: string | null
}

export const getPublicVacancies = async () => (await publicApi.get<PublicVacancy[]>('/public/vacancies')).data
export const getPublicVacancy = async (slug: string) =>
  (await publicApi.get<PublicVacancy>(`/public/vacancies/${encodeURIComponent(slug)}`)).data

export async function applyToVacancy(
  slug: string,
  form: { fullName: string; phone: string; email: string; experience: string; about: string; website: string },
  cv: File | null,
): Promise<void> {
  const fd = new FormData()
  for (const [k, v] of Object.entries(form)) if (v) fd.append(k, v)
  if (cv) fd.append('cv', cv)
  await publicApi.post(`/public/vacancies/${encodeURIComponent(slug)}/apply`, fd)
}

/** The server's `{ message }`, or a fallback. */
export function publicErrorMessage(err: unknown, fallback: string): string {
  if (axios.isAxiosError(err)) {
    if (err.response?.status === 429) return "Juda ko'p urinish — birozdan keyin qayta yuboring"
    const m = (err.response?.data as { message?: string } | undefined)?.message
    if (m) return m
  }
  return fallback
}
