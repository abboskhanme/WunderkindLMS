import type {
  Assignment,
  AssignmentResult,
  AssignmentType,
  ChatMessage,
  JournalColumn,
  JournalEntry,
  JournalTopic,
  PortalMeta,
  QuarterGradeRow,
  SalaryLedger,
  Student,
  Subject,
  TeacherClass,
  TeacherLesson,
  Holiday,
  EvaluationBoard,
  EvaluationType,
} from '@/types'
import { api, USE_MOCK } from '../client'
import type { MaterialInput, SaveAssignmentInput } from './assignments'

/** O'qituvchi profili (panel sarlavhasi/salom uchun) */
export interface TeacherProfile {
  id: string
  fullName: string
  email: string
  homeroomClass: string
  subjects: Subject[]
}

export async function getTeacherProfile(): Promise<TeacherProfile | null> {
  if (USE_MOCK) return null
  const { data } = await api.get<TeacherProfile>('/teacher/me')
  return data
}

/** O'qituvchi dars beradigan sinflar (har biri o'qitadigan fanlari bilan) */
export async function getMyClasses(): Promise<TeacherClass[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<TeacherClass[]>('/teacher/classes')
  return data
}

/* ---------- O'quvchilarni baholash (o'z fanidan) ---------- */

export async function getTeacherEvalTypes(): Promise<EvaluationType[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<EvaluationType[]>('/teacher/evaluation/types')
  return data
}

/** O'qituvchining shu sinf+fan bo'yicha baholash jadvali (tanlangan oy). */
export async function getTeacherEvalBoard(
  classId: string,
  subjectId: string,
  month?: string,
): Promise<EvaluationBoard> {
  if (USE_MOCK)
    return { months: [], month: '', week: 0, types: [], rows: [], subjectId, subjects: [] }
  const { data } = await api.get<EvaluationBoard>('/teacher/evaluation/board', {
    params: { classId, subjectId, month },
  })
  return data
}

/** O'z fanidan bitta o'quvchiga bitta tur bo'yicha bir oyda baho qo'yish (1-5). score=null = tozalash. */
export async function setTeacherEvalGrade(
  classId: string,
  subjectId: string,
  studentId: string,
  typeId: string,
  month: string,
  score: number | null,
): Promise<void> {
  await api.post('/teacher/evaluation/grade', {
    classId, subjectId, studentId, typeId, month, week: 0, score,
  })
}

/** Fanning chorakdagi darslari (sana + dars raqami) — o'qituvchi jadvalidan */
export async function getTeacherLessons(
  classId: string,
  subjectId: string,
  quarter: number,
): Promise<JournalColumn[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<JournalColumn[]>('/teacher/journal/columns', {
    params: { classId, subjectId, quarter },
  })
  return data
}

/** Dars mavzulari/uyga vazifalari (jurnaldan) */
export async function getTeacherTopics(
  classId: string,
  subjectId: string,
  quarter: number,
): Promise<JournalTopic[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<JournalTopic[]>('/teacher/journal/notes', {
    params: { classId, subjectId, quarter },
  })
  return data
}

/** O'qituvchining o'zi yaratgan topshiriqlari */
export async function getTeacherAssignments(): Promise<Assignment[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Assignment[]>('/teacher/assignments')
  return data
}

export async function createTeacherAssignment(input: SaveAssignmentInput): Promise<Assignment> {
  const { data } = await api.post<Assignment>('/teacher/assignments', input)
  return data
}

export async function updateTeacherAssignment(id: string, input: SaveAssignmentInput): Promise<void> {
  await api.put(`/teacher/assignments/${id}`, input)
}

export async function deleteTeacherAssignment(id: string): Promise<void> {
  await api.delete(`/teacher/assignments/${id}`)
}

/** Topshiriq natijalari — kim bajardi/bajarmadi */
export async function getTeacherAssignmentResults(id: string): Promise<AssignmentResult> {
  const { data } = await api.get<AssignmentResult>(`/teacher/assignments/${id}/results`)
  return data
}

/** O'quvchining bajarish holatini belgilash */
export async function setTeacherSubmission(
  id: string,
  studentId: string,
  completed: boolean,
  score?: number | null,
): Promise<void> {
  await api.put(`/teacher/assignments/${id}/submissions/${studentId}`, {
    completed,
    score: score ?? null,
  })
}

