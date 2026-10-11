import { api, USE_MOCK } from '../client'

/**
 * Davomat analitikasi (Analitika #5) — zavuch ekrani.
 *
 * Shakl backenddagi `AttendanceAnalyticsDto` ning aynan nusxasi (camelCase).
 * FOIZLAR SERVERDAN keladi: brauzerda qayta hisoblanmaydi, aks holda ikki joyda
 * ikki xil ta'rif paydo bo'lardi.
 */

/** Bitta kesim bo'yicha yig'indi. `present + absent + unchecked = opportunities`. */
/**
 * Totals, classes and the trend count STUDENT-DAYS (client, 2026-10-07: "bitta o'quvchi bir kishiga"):
 * a student absent all day is one "kelmadi", absent in some lessons — "keldi" (and `partial`). The
 * period cut (one lesson) counts one student per lesson as before.
 */
export interface AttendanceTally {
  /** Kesimga tushgan o'tilgan dars kataklari soni */
  lessons: number
  /** O'quvchi × kun (dars soatlari kesimida — o'quvchi × dars) */
  opportunities: number
  /** Kelgan o'quvchi-kunlar (kech kelganlar va qisman qoldirganlar ham shu yerda) */
  present: number
  /** Kun bo'yi kelmaganlar — sababli + sababsiz ("kech keldi" bunga KIRMAYDI) */
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
  /** Kelgan, lekin ayrim darslarga kelmagan o'quvchi-kunlar — `present` ICHIDAN (2026-10-07) */
  partial?: number
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
  /** Har bir o'quvchi davr bo'yicha (o'quvchi-kun birligida), 2026-10-09. */
  students?: AttendanceStudentRow[]
  /** "Kim, qaysi kuni, nima sababdan" — oddiy "keldi" bo'lmagan har bir o'quvchi-kun. */
  events?: AttendanceEvent[]
  /** Ro'yxat 5000 qatorda kesilgan (butun maktab, uzun davr) — davrni toraytirish kerak. */
  eventsTruncated?: boolean
  /** Dars soatlari kesimi butun davr bo'yicha (bosh sahifadan ko'chgan jadval, 2026-10-11). */
  periodsRange?: AttendancePeriodRow[]
}

export interface AttendanceStudentRow {
  studentId: string
  fullName: string
  rowId: string
  /** Sinf (yoki yo'nalish guruhi) nomi */
  rowName: string
  /** `opportunities` — o'quvchining maktab kunlari */
  tally: AttendanceTally
  /** Darsi bo'lgan kunlar ("YYYY-MM-DD") — faqat bitta sinf tanlanganda (oylik jadval uchun). */
  dates?: string[] | null
  /** Davrdagi oxirgi kelgan kuni; davrda kelmagan bo'lsa — null. */
  lastPresent?: string | null
}

/** `absent` — kun bo'yi kelmadi, `partial` — ayrim darslarga, `late` — kech keldi, `unchecked` — belgilanmagan. */
export type AttendanceEventStatus = 'absent' | 'partial' | 'late' | 'unchecked'

export interface AttendanceEvent {
  /** "YYYY-MM-DD" */
  date: string
  studentId: string
  fullName: string
  rowId: string
  rowName: string
  status: AttendanceEventStatus
  /** Sabablar (asosiysi birinchi) — `reasons` ro'yxatidagi id'lar */
  reasonIds: string[]
  /** Asosiy sabab sababsiz deb hisoblanadimi */
  unexcused: boolean
  /** Qoldirilgan (kech kelganda — kechikkan) dars raqamlari */
  periods: number[]
  /** O'sha kuni o'tilgan darslar soni */
  heldLessons: number
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

/**
 * Davomat analitikasi Excel'da: dars qoldirganlar, har bir kun sababi bilan, sabablar va dars
 * soatlari (2026-10-11). Filtrlar ekrandagining o'zi.
 */
export async function downloadAttendanceAnalytics(params: { classId?: string; from: string; to: string }): Promise<void> {
  const res = await api.get('/admin/attendance/analytics/export', {
    params: { classId: params.classId || undefined, from: params.from, to: params.to },
    responseType: 'blob',
  })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^";]+)"?/)
  a.download = m?.[1] ?? `davomat_${params.from}_${params.to}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
