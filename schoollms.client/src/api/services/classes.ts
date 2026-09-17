import type { ClassGroups, HomeroomTeacher, SchoolClass } from '@/types'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { classesMock } from '../mock/classes'

export type ClassPayload = Omit<SchoolClass, 'id'>

/** C-3: ro'yxatdagi qidiruv — nom yoki xona bo'yicha. */
export interface ClassListParams {
  search?: string
  includeArchived?: boolean
}

export async function getClasses(params: ClassListParams = {}): Promise<SchoolClass[]> {
  if (USE_MOCK) {
    await delay()
    return classesMock
  }
  const { data } = await api.get<SchoolClass[]>('/admin/classes', {
    params: {
      search: params.search?.trim() || undefined,
      includeArchived: params.includeArchived || undefined,
    },
  })
  return data
}

/** Sinflar ro'yxatini Excel (.xlsx) ga yuklab oladi — ekrandagi qidiruv bilan bir xil qatorlar (C-3). */
export async function downloadClasses(search?: string): Promise<void> {
  if (USE_MOCK) {
    alert('Eksport faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get('/admin/classes/export', {
    params: { search: search?.trim() || undefined },
    responseType: 'blob',
  })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^"]+)"?/)
  a.download = m?.[1] ?? `sinflar_${new Date().toISOString().slice(0, 10)}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
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
 * Sinfni yangilash.
 *
 * P1-21: `applyFee` parametri olib tashlandi. Oylik to'lovni o'zgartirish endi
 * PULGA TEGMAYDI — narx `student_subscriptions.monthly_amount` da, sinf narxi
 * esa faqat yangi obuna ochilganda taklif qilinadigan standart qiymat
 * (SPEC §3.7). Mavjud obunalarni "Moliya → Obunalar" ekrani boshqaradi.
 */
export async function updateClass(id: string, payload: ClassPayload): Promise<SchoolClass> {
  if (USE_MOCK) {
    await delay(200)
    return { ...payload, id }
  }
  const { data } = await api.put<SchoolClass>(`/admin/classes/${id}`, payload)
  return data
}

export async function deleteClass(id: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.delete(`/admin/classes/${id}`)
}

/** Arxivlangan sinflar ro'yxati. */
export async function getArchivedClasses(): Promise<SchoolClass[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<SchoolClass[]>('/admin/classes/archived')
  return data
}

/** Sinfni arxivlash — o'quvchilari ham arxivlanadi. */
export async function archiveClass(id: string): Promise<{ archivedStudents: number }> {
  const { data } = await api.post<{ archivedStudents: number }>(`/admin/classes/${id}/archive`)
  return data
}

/** Sinfni arxivdan chiqarish — sinf bilan arxivlangan o'quvchilar ham qaytariladi. */
export async function unarchiveClass(id: string): Promise<{ restoredStudents: number }> {
  const { data } = await api.post<{ restoredStudents: number }>(`/admin/classes/${id}/unarchive`)
  return data
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

/* ---------- Sinf rahbari(lari) — C-5 ---------- */

/** Shu sinfga biriktirilgan sinf rahbari(lar)i (`teachers.homeroom_class` dan). */
export async function getHomeroomTeachers(classId: string): Promise<HomeroomTeacher[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<HomeroomTeacher[]>(`/admin/classes/${classId}/homeroom-teachers`)
  return data
}

/**
 * Sinf rahbari(lar)ini belgilaydi — ro'yxatda yo'q o'qituvchi bo'shatiladi, bor o'qituvchi
 * shu sinfga biriktiriladi (bitta o'qituvchi faqat bitta sinf rahbari bo'ladi).
 */
export async function setHomeroomTeachers(classId: string, teacherIds: string[]): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    return
  }
  await api.put(`/admin/classes/${classId}/homeroom-teachers`, { teacherIds })
}
