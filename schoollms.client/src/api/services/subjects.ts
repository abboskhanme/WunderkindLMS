import type { Subject } from '@/types'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { subjectsMock } from '../mock/subjects'

export interface SubjectPayload {
  name: string
  /**
   * "Guruhlarga bo'linadi" (students-parity.md §2.5, G-9) — faqat shunday
   * fanga o'quv guruhi ochiladi.
   */
  isGroupable: boolean
  /** `#RRGGBB` yoki bo'sh/null — rangni tozalaydi (F-3). */
  color?: string | null
  /** Faolmi (F-3). Sukut — `true` (yangi fan har doim faol boshlanadi). */
  isActive?: boolean
}

/**
 * Fanlar ro'yxati. `groupable` berilsa — faqat guruhlarga bo'linadiganlari
 * (guruh formasining fan tanlovi aynan shuni so'raydi). `isActive` berilsa —
 * faqat faol (yoki faqat faolsiz) fanlar. Ikkalasi ham BERILMASA — hammasi:
 * jadval, jurnal, chorak bahosi kabi ko'plab ekran fan nomini ID bo'yicha shu
 * ro'yxatdan qidiradi va faolsizlantirilgan eski yozuvlar buzilmasligi kerak.
 */
export async function getSubjects(groupable?: boolean, isActive?: boolean): Promise<Subject[]> {
  if (USE_MOCK) {
    await delay()
    return subjectsMock
  }
  const params: Record<string, boolean> = {}
  if (groupable) params.groupable = true
  if (isActive !== undefined) params.isActive = isActive
  const { data } = await api.get<Subject[]>('/admin/subjects', {
    params: Object.keys(params).length > 0 ? params : undefined,
  })
  return data
}

export async function createSubject(payload: SubjectPayload): Promise<Subject> {
  if (USE_MOCK) {
    await delay(200)
    return { id: uid(), ...payload }
  }
  const { data } = await api.post<Subject>('/admin/subjects', payload)
  return data
}

export async function updateSubject(id: string, payload: SubjectPayload): Promise<Subject> {
  if (USE_MOCK) {
    await delay(200)
    return { id, ...payload }
  }
  const { data } = await api.put<Subject>(`/admin/subjects/${id}`, payload)
  return data
}

export async function deleteSubject(id: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.delete(`/admin/subjects/${id}`)
}

/** Fan qayerlarda ishlatilayotgani — o'chirishdan oldin tasdiqlash oynasi so'raydi. */
export interface SubjectUsage {
  canDelete: boolean
  /** "2 ta o'quv guruhi", "14 ta dars jadvali katagi" ... */
  usedIn: string[]
}

export async function getSubjectUsage(id: string): Promise<SubjectUsage> {
  if (USE_MOCK) return { canDelete: true, usedIn: [] }
  const { data } = await api.get<SubjectUsage>(`/admin/subjects/${id}/usage`)
  return data
}
