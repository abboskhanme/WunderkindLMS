import { api, USE_MOCK } from '../client'

/**
 * Davomat analitikasi (Analitika #5) — zavuch ekrani.
 *
 * Shakl backenddagi `AttendanceAnalyticsDto` ning aynan nusxasi (camelCase).
 * FOIZLAR SERVERDAN keladi: brauzerda qayta hisoblanmaydi, aks holda ikki joyda
 * ikki xil ta'rif paydo bo'lardi.
 */

/** Bitta kesim bo'yicha yig'indi. `present + absent + unchecked = opportunities`. */
export interface AttendanceTally {
  /** Kesimga tushgan o'tilgan dars kataklari soni */
  lessons: number
  /** O'tilgan dars × shu darsga tegishli o'quvchi (maxraj) */
  opportunities: number
  /** Belgilangan va yo'qlik sababi qo'yilmagan (kech kelganlar ham shu yerda) */
  present: number
  /** Sababli + sababsiz yo'qliklar ("kech keldi" bunga KIRMAYDI) */
  absent: number
  excused: number
  unexcused: number
  /** Kech kelganlar — `present` ICHIDAN */
  late: number
  /** Dars o'tilgan, lekin davomat umuman belgilanmagan */
  unchecked: number
  presentPct: number | null
  absentPct: number | null
  uncheckedPct: number | null
}

export interface AttendanceClassRow {
  classId: string
  className: string
  grade: number
  students: number
  tally: AttendanceTally
  /** 'group' — yo'nalish guruhi qatori (9–11-sinflar o'rnida) */
  ownerKind?: 'class' | 'group'
}

export interface AttendancePeriodRow {
  period: number
  startTime: string | null
  endTime: string | null
  tally: AttendanceTally
}

export interface AttendanceTrendPoint {
  date: string
  tally: AttendanceTally
}

export interface AttendanceReasonRow {
  reasonId: string
  name: string
  short: string
  isLate: boolean
  /** Shu sabab sababsiz yo'qlik deb hisoblanadimi (serverdagi ta'rif) */
  unexcused: boolean
  count: number
}

export interface AttendanceAnalytics {
  from: string
  to: string
  classId: string | null
  /** Dars soatlari kesimi ko'rsatilayotgan kun */
  day: string
  studentsTotal: number
  total: AttendanceTally
  classes: AttendanceClassRow[]
  periods: AttendancePeriodRow[]
  trend: AttendanceTrendPoint[]
  reasons: AttendanceReasonRow[]
}

const emptyTally: AttendanceTally = {
  lessons: 0,
  opportunities: 0,
  present: 0,
  absent: 0,
  excused: 0,
  unexcused: 0,
  late: 0,
  unchecked: 0,
  presentPct: null,
  absentPct: null,
  uncheckedPct: null,
}

export interface AttendanceAnalyticsParams {
  /** Bo'sh bo'lsa — butun maktab */
  classId?: string
  from: string
  to: string
  /** Dars soatlari kesimi uchun kun; berilmasa — davrning oxirgi kuni */
  day?: string
}

/** Davomat roll-up'i: davr, sinflar, dars soatlari, trend va sabablar. */
export async function getAttendanceAnalytics(
  params: AttendanceAnalyticsParams,
): Promise<AttendanceAnalytics> {
  if (USE_MOCK) {
    return {
      from: params.from,
      to: params.to,
      classId: params.classId ?? null,
      day: params.day ?? params.to,
      studentsTotal: 0,
      total: emptyTally,
      classes: [],
      periods: [],
      trend: [],
      reasons: [],
    }
  }
  const { data } = await api.get<AttendanceAnalytics>('/admin/attendance/analytics', {
    params: {
      classId: params.classId || undefined,
      from: params.from,
      to: params.to,
      day: params.day || undefined,
    },
  })
  return data
}
