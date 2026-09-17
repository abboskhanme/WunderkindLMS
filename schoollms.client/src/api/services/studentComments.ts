import { api, USE_MOCK } from '../client'

/* =========================================================================
 *  O'quvchi izohlari — docs/modules/students-parity.md §2.3 (S-11).
 *
 *  BALL EMAS: intizomiy ball 100 dan ayriladi va hisobotga tushadi, izoh esa
 *  ballsiz kuzatuv ("onasi bilan gaplashildi"). Shuning uchun alohida jadval
 *  va alohida ekran.
 * ========================================================================= */

export type StudentCommentKind = 'positive' | 'negative'

export interface StudentComment {
  id: string
  studentId: string
  kind: StudentCommentKind
  body: string
  imageUrl: string | null
  fileUrl: string | null
  createdBy: string
  createdByName: string
  createdAt: string
  updatedAt: string | null
  /** Joriy foydalanuvchi shu izohni tahrirlay/o'chira oladimi (muallif yoki admin). */
  canEdit: boolean
}

export interface SaveStudentCommentInput {
  kind: StudentCommentKind
  body: string
  /** null — tegilmaydi; bo'sh satr — olib tashlanadi. */
  imageUrl?: string | null
  fileUrl?: string | null
}

export async function getStudentComments(
  studentId: string,
  kind?: StudentCommentKind,
): Promise<StudentComment[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<StudentComment[]>(`/admin/students/${studentId}/comments`, {
    params: kind ? { kind } : undefined,
  })
  return data
}

export async function createStudentComment(
  studentId: string,
  input: SaveStudentCommentInput,
): Promise<StudentComment> {
  const { data } = await api.post<StudentComment>(`/admin/students/${studentId}/comments`, input)
  return data
}

export async function updateStudentComment(
  id: string,
  input: SaveStudentCommentInput,
): Promise<StudentComment> {
  const { data } = await api.put<StudentComment>(`/admin/students/comments/${id}`, input)
  return data
}

export async function deleteStudentComment(id: string): Promise<void> {
  await api.delete(`/admin/students/comments/${id}`)
}
