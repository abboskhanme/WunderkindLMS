import type { Subject } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

/** O'zlashtirish hisobotining bitta qatori (sinf / parallel / bosqich / maktab) */
export interface GradesProgressRow {
  kind: 'class' | 'parallel' | 'level' | 'school'
  label: string
  language: string
  total: number
  /** false bo'lsa kategoriya kataklari bo'sh ko'rsatiladi */
  showCategories: boolean
  excellentCount: number
  excellentPct: number
  /** A'lochilar FISH ro'yxati (\n bilan ajratilgan) */
  excellentNames: string
  goodCount: number
  goodPct: number
  satisfactoryCount: number
  satisfactoryPct: number
  poorCount: number
  poorPct: number
  /** O'zlashtirmaydiganlar FISH ro'yxati (\n bilan ajratilgan) */
  poorNames: string
  avgRating: number
  qualityPct: number
  otmPct: number
}

export interface GradesProgressReport {
  totalStudents: number
  noGradesCount: number
  rows: GradesProgressRow[]
}

/** Maktab bo'yicha o'zlashtirish hisoboti — tanlangan sinflar va choraklar (birlashtirilgan) */
export async function getSchoolGradesReport(
  classIds: string[],
  quarters: number[],
): Promise<GradesProgressReport> {
  if (USE_MOCK) {
    await delay()
    return { totalStudents: 0, noGradesCount: 0, rows: [] }
  }
  const { data } = await api.get<GradesProgressReport>('/admin/grades-report/school', {
    params: { classIds: classIds.join(','), quarters: quarters.join(',') },
  })
  return data
}

/* ---------- O'zlashtirish (fanlar bo'yicha) ---------- */

/** Pivotning bitta katagi: sinf × fan, tanlangan choraklar bo'yicha */
export interface SubjectAttainmentCell {
  subjectId: string
  /** Katakka tushgan (o'quvchi × chorak) baho qiymatlari soni */
  values: number
  /** O'rtacha baho; qiymat bo'lmasa null (nol EMAS: "baho yo'q" ≠ "0") */
  average: number | null
  /** Sifat ko'rsatkichi — 4 va 5 baholarning ulushi (%) */
  qualityPct: number
  /** Chorak → o'rtacha baho (faqat qiymati bor choraklar) */
  byQuarter: Record<string, number>
}

/**
 * Pivotning bitta qatori. `cells` hisobotdagi `subjects` ro'yxati bilan bir xil tartibda
 * va bir xil uzunlikda — sinf o'tmaydigan fan katagi ham bor (`values = 0`).
 */
export interface SubjectAttainmentRow {
  kind: 'class' | 'school'
  classId: string
  className: string
  grade: number
  students: number
  cells: SubjectAttainmentCell[]
  average: number | null
  qualityPct: number
}

export interface SubjectAttainmentReport {
  quarters: number[]
  subjects: Subject[]
  rows: SubjectAttainmentRow[]
  school: SubjectAttainmentRow
}

/**
 * O'zlashtirish (fanlar bo'yicha) — sinf × fan pivoti, chorak kesimi bilan.
 * O'rtacha va foizlar SERVERDA hisoblanadi.
 */
export async function getSubjectAttainment(
  classIds: string[],
  quarters: number[],
): Promise<SubjectAttainmentReport> {
  const empty: SubjectAttainmentReport = {
    quarters: [],
    subjects: [],
    rows: [],
    school: {
      kind: 'school',
      classId: '',
      className: 'Maktab',
      grade: 0,
      students: 0,
      cells: [],
      average: null,
      qualityPct: 0,
    },
  }
  if (USE_MOCK) {
    await delay()
    return empty
  }
  const { data } = await api.get<SubjectAttainmentReport>('/admin/grades-report/subjects', {
    params: { classIds: classIds.join(','), quarters: quarters.join(',') },
  })
  return data
}

/* ---------- Sinf bo'yicha hisobot ---------- */

export interface ClassReportStudent {
  id: string
  fullName: string
  /** subjectId -> chorak ("1".."4") -> o'rtacha baho */
  averages: Record<string, Record<string, number>>
}

export interface ClassReport {
  classId: string
  className: string
  grade: number
  language: string
  homeroomTeacher: string
  subjects: Subject[]
  students: ClassReportStudent[]
}

/** Sinf bo'yicha hisobot uchun xom ma'lumot (o'quvchilar × fanlar × choraklar o'rtacha baholari) */
export async function getClassReport(classId: string): Promise<ClassReport> {
  if (USE_MOCK) {
    await delay()
    return {
      classId,
      className: '',
      grade: 0,
      language: '',
      homeroomTeacher: '',
      subjects: [],
      students: [],
    }
  }
  const { data } = await api.get<ClassReport>('/admin/grades-report/class', { params: { classId } })
  return data
}

/* ---------- O'quvchi bo'yicha hisobot ---------- */

export interface StudentAttendance {
  missedDays: Record<string, number>
  illnessDays: Record<string, number>
  missedLessons: Record<string, number>
  illnessLessons: Record<string, number>
  lateCount: Record<string, number>
}

export interface StudentReport {
  studentId: string
  fullName: string
  className: string
  homeroomTeacher: string
  parentFullName: string
  subjects: Subject[]
  /** subjectId -> chorak ("1".."4") -> o'rtacha baho */
  grades: Record<string, Record<string, number>>
  attendance: StudentAttendance
}

/** Bitta o'quvchining o'zlashtirish va qatnashish hisoboti */
export async function getStudentProgressReport(studentId: string): Promise<StudentReport> {
  if (USE_MOCK) {
    await delay()
    return {
      studentId,
      fullName: '',
      className: '',
      homeroomTeacher: '',
      parentFullName: '',
      subjects: [],
      grades: {},
      attendance: {
        missedDays: {},
        illnessDays: {},
        missedLessons: {},
        illnessLessons: {},
        lateCount: {},
      },
    }
  }
  const { data } = await api.get<StudentReport>('/admin/grades-report/student', {
    params: { studentId },
  })
  return data
}
