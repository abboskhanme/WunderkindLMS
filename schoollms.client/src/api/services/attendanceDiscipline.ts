/**
 * Davomat intizomi bo'yicha hisobot (§4, #6) — FAQAT O'QISH.
 *
 * Tiplar `SchoolLms.Application/Dtos/AnalyticsReportDtos.cs` dagi DTO'larning aynan
 * nusxasi (camelCase). Foiz va yig'indilar serverda hisoblanadi.
 *
 * Ikki ta'rif farqi (serverdagi izohning qisqasi):
 *  - `absences` / `lates` / `unchecked` — faqat O'TILGAN darslar bo'yicha; belgilanmagan
 *    katak hech qachon "keldi" emas;
 *  - `attendancePoints` — Ballar nazorati bilan bir xil qoida: sababi balli bo'lgan HAR
 *    belgi ballga ta'sir qiladi.
 */
import { api, USE_MOCK } from '../client'

export interface AttendanceDisciplineStudent {
  studentId: string
  fullName: string
  className: string
  /** Shu davrda o'quvchiga tegishli o'tilgan darslar. */
  opportunities: number
  absences: number
  lates: number
  /** Davomati umuman belgilanmagan darslar. */
  unchecked: number
  attendancePoints: number
  manualPoints: number
  /** Qoldi — 100 + BUTUN tarix bo'yicha ballar (davr filtri ta'sir qilmaydi). */
  remaining: number
  attendancePercent: number | null
}

export interface AttendanceDisciplineClass {
  classId: string
  className: string
  students: number
  opportunities: number
  absences: number
  lates: number
  unchecked: number
  attendancePoints: number
  attendancePercent: number | null
  /** Bitta o'quvchiga to'g'ri keladigan o'rtacha jazo balli (musbat son). */
  penaltyPerStudent: number
}

/** `kind`: `attendance` — jurnal davomati sababi, `manual` — qo'lda kiritilgan ball. */
export interface AttendanceDisciplineReason {
  reasonId: string
  name: string
  kind: 'attendance' | 'manual'
  pointsEach: number
  count: number
  totalPoints: number
}

export interface AttendanceDisciplineTotals {
  /** Hisobot qamragan BARCHA faol o'quvchilar (jadvaldagi qatorlardan ko'p bo'lishi mumkin). */
  students: number
  opportunities: number
  absences: number
  lates: number
  unchecked: number
  attendancePoints: number
  manualPoints: number
  attendancePercent: number | null
}

export interface AttendanceDisciplineReport {
  from: string
  to: string
  totals: AttendanceDisciplineTotals
  classes: AttendanceDisciplineClass[]
  /** Faqat belgisi (yoki qo'lda balli) bo'lgan o'quvchilar. */
  students: AttendanceDisciplineStudent[]
  reasons: AttendanceDisciplineReason[]
}

const EMPTY: AttendanceDisciplineReport = {
  from: '',
  to: '',
  totals: {
    students: 0,
    opportunities: 0,
    absences: 0,
    lates: 0,
    unchecked: 0,
    attendancePoints: 0,
    manualPoints: 0,
    attendancePercent: null,
  },
  classes: [],
  students: [],
  reasons: [],
}

/**
 * @param from "YYYY-MM-DD" (shu kun kiradi)
 * @param to "YYYY-MM-DD" (shu kun kiradi)
 * @param classId Bitta sinf bo'yicha filtr; bo'sh = barcha sinflar.
 */
export async function getAttendanceDisciplineReport(
  from: string,
  to: string,
  classId?: string,
): Promise<AttendanceDisciplineReport> {
  if (USE_MOCK) return EMPTY
  const { data } = await api.get<AttendanceDisciplineReport>(
    '/admin/attendance-discipline-report',
    { params: { from, to, classId: classId || undefined } },
  )
  return data
}
