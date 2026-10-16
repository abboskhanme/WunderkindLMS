/**
 * Qizil ro'yxat — ketma-ket va sababsiz dars qoldirayotgan o'quvchilar va ota-ona bilan aloqa
 * (docs/modules/attendance-red-list.md, 2026-10-15).
 *
 * Server: `SchoolLms.Server/Controllers/AttendanceRedListController.cs`. Tiplar backend DTO'lariga
 * aynan mos (`Dtos/AttendanceRedListDtos.cs`, camelCase). O'qish — davomat bo'limi kabi, ishlash
 * (amal, sozlama, "Hozir tekshirish") — `attendanceCases` kaliti.
 */
import { api } from '../client'

export type CaseStatus = 'aloqa_kerak' | 'qayta_qongiroq' | 'boglanib_bolmadi' | 'hal_boldi'
export type CaseTab = 'open' | CaseStatus

export interface RedListContact {
  name: string
  phone: string
  relation: string | null
}

export interface RedListTeacher {
  id: string
  fullName: string
}

export interface AttendanceCaseRow {
  id: string
  studentId: string
  fullName: string
  className: string
  group: string
  archived: boolean
  status: CaseStatus
  trigger: 'consecutive' | 'window'
  streak: number
  windowCount: number
  openedOn: string
  lastAbsentOn: string | null
  lastPresentOn: string | null
  /** O'quvchi oxirgi kelmagan kunidan keyin maktabga kelgan. */
  returned: boolean
  absentDates: string[]
  callbackOn: string | null
  lastActionAt: string | null
  lastComment: string | null
  lastActorName: string | null
  daysWithoutAction: number
  overdue: boolean
  overdueReason: 'unreachable' | 'callback_due' | 'no_action' | null
  teachers: RedListTeacher[]
  contacts: RedListContact[]
  closedAt: string | null
  closedByName: string | null
}

export interface AttendanceCaseAction {
  id: string
  status: CaseStatus
  comment: string
  callbackOn: string | null
  actorName: string
  actorType: 'teacher' | 'staff'
  createdAt: string
}

export interface AttendanceCaseDetail {
  case: AttendanceCaseRow
  actions: AttendanceCaseAction[]
}

export interface AttendanceCaseList {
  counts: Record<string, number>
  rows: AttendanceCaseRow[]
  classes: string[]
  teachers: RedListTeacher[]
  closedDays: number
}

export interface OpenAttendanceCase {
  studentId: string
  caseId: string
  status: CaseStatus
}

export interface RedListSettings {
  consecutiveDays: number
  windowDays: number
  windowUnexcused: number
  countExcused: boolean
  escalateAfterDays: number
  notifyTeachers: boolean
  notifyManagement: boolean
  lastScanAt: string | null
  scanTimes: string[]
  updatedAt: string
  updatedByName: string | null
}

export type SaveRedListSettings = Pick<
  RedListSettings,
  'consecutiveDays' | 'windowDays' | 'windowUnexcused' | 'countExcused' | 'escalateAfterDays' | 'notifyTeachers' | 'notifyManagement'
>

export interface AddCaseAction {
  status: CaseStatus
  comment: string
  callbackOn?: string | null
}

export interface ScanResult {
  pupils: number
  opened: number
  updated: number
  open: number
  notified: number
}

export const CASE_STATUSES: { key: CaseStatus; label: string; cls: string; dot: string }[] = [
  { key: 'aloqa_kerak', label: 'Aloqa kerak', cls: 'bg-red-50 text-red-700', dot: 'bg-red-500' },
  { key: 'qayta_qongiroq', label: "Qayta qo'ng'iroq", cls: 'bg-amber-50 text-amber-700', dot: 'bg-amber-500' },
  { key: 'boglanib_bolmadi', label: "Bog'lanib bo'lmadi", cls: 'bg-violet-50 text-violet-700', dot: 'bg-violet-500' },
  { key: 'hal_boldi', label: "Hal bo'ldi", cls: 'bg-emerald-50 text-emerald-700', dot: 'bg-emerald-500' },
]

export const caseStatusMeta = (s: CaseStatus) => CASE_STATUSES.find((x) => x.key === s) ?? CASE_STATUSES[0]

/** "ketma-ket 3 kun" / "14 kunda 5 kun" — nima uchun ro'yxatga tushgani. */
export function caseReason(row: Pick<AttendanceCaseRow, 'trigger' | 'streak' | 'windowCount'>, windowDays: number): string {
  return row.trigger === 'consecutive' ? `${row.streak} kun ketma-ket` : `${windowDays} kunda ${row.windowCount} kun`
}

export function overdueText(row: AttendanceCaseRow): string | null {
  switch (row.overdueReason) {
    case 'unreachable':
      return "Bog'lanib bo'lmadi — rahbariyat e'tiboriga"
    case 'callback_due':
      return "Qayta qo'ng'iroq kuni o'tdi"
    case 'no_action':
      return `${row.daysWithoutAction} kundan beri amal yo'q`
    default:
      return null
  }
}

export async function getRedList(params: {
  status?: CaseTab
  className?: string
  teacherId?: string
  overdue?: boolean
}): Promise<AttendanceCaseList> {
  const { data } = await api.get<AttendanceCaseList>('/admin/attendance/red-list', {
    params: { ...params, overdue: params.overdue || undefined },
  })
  return data
}

export async function getRedListCase(id: string): Promise<AttendanceCaseDetail> {
  const { data } = await api.get<AttendanceCaseDetail>(`/admin/attendance/red-list/${id}`)
  return data
}

export async function addRedListAction(id: string, body: AddCaseAction): Promise<AttendanceCaseDetail> {
  const { data } = await api.post<AttendanceCaseDetail>(`/admin/attendance/red-list/${id}/actions`, body)
  return data
}

export async function deleteRedListAction(actionId: string): Promise<AttendanceCaseDetail> {
  const { data } = await api.delete<AttendanceCaseDetail>(`/admin/attendance/red-list/actions/${actionId}`)
  return data
}

export async function getOpenRedListCases(): Promise<OpenAttendanceCase[]> {
  const { data } = await api.get<OpenAttendanceCase[]>('/admin/attendance/red-list/open')
  return data
}

export async function getRedListSettings(): Promise<RedListSettings> {
  const { data } = await api.get<RedListSettings>('/admin/attendance/red-list/settings')
  return data
}

export async function saveRedListSettings(body: SaveRedListSettings): Promise<RedListSettings> {
  const { data } = await api.put<RedListSettings>('/admin/attendance/red-list/settings', body)
  return data
}

export async function scanRedList(): Promise<ScanResult> {
  const { data } = await api.post<ScanResult>('/admin/attendance/red-list/scan')
  return data
}
