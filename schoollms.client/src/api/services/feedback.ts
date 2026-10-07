import type { Feedback } from '@/types'
import { api, USE_MOCK } from '../client'

/** Murojaat holatlari — mijoz aytgan uchtasi (2026-10-07). */
export const FEEDBACK_STATUS: Record<Feedback['status'], string> = {
  new: 'Qabul qilindi',
  in_progress: 'Jarayonda',
  resolved: 'Hal qilindi',
}

export interface FeedbackDepartmentMember {
  userId: string
  fullName: string
  /** "Taklif va shikoyatlar" bo'limiga kira oladimi (admin yoki ruxsati bor xodim). */
  hasAccess: boolean
}

export interface FeedbackDepartment {
  id: string
  name: string
  position: number
  isActive: boolean
  members: FeedbackDepartmentMember[]
}

export interface FeedbackDepartmentPayload {
  name: string
  position: number
  isActive: boolean
  memberUserIds: string[]
}

export interface FeedbackEvent {
  id: string
  status: string | null
  statusLabel: string | null
  departmentName: string | null
  assigneeName: string | null
  note: string | null
  /** true — yuboruvchiga ko'rinadigan javob */
  isPublic: boolean
  actorName: string
  createdAt: string
}

/** Bitta qadam: bo'sh maydonlar o'zgarmaydi. `answer` — yuboruvchiga, `note` — ichki izoh. */
export interface FeedbackUpdate {
  status?: Feedback['status']
  departmentId?: string
  assigneeId?: string
  clearAssignee?: boolean
  note?: string
  answer?: string
}

export async function getFeedback(
  type?: string,
  status?: string,
  departmentId?: string,
  mine?: boolean,
): Promise<Feedback[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Feedback[]>('/admin/feedback', {
    params: {
      type: type || undefined,
      status: status || undefined,
      departmentId: departmentId || undefined,
      mine: mine ? 'true' : undefined,
    },
  })
  return data
}

export async function getFeedbackItem(id: string): Promise<{ item: Feedback; history: FeedbackEvent[] }> {
  const { data } = await api.get<{ item: Feedback; history: FeedbackEvent[] }>(`/admin/feedback/${id}`)
  return data
}

export async function updateFeedback(id: string, patch: FeedbackUpdate): Promise<Feedback> {
  const { data } = await api.patch<Feedback>(`/admin/feedback/${id}`, patch)
  return data
}

export async function resolveFeedback(id: string): Promise<void> {
  await api.post(`/admin/feedback/${id}/resolve`)
}

export async function getFeedbackDepartments(): Promise<FeedbackDepartment[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<FeedbackDepartment[]>('/admin/feedback/departments')
  return data
}

export async function saveFeedbackDepartment(
  id: string | null,
  payload: FeedbackDepartmentPayload,
): Promise<FeedbackDepartment> {
  const { data } = id
    ? await api.put<FeedbackDepartment>(`/admin/feedback/departments/${id}`, payload)
    : await api.post<FeedbackDepartment>('/admin/feedback/departments', payload)
  return data
}

/** Mas'ul bo'la oladiganlar: administratorlar va xodimlar. */
export async function getFeedbackAssignees(): Promise<FeedbackDepartmentMember[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<FeedbackDepartmentMember[]>('/admin/feedback/assignees')
  return data
}
