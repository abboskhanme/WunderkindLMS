import type { Student, Subject } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { studentsMock } from '../mock/students'
import { subjectsMock } from '../mock/subjects'
import { classesMock } from '../mock/classes'
import { templatesMock } from '../mock/scheduleTemplates'

export interface ClassStudentRow {
  student: Student
  /** subjectId -> o'rtacha baho */
  grades: Record<string, number>
  /** Umumiy o'rtacha baho */
  average: number
  /** Davomat foizi (0-100); o'tilgan dars bo'lmasa null */
  attendance: number | null
}

export interface ClassPerformanceData {
  /** Sinfda o'qitiladigan fanlar (ustunlar) */
  subjects: Subject[]
  rows: ClassStudentRow[]
}

// --- Mock uchun deterministik generatsiya ---
function hash(str: string): number {
  let h = 2166136261
  for (let i = 0; i < str.length; i++) {
    h ^= str.charCodeAt(i)
    h = Math.imul(h, 16777619)
  }
  return h >>> 0
}

function gradeFor(studentId: string, subjectId: string): number {
  const v = 3 + (hash(`${studentId}-${subjectId}`) % 21) / 10 // 3.0 .. 5.0
  return Math.round(v * 10) / 10
}

function attendanceFor(studentId: string): number {
  return 85 + (hash(`${studentId}-att`) % 16) // 85 .. 100
}

function buildMock(classId: string): ClassPerformanceData {
  const cls = classesMock.find((c) => c.id === classId)
  if (!cls) return { subjects: [], rows: [] }

  const students = studentsMock.filter((s) => s.className === cls.name)

  // Sinf fanlari — barcha jadval variantlaridan; bo'sh bo'lsa, barcha fanlar
  const allLessons = (templatesMock[classId] ?? []).flatMap((t) => t.lessons)
  const fromSchedule = [...new Set(allLessons.map((l) => l.subjectId))]
  const subjectIds = fromSchedule.length ? fromSchedule : subjectsMock.map((s) => s.id)
  const subjects = subjectIds
    .map((id) => subjectsMock.find((s) => s.id === id))
    .filter((s): s is Subject => Boolean(s))

  const rows: ClassStudentRow[] = students.map((student) => {
    const grades: Record<string, number> = {}
    let sum = 0
    subjects.forEach((sub) => {
      const g = gradeFor(student.id, sub.id)
      grades[sub.id] = g
      sum += g
    })
    const average = subjects.length ? Math.round((sum / subjects.length) * 10) / 10 : 0
    return { student, grades, average, attendance: attendanceFor(student.id) }
  })

  return { subjects, rows }
}

export async function getClassPerformance(classId: string): Promise<ClassPerformanceData> {
  if (USE_MOCK) {
    await delay()
    return buildMock(classId)
  }
  const { data } = await api.get<ClassPerformanceData>(`/admin/classes/${classId}/performance`)
  return data
}

export interface ClassStats {
  studentsCount: number
  /** O'rtacha baho (reyting) */
  averageGrade: number
  /** O'rtacha davomat foizi; o'tilgan dars bo'lmasa null */
  attendance: number | null
}

export interface StudentRatingRow {
  student: Student
  className: string
  /** Sinf darajasi */
  grade: number
  average: number
  /** Davomat foizi; o'tilgan dars bo'lmasa null */
  attendance: number | null
}

/** Butun maktab o'quvchilari reytingi */
export async function getStudentsRating(): Promise<StudentRatingRow[]> {
  if (USE_MOCK) {
    await delay()
    const rows: StudentRatingRow[] = []
    classesMock.forEach((c) => {
      buildMock(c.id).rows.forEach((r) =>
        rows.push({
          student: r.student,
          className: c.name,
          grade: c.grade,
          average: r.average,
          attendance: r.attendance,
        }),
      )
    })
    return rows
  }
  const { data } = await api.get<StudentRatingRow[]>('/admin/students/rating')
  return data
}

/** Barcha sinflar bo'yicha umumiy ko'rsatkichlar (classId -> stats) */
export async function getClassesStats(): Promise<Record<string, ClassStats>> {
  if (USE_MOCK) {
    await delay()
    const result: Record<string, ClassStats> = {}
    classesMock.forEach((c) => {
      const { rows } = buildMock(c.id)
      const n = rows.length
      result[c.id] = {
        studentsCount: n,
        averageGrade: n
          ? Math.round((rows.reduce((a, r) => a + r.average, 0) / n) * 10) / 10
          : 0,
        attendance: n ? Math.round(rows.reduce((a, r) => a + (r.attendance ?? 0), 0) / n) : 0,
      }
    })
    return result
  }
  const { data } = await api.get<Record<string, ClassStats>>('/admin/classes/stats')
  return data
}
