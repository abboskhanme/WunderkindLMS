import type { ClassGroups, SchoolClass } from '@/types'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { classesMock } from '../mock/classes'

export type ClassPayload = Omit<SchoolClass, 'id'>

export async function getClasses(): Promise<SchoolClass[]> {
  if (USE_MOCK) {
    await delay()
    return classesMock
  }
  const { data } = await api.get<SchoolClass[]>('/admin/classes')
  return data
}

export async function createClass(payload: ClassPayload): Promise<SchoolClass> {
  if (USE_MOCK) {
    await delay(200)
    return { ...payload, id: uid() }
  }
  const { data } = await api.post<SchoolClass>('/admin/classes', payload)
  return data
}

/**
 * Sinfni yangilash. Oylik to'lov o'zgargan bo'lsa, `applyFee` orqali yangi narx joriy oy
 * o'quvchilariga qo'llanishini boshqaramiz: true = joriy oydan, false = keyingi oydan.
 */
export async function updateClass(
  id: string,
  payload: ClassPayload,
  applyFee?: boolean,
): Promise<SchoolClass> {
  if (USE_MOCK) {
    await delay(200)
    return { ...payload, id }
  }
  const { data } = await api.put<SchoolClass>(`/admin/classes/${id}`, payload, {
    params: applyFee === undefined ? undefined : { applyFee },
  })
  return data
}

export async function deleteClass(id: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.delete(`/admin/classes/${id}`)
}

/** Sinfdagi guruhlar (1/2) holati va lock holati. */
export async function getClassGroups(classId: string): Promise<ClassGroups> {
  if (USE_MOCK) {
    await delay(100)
    return {
      classId,
      className: '',
      locked: false,
      lockReason: null,
      canEdit: true,
      ungroupedCount: 0,
      group1Count: 0,
      group2Count: 0,
      students: [],
    }
  }
  const { data } = await api.get<ClassGroups>(`/admin/classes/${classId}/groups`)
  return data
}

/** O'quvchilarni guruhga belgilash. Backend yopiq bo'lsa 400 qaytaradi. */
export async function saveClassGroups(
  classId: string,
  assignments: { studentId: string; subGroup: number }[],
): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    return
  }
  await api.put(`/admin/classes/${classId}/groups`, { assignments })
}

/** Avtomatik bo'lish: alifbo bo'yicha 1/2 ga taqsimlanadi. Yopiq bo'lsa 400. */
export async function autoSplitClassGroups(classId: string): Promise<ClassGroups> {
  if (USE_MOCK) {
    await delay(150)
    return getClassGroups(classId)
  }
  const { data } = await api.post<ClassGroups>(`/admin/classes/${classId}/groups/auto-split`)
  return data
}
