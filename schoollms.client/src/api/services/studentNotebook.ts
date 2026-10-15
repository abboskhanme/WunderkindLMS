import type {
  Subject,
  AttendanceReasonCount,
  DisciplinePoint,
  EvaluationType,
} from '@/types'
import { api } from '../client'

/** O'quvchining bitta oydagi baholash turlari bo'yicha baholari */
export interface MonthlyEvaluation {
  month: string
  /** baholash turi id → baho (1-5) */
  grades: Record<string, number>
  avg: number
}

/** O'quvchining bitta fan bo'yicha oylik baholashlari (fan kesimida) */
export interface SubjectEvaluation {
  subjectId: string
  subjectName: string
  avg: number
  evaluations: MonthlyEvaluation[]
}

/** O'quvchining bitta chorakdagi uy vazifa/xulq jamlamasi */
export interface QuarterMarks {
  quarter: number
  /** + Bajardi (1) */
  homeworkDone: number
  /** * Umuman yo'q (2 — the old "qilmadi") */
  homeworkMissed: number
  behaviorGood: number
  behaviorBad: number
  /** − Chala (3) — homework-completion.md; older servers omit it */
  homeworkPartial?: number
  /** K Kech qoldi (4) */
  homeworkLate?: number
  /** Y Kelmadi (5, stored) */
  homeworkAbsent?: number
}

/** Bitta topshiriqdagi natija */
export interface AssignmentScoreItem {
  assignmentId: string
  subjectName: string
  title: string
  format: string
  maxScore: number
  score?: number | null
  completed: boolean
  dueDate?: string | null
  submittedAt?: string | null
}

export interface StudentAssignmentScores {
  count: number
  gradedCount: number
  totalScore: number
  totalMax: number
  items: AssignmentScoreItem[]
}

/** Davomat — har metrika chorak ("1".."4") → son */
export interface NotebookAttendance {
  missedDays: Record<string, number>
  illnessDays: Record<string, number>
  missedLessons: Record<string, number>
  illnessLessons: Record<string, number>
  lateCount: Record<string, number>
}

/** O'quvchi shaxsiy daftari — bitta o'quvchi haqida barcha ma'lumot */
export interface StudentNotebook {
  // Profil
  id: string
  fullName: string
  className: string
  homeroomTeacher: string
  parentFullName: string
  parentPhone: string
  gender: string
  birthDate: string
  enrollmentDate: string
  balance: number
  photoUrl?: string | null
  // Shaxsiy ma'lumotlar
  address: string
  subGroup: number
  parentPassportUrl?: string | null
  // O'zlashtirish
  subjects: Subject[]
  /** subjectId → chorak ("1".."4") → o'rtacha baho */
  grades: Record<string, Record<string, number>>
  avgGrade: number
  // Davomat
  attendance: NotebookAttendance
  conducted: number
  attended: number
  attendancePct: number
  reasons: AttendanceReasonCount[]
  // Intizom
  disciplineScore: number
  disciplinePlus: number
  disciplineMinus: number
  disciplinePoints: DisciplinePoint[]
  // Topshiriqlar
  assignments: StudentAssignmentScores
  // Oylik baholash — umumiy (fanlar o'rtachasi) + fan kesimida
  evaluationTypes: EvaluationType[]
  evaluations: MonthlyEvaluation[]
  evaluationsBySubject: SubjectEvaluation[]
  // Uy vazifa + xulq
  homeworkDone: number
  homeworkMissed: number
  behaviorGood: number
  behaviorBad: number
  marksTrend: QuarterMarks[]
  /** Statuses added 2026-10-15 (homework-completion.md): − Chala, K Kech qoldi, Y Kelmadi */
  homeworkPartial?: number
  homeworkLate?: number
  homeworkAbsent?: number
}

export async function getStudentNotebook(id: string): Promise<StudentNotebook> {
  const { data } = await api.get<StudentNotebook>(`/admin/students/${id}/profile`)
  return data
}
