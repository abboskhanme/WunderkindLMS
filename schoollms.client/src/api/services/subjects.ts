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
}

/**
 * Fanlar ro'yxati. `groupable` berilsa — faqat guruhlarga bo'linadiganlari
 * (guruh formasining fan tanlovi aynan shuni so'raydi).
 */
export async function getSubjects(groupable?: boolean): Promise<Subject[]> {
  if (USE_MOCK) {
    await delay()
    return subjectsMock
  }
  const { data } = await api.get<Subject[]>('/admin/subjects', {
    params: groupable ? { groupable: true } : undefined,
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
