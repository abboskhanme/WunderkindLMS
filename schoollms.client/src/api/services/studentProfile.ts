import type { AttendanceReasonCount, AuditLog, Student } from '@/types'
import { api } from '../client'

/* =========================================================================
 *  O'quvchi kartochkasi — docs/modules/students-parity.md §2.3 (S-10, S-12),
 *  §2.8 (L-1).
 *
 *  Tiplar `SchoolLms.Application/Dtos/StudentProfileDtos.cs` dan AYNAN
 *  ko'chirilgan (C# PascalCase -> JSON camelCase). Sanalar — matn
 *  ("yyyy-MM-dd"), chunki <input type="date"> ham shu formatda ishlaydi.
 *
 *  BU YERDA PUL YO'Q. Kartochkadagi balans va hisob-kitob `FinanceView`
 *  orqali, moliya rolining orqasida keladi — shuning uchun `student.balance`
 *  bu javobda har doim bo'sh.
 * ========================================================================= */

/** Kartochkaning chap paneli. */
export interface StudentCard {
  /** Ro'yxatdagi bilan bir xil shakl — tahrirlash va arxivlash oynalari uchun. */
  student: Student
  /** O'quvchining O'Z telefoni (ota-onanikidan alohida). */
  phone: string | null
  /** O'qish tili: uz | ru | en | kaa. */
  language: string | null
  /** Hujjat skani (metrika/pasport) — profil suratidan alohida. */
  documentUrl: string | null
  statusId: string | null
  statusName: string | null
  /** `#RRGGBB` yoki null. */
  statusColor: string | null
  /** Tizimga kirish logini (parol bu yerda qaytmaydi). */
  login: string | null
  homeroomTeacher: string
  latitude: number | null
  longitude: number | null
  locationAddress: string | null
  locationUpdatedAt: string | null
  /** Maktabda necha kun — qabul sanasidan bugungacha (arxivda arxiv sanasigacha). */
  activeDays: number
}

/** Jadvaldagi bitta dars. `ownerName` — guruh darsida guruh nomi. */
export interface StudentLesson {
  /** 0 = Dushanba ... 5 = Shanba */
  day: number
  period: number
  startTime: string | null
  endTime: string | null
  subjectId: string
  subjectName: string
  teacherId: string
  teacherName: string
  subGroup: number
  /** "class" | "group" */
  ownerKind: string
  ownerName: string | null
}

export interface StudentTimetable {
  quarter: number
  week: number
  /** Chorakdagi haftalar soni (0 = chorak belgilanmagan). */
  weekCount: number
  weekStart: string | null
  weekEnd: string | null
  lessons: StudentLesson[]
}

export interface StudentAttendanceSubject {
  subjectId: string
  subjectName: string
  planned: number
  attended: number
  absent: number
  late: number
  pct: number
}

/** Bitta kun — oy kalendari uchun. */
export interface StudentAttendanceDay {
  date: string
  planned: number
  absent: number
  late: number
}

export interface StudentAttendanceRange {
  from: string
  to: string
  planned: number
  attended: number
  absent: number
  late: number
  pct: number
  subjects: StudentAttendanceSubject[]
  reasons: AttendanceReasonCount[]
  /** Faqat dars o'tilgan kunlar. */
  days: StudentAttendanceDay[]
}

export interface SaveStudentLocationInput {
  latitude: number | null
  longitude: number | null
  address: string | null
}

export async function getStudentCard(id: string): Promise<StudentCard> {
  const { data } = await api.get<StudentCard>(`/admin/students/${id}/card`)
  return data
}

/** Chorak/hafta berilmasa — server joriy haftani o'zi tanlaydi. */
export async function getStudentTimetable(
  id: string,
  quarter?: number,
  week?: number,
): Promise<StudentTimetable> {
  const { data } = await api.get<StudentTimetable>(`/admin/students/${id}/timetable`, {
    params: { quarter, week },
  })
  return data
}

/** Oraliq berilmasa — oxirgi 30 kun. */
export async function getStudentAttendanceRange(
  id: string,
  from?: string,
  to?: string,
): Promise<StudentAttendanceRange> {
  const { data } = await api.get<StudentAttendanceRange>(
    `/admin/students/${id}/attendance-range`,
    { params: { from, to } },
  )
  return data
}

/**
 * Faoliyat tarixi. Moliya huquqi bo'lmagan foydalanuvchiga moliyaviy yozuvlar
 * va pul maydonlari SERVERDA kesib tashlanadi — bu yerda filtr yo'q.
 */
export async function getStudentActivity(id: string, limit = 100): Promise<AuditLog[]> {
  const { data } = await api.get<AuditLog[]>(`/admin/students/${id}/activity`, {
    params: { limit },
  })
  return data
}

/** Uy joylashuvini saqlaydi; hammasi null bo'lsa — tozalaydi. */
export async function saveStudentLocation(
  id: string,
  input: SaveStudentLocationInput,
): Promise<StudentCard> {
  const { data } = await api.put<StudentCard>(`/admin/students/${id}/location`, input)
  return data
}
