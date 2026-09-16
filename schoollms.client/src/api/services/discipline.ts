import type {
  DisciplineReason,
  DisciplineScoreRow,
  DisciplinePoint,
  DisciplineFeed,
  DisciplineFeedFilters,
} from '@/types'
import { api, USE_MOCK } from '../client'

/* ---------- Ball sabablar ---------- */

export async function getDisciplineReasons(): Promise<DisciplineReason[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<DisciplineReason[]>('/admin/discipline/reasons')
  return data
}

export async function createDisciplineReason(name: string, points: number): Promise<DisciplineReason> {
  const { data } = await api.post<DisciplineReason>('/admin/discipline/reasons', { name, points })
  return data
}

export async function updateDisciplineReason(
  id: string,
  name: string,
  points: number,
): Promise<DisciplineReason> {
  const { data } = await api.put<DisciplineReason>(`/admin/discipline/reasons/${id}`, { name, points })
  return data
}

export async function deleteDisciplineReason(id: string): Promise<void> {
  await api.delete(`/admin/discipline/reasons/${id}`)
}

/** Davomat sababiga ball belgilash (jurnalda shu sabab bilan davomat qoldiga ta'sir qiladi). */
export async function setAttendanceReasonPoints(id: string, points: number): Promise<DisciplineReason> {
  const { data } = await api.put<DisciplineReason>(`/admin/discipline/reasons/attendance/${id}`, { points })
  return data
}

/* ---------- Ballar nazorati ---------- */

export async function getDisciplineScores(): Promise<DisciplineScoreRow[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<DisciplineScoreRow[]>('/admin/discipline/scores')
  return data
}

/** O'quvchiga sabab bo'yicha ball kiritish. */
export async function addDisciplinePoint(
  studentId: string,
  reasonId: string,
  note?: string,
): Promise<DisciplinePoint> {
  const { data } = await api.post<DisciplinePoint>('/admin/discipline/points', {
    studentId,
    reasonId,
    note,
  })
  return data
}

/** Bitta o'quvchining ball tarixi. */
export async function getStudentDisciplinePoints(studentId: string): Promise<DisciplinePoint[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<DisciplinePoint[]>('/admin/discipline/points', {
    params: { studentId },
  })
  return data
}

export async function deleteDisciplinePoint(id: string): Promise<void> {
  await api.delete(`/admin/discipline/points/${id}`)
}

/* ---------- Harakatlar (maktab bo'ylab lenta) ---------- */

const EMPTY_FEED: DisciplineFeed = {
  items: [],
  total: 0,
  page: 1,
  pageSize: 50,
  plusCount: 0,
  minusCount: 0,
  pointsSum: 0,
  authors: [],
  classNames: [],
}

/** Bo'sh qiymatlar so'rovga tushmasin ("&author=" server uchun ham shovqin). */
function clean(filters: Record<string, unknown>): Record<string, unknown> {
  return Object.fromEntries(
    Object.entries(filters).filter(([, v]) => v !== undefined && v !== null && v !== '' && v !== 'all'),
  )
}

/**
 * Maktab bo'ylab intizomiy harakatlar lentasi — qo'lda kiritilgan ballar va jurnal davomati
 * bir ro'yxatda. Davr berilmasa server oxirgi 30 kunni beradi.
 */
export async function getDisciplineFeed(filters: DisciplineFeedFilters = {}): Promise<DisciplineFeed> {
  if (USE_MOCK) return EMPTY_FEED
  const { data } = await api.get<DisciplineFeed>('/admin/discipline/feed', {
    params: clean({ ...filters }),
  })
  return data
}

/** Ballar nazorati filtrlari (eksport serverda xuddi shu filtrlarni takrorlaydi). */
export interface DisciplineScoreFilters {
  className?: string
  search?: string
  minPoints?: number
  maxPoints?: number
  sort?: string
}

/** Ballar nazoratini Excel (.xlsx) ga yuklab oladi — ekrandagi filtrlar bilan bir xil qatorlar. */
export async function downloadDisciplineScores(filters: DisciplineScoreFilters = {}): Promise<void> {
  if (USE_MOCK) {
    alert('Eksport faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get('/admin/discipline/scores/export', {
    params: clean({ ...filters }),
    responseType: 'blob',
  })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^"]+)"?/)
  a.download = m?.[1] ?? `ballar_nazorati_${new Date().toISOString().slice(0, 10)}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
