import type { GuardianRelation, GuardianRow, ParentRow, TeacherAppRow } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

/** Ota-onalar ro'yxati (telefon bo'yicha guruhlangan). Admin "Ilova → Ota-onalar" sahifasi uchun. */
export async function getParents(): Promise<ParentRow[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<ParentRow[]>('/admin/parents')
  return data
}

/* =========================================================================
 *  §2.9 (P-1, P-2) — ro'yxat endi `guardians` + `student_guardians` dan.
 *
 *  ESKI YO'L TEGILMAGAN: yuqoridagi `getParents` o'z joyida qoladi
 *  (u telefon raqami bo'yicha guruhlaydi). Quyidagisi — QO'SHIMCHA yo'l.
 * ========================================================================= */

/** Ota-onalar ro'yxatining filtri. BARCHASI ixtiyoriy. */
export interface GuardianListFilter {
  search?: string
  className?: string
  groupId?: string
  studentId?: string
  relation?: GuardianRelation
  /** true = Telegram bog'langanlar, false = bog'lanmaganlar. */
  connected?: boolean
  /** Farzand holati: active (sukut) | archived | all. */
  state?: 'active' | 'archived' | 'all'
}

/** Filtrni so'rov satriga aylantiradi (bo'sh qiymatlar tushib qoladi). */
function guardianQuery(filter?: GuardianListFilter): string {
  const params = new URLSearchParams()
  if (filter?.search?.trim()) params.set('search', filter.search.trim())
  if (filter?.className) params.set('className', filter.className)
  if (filter?.groupId) params.set('groupId', filter.groupId)
  if (filter?.studentId) params.set('studentId', filter.studentId)
  if (filter?.relation) params.set('relation', filter.relation)
  if (filter?.connected !== undefined) params.set('connected', String(filter.connected))
  if (filter?.state) params.set('state', filter.state)
  const text = params.toString()
  return text ? `?${text}` : ''
}

/** Vasiy jadvalidan qurilgan ota-onalar ro'yxati (P-1). */
export async function getGuardianRows(filter?: GuardianListFilter): Promise<GuardianRow[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<GuardianRow[]>(`/admin/parents/guardians${guardianQuery(filter)}`)
  return data
}

/** P-2 — filtrlangan ro'yxatning .xlsx eksporti. */
export async function exportGuardianRows(filter?: GuardianListFilter): Promise<void> {
  if (USE_MOCK) {
    alert('Eksport faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get(`/admin/parents/guardians/export${guardianQuery(filter)}`, {
    responseType: 'blob',
  })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^"]+)"?/)
  a.download = m?.[1] ?? `ota-onalar_${new Date().toISOString().slice(0, 10)}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** O'qituvchilar ilova faolligi + qurilma. Admin "Ilova → O'qituvchilar" sahifasi uchun. */
export async function getTeacherAppUsers(): Promise<TeacherAppRow[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<TeacherAppRow[]>('/admin/app/teachers')
  return data
}
