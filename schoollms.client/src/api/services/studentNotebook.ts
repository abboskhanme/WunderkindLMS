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
  homeworkDone: number
  homeworkMissed: number
  behaviorGood: number
  behaviorBad: number
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
  discountPct: number
  discountAmount: number
  discountNote: string
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
}

export async function getStudentNotebook(id: string): Promise<StudentNotebook> {
  const { data } = await api.get<StudentNotebook>(`/admin/students/${id}/profile`)
  return data
}
