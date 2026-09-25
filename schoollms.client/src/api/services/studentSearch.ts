import { api, USE_MOCK } from '../client'

/* =========================================================================
 *  O'quvchilar ro'yxati — server tarafdagi filtr, tartib, sahifa, eksport
 *  va ommaviy o'chirish (docs/modules/students-parity.md §2.3, §2.4).
 *
 *  ESKI YO'L TEGILMAGAN: `getStudents` / `getArchivedStudents` (students.ts)
 *  o'z joyida qoladi va boshqa ekranlar o'shandan o'qiydi. Bu yerdagi
 *  `searchStudents` — QO'SHIMCHA yo'l, faqat ro'yxat ekrani uchun.
 *
 *  DTO nomlari backend bilan AYNAN bir xil (camelCase) — `StudentListRowDto`,
 *  `StudentListPageDto`, `StudentListFilter`.
 * ========================================================================= */

/** Ro'yxat qatori — §2.3.1 dagi ustunlar. */
export interface StudentListRow {
  id: string
  fullName: string
  className: string
  /** Sinf darajasi (0–11). Sinf topilmasa 0. */
  grade: number
  /** Sinfi hali yo'q o'quvchining mo'ljaldagi sinf darajasi (S-9). Sinfi bor bo'lsa null. */
  targetGrade: number | null
  gender: 'male' | 'female'
  birthDate: string
  /** To'liq yosh. Sana bo'sh yoki buzuq bo'lsa null. */
  age: number | null
  address: string
  phone: string | null
  parentFullName: string
  parentPhone: string
  language: string | null
  enrollmentDate: string
  /** Manfiy = qarz, musbat = avans. */
  balance: number
  statusId: string | null
  statusName: string | null
  statusColor: string | null
  hasContract: boolean
  contractNumber: string | null
  isArchived: boolean
  archivedAt: string | null
  archiveReason: string | null
  archiveReasonId: string | null
  photoUrl: string | null
}

/** Bitta sahifa va butun filtrlangan to'plamning yakunlari. */
export interface StudentListPage {
  items: StudentListRow[]
  total: number
  page: number
  pageSize: number
  /** Jami qarz (musbat son). */
  totalDebt: number
  /** Jami avans (musbat son). */
  totalCredit: number
}

/** Ro'yxat filtri. BARCHASI ixtiyoriy — bo'sh filtr bugungi ro'yxatning aynan o'zi. */
export type StudentPlacement = 'inClass' | 'unassigned' | 'waiting' | 'leftFromClass'

export interface StudentListFilter {
  state?: 'active' | 'archived' | 'all'
  search?: string
  className?: string
  /** Sinf darajalari, masalan [5, 9]. */
  grades?: number[]
  gender?: 'male' | 'female'
  language?: string
  statusId?: string
  hasStatus?: boolean
  groupId?: string
  hasContract?: boolean
  hasSubscription?: boolean
  categoryId?: string
  hasDiscount?: boolean
  certificateTypeIds?: string[]
  certificateTeacherId?: string
  enrolledFrom?: string
  enrolledTo?: string
  archivedFrom?: string
  archivedTo?: string
  archiveReasonId?: string
  ageFrom?: number
  ageTo?: number
  balanceState?: 'debt' | 'paid' | 'credit'
  /** Bosh sahifa kartalari bilan bir xil to'plamlar (server `Placement`). */
  placement?: StudentPlacement
  /** `ever` — kamida bitta to'lov qilganlar, `thisMonth` — birinchi to'lovi shu oyda. */
  firstPayment?: 'ever' | 'thisMonth'
  minDebt?: number
  balanceFrom?: number
  balanceTo?: number
  sortBy?: StudentSortKey
  sortOrder?: 'asc' | 'desc'
  page?: number
  pageSize?: number
}

export type StudentSortKey =
  | 'fullName'
  | 'className'
  | 'balance'
  | 'birthDate'
  | 'enrollmentDate'
  | 'status'
  | 'archivedAt'
  | 'parentFullName'

