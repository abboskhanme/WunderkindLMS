import { api, USE_MOCK } from '../client'

/* =========================================================================
 *  O'quvchi holati taglari — docs/modules/students-parity.md §2.3 (S-5).
 *
 *  Holat — ARXIVLASHDAN OLDINGI kuzatuv vositasi ("VIP", "Sinov muddatida",
 *  "Ko'chib ketmoqchi"). Arxivlash sabablari bilan chalkashtirmang: u
 *  o'quvchi KETGANDAN keyingi yorliq.
 * ========================================================================= */

/** Katalogdagi bitta holat. */
export interface StudentStatusTag {
  id: string
  name: string
  /** `#RRGGBB` yoki null (neytral rang). */
  color: string | null
  position: number
  /** Tizim qatori — tahrirlab ham, o'chirib ham bo'lmaydi. */
  isDefault: boolean
  isActive: boolean
  /** Nechta o'quvchida qo'yilgan — 0 bo'lsagina o'chirish mumkin. */
  usedBy: number
}

export interface SaveStudentStatusInput {
  name: string
  /** `#RRGGBB`; bo'sh satr — rangni tozalaydi. */
  color?: string | null
  position?: number
  isActive?: boolean
}

export async function getStudentStatuses(includeInactive = false): Promise<StudentStatusTag[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<StudentStatusTag[]>('/admin/student-statuses', {
    params: includeInactive ? { includeInactive: true } : undefined,
  })
  return data
}

export async function createStudentStatus(input: SaveStudentStatusInput): Promise<StudentStatusTag> {
  const { data } = await api.post<StudentStatusTag>('/admin/student-statuses', input)
  return data
}

export async function updateStudentStatus(
  id: string,
  input: SaveStudentStatusInput,
): Promise<StudentStatusTag> {
  const { data } = await api.put<StudentStatusTag>(`/admin/student-statuses/${id}`, input)
  return data
}

export async function deleteStudentStatus(id: string): Promise<void> {
  await api.delete(`/admin/student-statuses/${id}`)
}

/** O'quvchiga holat qo'yadi. `null` — holatni olib tashlaydi. */
export async function setStudentStatus(studentId: string, statusId: string | null): Promise<void> {
  await api.put(`/admin/students/${studentId}/status`, { statusId })
}
