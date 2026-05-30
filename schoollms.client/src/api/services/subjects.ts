import type { Subject } from '@/types'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { subjectsMock } from '../mock/subjects'

export interface SubjectPayload {
  name: string
}

export async function getSubjects(): Promise<Subject[]> {
  if (USE_MOCK) {
    await delay()
    return subjectsMock
  }
  const { data } = await api.get<Subject[]>('/admin/subjects')
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
