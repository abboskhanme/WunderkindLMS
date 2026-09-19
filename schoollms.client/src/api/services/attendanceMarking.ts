import { api } from '../client'

/* ==========================================================================
   Kunlik davomat belgilash — mas'ul xodim uchun

   Mijoz, 2026-09-18: "bir kishi doimiy sinflar davomatini qiloladigan bo'lsin
   ... barcha sinflarni eng qulay usulda davomatini qilolsin."

   YO'QLIK JURNALGA YOZILADI — bu yerda yangi "davomat bazasi" yo'q. Server
   tomondagi sabab: `SchoolLms.Application/Services/DailyAttendanceService.cs`
   fayl boshidagi izoh.
   ========================================================================== */

/** Ro'yxatdagi bitta sinf. */
export interface DailyAttendanceClass {
  classId: string
  className: string
  studentCount: number
  /** Shu kunda jadval bo'yicha nechta dars bor. 0 — dam olish yoki jadvalsiz kun. */
  lessonCount: number
  /** Shulardan nechtasi belgilangan. */
  markedLessons: number
  absentCount: number
  lateCount: number
}

export interface DailyAttendanceOverview {
  date: string
  classes: DailyAttendanceClass[]
  /** Kunning BARCHA darsi belgilangan sinflar soni. */
  markedClasses: number
  totalClasses: number
  absentTotal: number
  lateTotal: number
  /** Shu kunda belgilangan dars soatlari. */
  markedLessons: number
  /** Shu kunda jadval bo'yicha o'tiladigan jami dars soati. */
  totalLessons: number
}

/** Bitta dars soati va undagi belgilar. */
export interface DailyAttendanceLesson {
  subjectId: string
  subjectName: string
  period: number
  /** Bo'linish: 0 — butun sinf, 1/2 — faqat shu guruh (til darsi kabi). */
  subGroup: number
  startTime: string | null
  endTime: string | null
  marked: boolean
  markedByName: string | null
  markedAt: string | null
  absentCount: number
  lateCount: number
  /** AYNAN shu darsda bo'ladigan o'quvchilar (bo'linish filtri qo'llangan). */
  studentIds: string[]
  /** O'quvchi id'si → davomat sababi. Ro'yxatda YO'Q o'quvchi — keldi. */
  marks: Record<string, string>
}

export interface DailyAttendanceStudent {
  studentId: string
  fullName: string
}

export interface DailyAttendanceClassDay {
  classId: string
  className: string
  date: string
  students: DailyAttendanceStudent[]
  lessons: DailyAttendanceLesson[]
  /** QIZIL tugma yozadigan sabab (katalogdan — server hal qiladi). */
  absentReasonId: string | null
  absentReasonName: string | null
  /** SARIQ tugma yozadigan sabab. */
  excusedReasonId: string | null
  excusedReasonName: string | null
}

export interface DailyAttendanceMarkInput {
  studentId: string
  reasonId: string | null
}

const BASE = '/admin/attendance/daily'

export async function getDailyOverview(date: string): Promise<DailyAttendanceOverview> {
  const { data } = await api.get<DailyAttendanceOverview>(`${BASE}/overview`, { params: { date } })
  return data
}

export async function getDailyClass(classId: string, date: string): Promise<DailyAttendanceClassDay> {
  const { data } = await api.get<DailyAttendanceClassDay>(`${BASE}/class`, {
    params: { classId, date },
  })
  return data
}

/**
 * Bitta DARS SOATINI saqlash: ro'yxatda BO'LMAGAN o'quvchi "keldi".
 * Javob — o'sha sinfning yangilangan kuni (barcha soatlari bilan).
 */
export async function saveDailyAttendance(
  classId: string,
  date: string,
  subjectId: string,
  period: number,
  subGroup: number,
  marks: DailyAttendanceMarkInput[],
): Promise<DailyAttendanceClassDay> {
  const { data } = await api.post<DailyAttendanceClassDay>(BASE, {
    classId,
    date,
    subjectId,
    period,
    subGroup,
    marks,
  })
  return data
}
