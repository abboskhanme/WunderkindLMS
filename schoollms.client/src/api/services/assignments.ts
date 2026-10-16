import type { Assignment, AssignmentResult, AssignmentScoreboard, AssignmentType } from '@/types'
import { api, USE_MOCK } from '../client'

/** Topshiriq materiali kiritmasi (yuklangach metadata) */
export interface MaterialInput {
  name: string
  url: string
  size: number
  contentType: string
}

/** Test savoli kiritmasi */
export interface QuestionInput {
  text: string
  options: string[]
  correctIndex: number
}

/** Topshiriq yaratish/tahrirlash kiritmasi (o'qituvchi ishlatadi) */
export interface SaveAssignmentInput {
  subjectId: string
  title: string
  description?: string
  format: string
  /** Sinf id'lari, yoki `ownerKind` "group" bo'lsa — o'quv guruh id'lari (G-20) */
  classIds: string[]
  startDate?: string | null
  dueDate?: string | null
  lateAccept: boolean
  latePenaltyPct: number
  maxScore: number
  autoGrade: boolean
  materials: MaterialInput[]
  questions: QuestionInput[]
  /** G-20: "class" (sukut) | "group". Eski mijoz (bu maydonni yubormaydi) — serverda "class" deb qabul qilinadi. */
  ownerKind?: 'class' | 'group'
}

/** Admin: barcha topshiriqlar (yoki sinf bo'yicha) — FAQAT KO'RISH */
export async function getAssignments(classId?: string): Promise<Assignment[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Assignment[]>('/admin/assignments', {
    params: classId ? { classId } : undefined,
  })
  return data
}

/** Admin: topshiriq natijalari — kim bajardi/bajarmadi + ball + yuborgan javobi */
export async function getAssignmentResults(id: string): Promise<AssignmentResult> {
  const { data } = await api.get<AssignmentResult>(`/admin/assignments/${id}/results`)
  return data
}

/** Admin: "Topshiriqlar bali" — sinf bo'yicha ball jadvali (o'quvchilar × topshiriqlar) */
export async function getAssignmentScoreboard(classId: string): Promise<AssignmentScoreboard> {
  const { data } = await api.get<AssignmentScoreboard>('/admin/assignments/scoreboard', {
    params: { classId },
  })
  return data
}

/** Admin: yangi topshiriq yaratish (o'qituvchidek — istalgan sinf+fan uchun) */
export async function createAssignment(input: SaveAssignmentInput): Promise<Assignment> {
  const { data } = await api.post<Assignment>('/admin/assignments', input)
  return data
}

/** Admin: topshiriqni tahrirlash (istalganini) */
export async function updateAssignment(id: string, input: SaveAssignmentInput): Promise<void> {
  await api.put(`/admin/assignments/${id}`, input)
}

/** Admin: topshiriqni o'chirish (istalganini) */
export async function deleteAssignment(id: string): Promise<void> {
  await api.delete(`/admin/assignments/${id}`)
}

/** Admin: topshiriq materiali sifatida fayl yuklash; yuklangan fayl metadatasini qaytaradi */
export async function uploadAdminFile(file: File): Promise<MaterialInput> {
  const fd = new FormData()
  fd.append('file', file)
  const { data } = await api.post<MaterialInput>('/admin/assignments/uploads', fd, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
}

/* ---------- Bulk import of test questions (docs/modules/test-import.md) ---------- */

/** Why a question was left out — the server's `AssignmentQuestionReason` (a closed list). */
export type AssignmentImportReason =
  | 'emptyText'
  | 'tooFewOptions'
  | 'tooManyOptions'
  | 'noCorrect'
  | 'badCorrect'
  | 'manyCorrect'
  | 'duplicateOption'

/**
 * `POST /admin/assignments/questions/parse` — the server writes NOTHING: the questions go
 * into the form and the assignment is saved as usual.
 */
export interface AssignmentQuestionParseResult {
  /** `row` — Excel sheet row (header = 1); `question` — position in the text / Word / JSON. */
  unit: 'row' | 'question'
  totalRows: number
  validCount: number
  errorCount: number
  questions: QuestionInput[]
  errors: { reason: AssignmentImportReason; rows: number[] }[]
}

/** Admin: reads questions from text (an AI answer or the template format) or a file (.xlsx/.docx/.txt/.json). */
export async function parseAdminQuestions(input: File | string): Promise<AssignmentQuestionParseResult> {
  if (typeof input === 'string') {
    const { data } = await api.post<AssignmentQuestionParseResult>('/admin/assignments/questions/parse', {
      text: input,
    })
    return data
  }
  const fd = new FormData()
  fd.append('file', input)
  const { data } = await api.post<AssignmentQuestionParseResult>('/admin/assignments/questions/parse', fd, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
}

/** Admin: the Excel template — `test_savollari_shablon.xlsx`. */
export async function downloadAdminQuestionTemplate(): Promise<void> {
  const res = await api.get('/admin/assignments/questions/template', { responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'test_savollari_shablon.xlsx'
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** Admin: o'quvchining bajarish holatini va ballini belgilash (baholash) */
export async function setAdminSubmission(
  id: string,
  studentId: string,
  completed: boolean,
  score?: number | null,
): Promise<void> {
  await api.put(`/admin/assignments/${id}/submissions/${studentId}`, {
    completed,
    score: score ?? null,
  })
}

/* ---------- Topshiriq turlari (Sozlamalarda boshqariladi — kategoriya) ---------- */

export async function getAssignmentTypes(): Promise<AssignmentType[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<AssignmentType[]>('/admin/settings/assignment-types')
  return data
}

export async function saveAssignmentTypes(types: AssignmentType[]): Promise<void> {
  if (USE_MOCK) return
  await api.put('/admin/settings/assignment-types', { types })
}