/** Topshiriq materiali sifatida fayl yuklash; yuklangan fayl metadatasini qaytaradi */
export async function uploadTeacherFile(file: File): Promise<MaterialInput> {
  const fd = new FormData()
  fd.append('file', file)
  const { data } = await api.post<MaterialInput>('/teacher/uploads', fd, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
}

export async function getTeacherAssignmentTypes(): Promise<AssignmentType[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<AssignmentType[]>('/teacher/assignment-types')
  return data
}

/* ---------- Meta (choraklar, dars vaqtlari, davomat sabablari) ---------- */

export async function getTeacherMeta(): Promise<PortalMeta | null> {
  if (USE_MOCK) return null
  const { data } = await api.get<PortalMeta>('/teacher/meta')
  return data
}

/* ---------- Dars jadvali ---------- */

export async function getTeacherSchedule(quarter: number, week: number): Promise<TeacherLesson[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<TeacherLesson[]>('/teacher/schedule', {
    params: { quarter, week },
  })
  return data
}

/** Bayram kunlari — bu sanalarda dars bo'lmaydi (jadvalda "Bayram" deb ko'rsatiladi). */
export async function getTeacherHolidays(): Promise<Holiday[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Holiday[]>('/teacher/holidays')
  return data
}

/* ---------- Maosh (faqat o'ziniki) ---------- */

export async function getTeacherSalary(from?: string, to?: string): Promise<SalaryLedger | null> {
  if (USE_MOCK) return null
  const { data } = await api.get<SalaryLedger>('/teacher/salary', {
    params: { from, to },
  })
  return data
}

/* ---------- Jurnal (faqat o'zi dars beradigan sinf+fan) ---------- */

export async function getTeacherStudents(classId: string): Promise<Student[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Student[]>('/teacher/journal/students', { params: { classId } })
  return data
}

export async function getTeacherEntries(
  classId: string,
  subjectId: string,
  quarter: number,
): Promise<JournalEntry[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<JournalEntry[]>('/teacher/journal', {
    params: { classId, subjectId, quarter },
  })
  return data
}

export async function setTeacherEntry(
  classId: string,
  subjectId: string,
  quarter: number,
  studentId: string,
  date: string,
  period: number,
  payload: { grade?: number | null; reasonId?: string | null; homework?: number; behavior?: number; mastery?: number | null },
): Promise<void> {
  await api.put('/teacher/journal', {
    classId, subjectId, quarter, studentId, date, period, ...payload,
  })
}

export async function clearTeacherEntry(
  classId: string,
  subjectId: string,
  quarter: number,
  studentId: string,
  date: string,
  period: number,
): Promise<void> {
  await api.delete('/teacher/journal', {
    params: { classId, subjectId, quarter, studentId, date, period },
  })
}

export async function setTeacherNote(
  classId: string,
  subjectId: string,
  quarter: number,
  date: string,
  period: number,
  topic: string,
  homework: string,
  conducted: boolean,
): Promise<void> {
  await api.put('/teacher/journal/notes', {
    classId, subjectId, quarter, date, period, topic, homework, conducted,
  })
}

export async function getTeacherQuarterGrades(
  classId: string,
  subjectId: string,
  quarter: number,
): Promise<QuarterGradeRow[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<QuarterGradeRow[]>('/teacher/journal/quarter-grades', {
    params: { classId, subjectId, quarter },
  })
  return data
}

export async function setTeacherQuarterGrade(
  classId: string,
  subjectId: string,
  quarter: number,
  studentId: string,
  grade: number | null,
): Promise<void> {
  await api.put('/teacher/journal/quarter-grades', { classId, subjectId, quarter, studentId, grade })
}

/* ---------- Guruh chati ---------- */

export async function getTeacherChatClasses(): Promise<string[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<string[]>('/teacher/chat/classes')
  return data
}

export async function getTeacherChat(className: string, since?: string): Promise<ChatMessage[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<ChatMessage[]>(`/teacher/chat/${encodeURIComponent(className)}`, {
    params: since ? { since } : undefined,
  })
  return data
}

export async function sendTeacherChat(className: string, text: string): Promise<ChatMessage> {
  const { data } = await api.post<ChatMessage>(`/teacher/chat/${encodeURIComponent(className)}`, { text })
  return data
}

/**
 * Har bir kanal uchun oxirgi xabar vaqti — o'qilmagan xabarlarni aniqlash uchun.
 * Qaytadi: { [channelName]: ISO vaqt yoki null (xabari yo'q kanal) }
 */
export async function getTeacherLastMessages(): Promise<Record<string, string | null>> {
  if (USE_MOCK) return {}
  const { data } = await api.get<Record<string, string | null>>('/teacher/chat/last-messages')
  return data
}

/* ---------- LMS (Ta'lim) — faqat ko'rish + progress ---------- */

import type { LmsSubject, LmsTopic, LmsProgressReport } from '@/types'

/** O'qituvchi barcha sinflari yoki bitta sinf LMS fanlari */
export async function getTeacherLmsSubjects(classId?: string): Promise<LmsSubject[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<LmsSubject[]>('/teacher/lms/subjects', {
    params: classId ? { classId } : undefined,
  })
  return data
}

/** Fan mavzulari (to'liq kontent + har mavzuda completedCount) */
export async function getTeacherLmsTopics(subjectId: string): Promise<LmsTopic[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<LmsTopic[]>(`/teacher/lms/subjects/${subjectId}/topics`)
  return data
}

/** O'quvchilar × mavzular progress matritsasi */
export async function getTeacherLmsProgress(subjectId: string): Promise<LmsProgressReport> {
  if (USE_MOCK) return { topics: [], students: [] }
  const { data } = await api.get<LmsProgressReport>(`/teacher/lms/subjects/${subjectId}/progress`)
  return data
}