/** O'chirilmagan o'quvchi va sababi. */
export interface StudentDeleteBlocked {
  studentId: string
  fullName: string
  reason: string
}

/** Ommaviy o'chirish natijasi — hammasi yoki hech nima. */
export interface StudentDeleteResult {
  deleted: number
  blocked: StudentDeleteBlocked[]
  message?: string | null
}

const EMPTY_PAGE: StudentListPage = {
  items: [],
  total: 0,
  page: 1,
  pageSize: 0,
  totalDebt: 0,
  totalCredit: 0,
}

/**
 * Filtrni query string'ga aylantiradi. Bo'sh, `undefined` va bo'sh massiv
 * qiymatlar UMUMAN yuborilmaydi — shunda "filtrsiz" so'rov haqiqatan
 * parametrsiz ketadi.
 */
export function studentFilterParams(filter: StudentListFilter): URLSearchParams {
  const params = new URLSearchParams()
  const put = (key: string, value: unknown) => {
    if (value === undefined || value === null || value === '') return
    if (Array.isArray(value)) {
      if (value.length === 0) return
      params.set(key, value.join(','))
      return
    }
    params.set(key, String(value))
  }

  put('state', filter.state)
  put('search', filter.search?.trim())
  put('className', filter.className)
  put('grades', filter.grades)
  put('gender', filter.gender)
  put('language', filter.language)
  put('statusId', filter.statusId)
  put('hasStatus', filter.hasStatus)
  put('groupId', filter.groupId)
  put('hasContract', filter.hasContract)
  put('hasSubscription', filter.hasSubscription)
  put('categoryId', filter.categoryId)
  put('hasDiscount', filter.hasDiscount)
  // Ilgari bu ikkisi so'rovga tushmasdi — panel va bosh sahifa kartalari tanlagan
  // "sinfli / sinfsiz" va "birinchi to'lov" filtrlari jimgina e'tiborsiz qolardi.
  put('placement', filter.placement)
  put('firstPayment', filter.firstPayment)
  put('certificateTypeIds', filter.certificateTypeIds)
  put('certificateTeacherId', filter.certificateTeacherId)
  put('enrolledFrom', filter.enrolledFrom)
  put('enrolledTo', filter.enrolledTo)
  put('archivedFrom', filter.archivedFrom)
  put('archivedTo', filter.archivedTo)
  put('archiveReasonId', filter.archiveReasonId)
  put('ageFrom', filter.ageFrom)
  put('ageTo', filter.ageTo)
  put('balanceState', filter.balanceState)
  put('minDebt', filter.minDebt)
  put('balanceFrom', filter.balanceFrom)
  put('balanceTo', filter.balanceTo)
  put('sortBy', filter.sortBy)
  put('sortOrder', filter.sortOrder)
  put('page', filter.page)
  put('pageSize', filter.pageSize)
  return params
}

export async function searchStudents(filter: StudentListFilter): Promise<StudentListPage> {
  if (USE_MOCK) return EMPTY_PAGE
  const { data } = await api.get<StudentListPage>(
    `/admin/students/search?${studentFilterParams(filter)}`,
  )
  return data
}

/** Filtrlangan ro'yxatning .xlsx eksporti — tanlangan qatorlarniki EMAS. */
export async function exportStudents(filter: StudentListFilter): Promise<void> {
  if (USE_MOCK) {
    alert('Eksport faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get(`/admin/students/search/export?${studentFilterParams(filter)}`, {
    responseType: 'blob',
  })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^"]+)"?/)
  a.download = m?.[1] ?? `oquvchilar_${new Date().toISOString().slice(0, 10)}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/**
 * Tanlanganlarni BUTUNLAY o'chirish (arxiv tab'i). Moliyaviy yozuvi bor
 * bitta o'quvchi ham topilsa server 400 qaytaradi va HECH KIM o'chmaydi —
 * javob tanasida kim to'sgani ko'rinadi.
 */
export async function deleteStudentsMany(studentIds: string[]): Promise<StudentDeleteResult> {
  const { data } = await api.post<StudentDeleteResult>('/admin/students/delete-many', { studentIds })
  return data
}
