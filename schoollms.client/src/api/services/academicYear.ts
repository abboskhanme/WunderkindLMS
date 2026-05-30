import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

export interface AcademicYearInfo {
  currentYear: string
  students: number
  classes: number
  journalEntries: number
  weekAssignments: number
  financeTransactions: number
}

export interface YearArchive {
  id: string
  year: string
  createdAt: string
  studentsCount: number
  classesCount: number
  journalCount: number
  financeCount: number
}

export interface RolloverPayload {
  newYear: string
  promoteStudents: boolean
  clearGrades: boolean
  clearSchedule: boolean
  clearQuarters: boolean
  clearFinance: boolean
}

export interface RolloverResult {
  oldYear: string
  newYear: string
  promoted: number
  graduated: number
}

export async function getAcademicYearInfo(): Promise<AcademicYearInfo> {
  if (USE_MOCK) {
    await delay()
    return {
      currentYear: '',
      students: 0,
      classes: 0,
      journalEntries: 0,
      weekAssignments: 0,
      financeTransactions: 0,
    }
  }
  const { data } = await api.get<AcademicYearInfo>('/admin/academic-year')
  return data
}

export async function getYearArchives(): Promise<YearArchive[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<YearArchive[]>('/admin/academic-year/archives')
  return data
}

/** Bir o'quv yiliga tegishli BARCHA ma'lumotni ZIP (papkalarga ajratilgan CSV + JSON) yuklab olish */
export async function downloadArchiveZip(id: string): Promise<Blob> {
  if (USE_MOCK) {
    await delay()
    return new Blob([], { type: 'application/zip' })
  }
  const { data } = await api.get(`/admin/academic-year/archives/${id}/download`, {
    responseType: 'blob',
  })
  return data as Blob
}

export async function rolloverYear(payload: RolloverPayload): Promise<RolloverResult> {
  if (USE_MOCK) {
    await delay(300)
    return { oldYear: '', newYear: payload.newYear, promoted: 0, graduated: 0 }
  }
  const { data } = await api.post<RolloverResult>('/admin/academic-year/rollover', payload)
  return data
}
