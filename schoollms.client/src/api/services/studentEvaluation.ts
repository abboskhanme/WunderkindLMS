import type { EvaluationType, EvaluationBoard } from '@/types'
import { api, USE_MOCK } from '../client'

/* ---------- Baholash turlari ---------- */

export async function getEvaluationTypes(): Promise<EvaluationType[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<EvaluationType[]>('/admin/student-evaluation/types')
  return data
}

export async function createEvaluationType(
  name: string,
  description: string,
): Promise<EvaluationType> {
  const { data } = await api.post<EvaluationType>('/admin/student-evaluation/types', {
    name,
    description,
  })
  return data
}

export async function updateEvaluationType(
  id: string,
  name: string,
  description: string,
): Promise<EvaluationType> {
  const { data } = await api.put<EvaluationType>(`/admin/student-evaluation/types/${id}`, {
    name,
    description,
  })
  return data
}

export async function deleteEvaluationType(id: string): Promise<void> {
  await api.delete(`/admin/student-evaluation/types/${id}`)
}

/* ---------- Baholash jadvali ---------- */

/** Baholash jadvali. `subjectId` — aniq fan (tahrir) yoki "all"/bo'sh (fanlar o'rtachasi, ko'rish). */
export async function getEvaluationBoard(
  month?: string,
  week = 0,
  subjectId?: string,
): Promise<EvaluationBoard> {
  if (USE_MOCK)
    return { months: [], month: '', week: 0, types: [], rows: [], subjectId: 'all', subjects: [] }
  const { data } = await api.get<EvaluationBoard>('/admin/student-evaluation/board', {
    params: { month, week, subjectId },
  })
  return data
}

/** Bitta o'quvchiga bitta fan + tur bo'yicha bir oyda baho qo'yish (1-5). score=null = tozalash. */
export async function setEvaluationGrade(
  studentId: string,
  typeId: string,
  month: string,
  week: number,
  score: number | null,
  subjectId?: string,
): Promise<void> {
  await api.post('/admin/student-evaluation/grade', {
    studentId, typeId, month, week, score, subjectId,
  })
}
