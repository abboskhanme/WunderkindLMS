/**
 * Pure logic of the "Topshiriq berish" page (the old `pages/hr/tasks/taskBatchForm.ts`):
 * one row's shape, its validation and its conversion to the request body.
 * Numeric fields are TEXT in the form so an empty input never yields `NaN`.
 */
import type { HrRecurrence, HrTaskAttachment, HrTaskBatchItem, HrTaskKind, HrTemplateBatchItem } from '@/types/hrTasks'
import { HR_MAX_BATCH_EMPLOYEES, HR_MAX_BATCH_ITEMS, HR_MAX_BATCH_TOTAL } from '@/types/hrTasks'
import { hrToIso } from '@/components/hr-tasks/lib'

const TIME_RE = /^([01]\d|2[0-3]):[0-5]\d$/
const AMOUNT_RE = /^\d+([.,]\d{1,2})?$/
const DAY_RE = /^(0?[1-9]|[12]\d|3[01])$/

/** Default working days — Monday to Saturday. */
export const HR_DEFAULT_WEEKDAYS: readonly number[] = [1, 2, 3, 4, 5, 6]

/** Default due time of a new row — today 18:00. */
export const HR_DEFAULT_DUE_TIME = '18:00'

let rowCounter = 0
function rowKey(): string {
  rowCounter += 1
  return `row-${rowCounter}`
}

export interface HrTaskRow {
  key: string
  title: string
  description: string
  /** `yyyy-MM-dd` + `HH:mm` (Tashkent) — for a one-off task. */
  dueDate: string
  dueTime: string
  kind: HrTaskKind
  targetAmount: string
  unit: string
  // ── Recurring mode ──
  recurrence: HrRecurrence
  /** `daily` — selected weekdays. */
  weekdays: number[]
  /** `weekly` — ONE day (`'1'`..`'7'`). */
  weekday: string
  dayOfMonth1: string
  dayOfMonth2: string
  /** `HH:mm`; empty — end of day. */
  templateDueTime: string
  /** Uploaded DRAFT attachment (voice note). */
  attachment: HrTaskAttachment | null
}

export type HrTaskRowField =
  | 'title'
  | 'description'
  | 'due'
  | 'targetAmount'
  | 'unit'
  | 'weekdays'
  | 'weekday'
  | 'dayOfMonth1'
  | 'dayOfMonth2'
  | 'templateDueTime'

export type HrTaskRowErrors = Partial<Record<HrTaskRowField, string>>

export function hrEmptyTaskRow(today: string): HrTaskRow {
  return {
    key: rowKey(),
    title: '',
    description: '',
    dueDate: today,
    dueTime: HR_DEFAULT_DUE_TIME,
    kind: 'check',
    targetAmount: '',
    unit: '',
    recurrence: 'daily',
    weekdays: [...HR_DEFAULT_WEEKDAYS],
    weekday: '1',
    dayOfMonth1: '',
    dayOfMonth2: '',
    templateDueTime: HR_DEFAULT_DUE_TIME,
    attachment: null,
  }
}

/** Row errors — caught BEFORE the backend answers 422. */
export function hrTaskRowErrors(row: HrTaskRow, recurring: boolean): HrTaskRowErrors {
  const errors: HrTaskRowErrors = {}
  const title = row.title.trim()

  if (title.length < 3) errors.title = 'Sarlavha kamida 3 ta belgi'
  else if (title.length > 200) errors.title = 'Sarlavha 200 belgidan oshmasin'

  if (row.description.trim().length > 2000) errors.description = 'Tavsif 2000 belgidan oshmasin'

  if (row.kind === 'numeric') {
    const amount = row.targetAmount.trim()
    if (!AMOUNT_RE.test(amount) || Number(amount.replace(',', '.')) <= 0) {
      errors.targetAmount = 'Maqsad: noldan katta son (masalan 200)'
    }
    if (row.unit.trim().length > 32) errors.unit = 'O‘lchov birligi 32 belgidan oshmasin'
    else if (recurring && row.unit.trim() === '') errors.unit = 'O‘lchov birligini yozing (masalan «lead»)'
  }

  if (!recurring) {
    if (!row.dueDate) errors.due = 'Muddatni belgilang'
    else if (hrToIso(row.dueDate, row.dueTime) === null) errors.due = 'Muddat noto‘g‘ri: sana va vaqtni tanlang'
    return errors
  }

  if (row.recurrence === 'daily' && row.weekdays.length === 0) errors.weekdays = 'Kamida bitta hafta kunini tanlang'
  if (row.recurrence === 'weekly' && row.weekday === '') errors.weekday = 'Hafta kunini tanlang'
  if (row.recurrence === 'monthly') {
    if (!DAY_RE.test(row.dayOfMonth1.trim())) errors.dayOfMonth1 = 'Oy kuni: 1–31 oralig‘idagi son'
    const second = row.dayOfMonth2.trim()
    if (second !== '') {
      if (!DAY_RE.test(second)) errors.dayOfMonth2 = 'Oy kuni: 1–31 oralig‘idagi son'
      else if (Number(second) === Number(row.dayOfMonth1.trim())) errors.dayOfMonth2 = 'Ikkala sana bir xil bo‘lmasin'
    }
  }
  if (row.templateDueTime.trim() !== '' && !TIME_RE.test(row.templateDueTime.trim())) {
    errors.templateDueTime = 'Vaqt HH:MM ko‘rinishida bo‘lsin (masalan 18:00)'
  }
  return errors
}

export function hrHasRowError(errors: HrTaskRowErrors): boolean {
  return Object.keys(errors).length > 0
}

/** Limits (`HR_MAX_BATCH_*`) — checked BEFORE sending. */
export function hrBatchLimitError(employees: number, rows: number): string | null {
  if (employees === 0) return 'Kamida bitta xodimni tanlang'
  if (rows === 0) return 'Kamida bitta topshiriq qatori kerak'
  if (employees > HR_MAX_BATCH_EMPLOYEES) return `Bir vaqtda ko‘pi bilan ${HR_MAX_BATCH_EMPLOYEES} ta xodim tanlanadi`
  if (rows > HR_MAX_BATCH_ITEMS) return `Bir vaqtda ko‘pi bilan ${HR_MAX_BATCH_ITEMS} ta topshiriq qatori bo‘ladi`
  if (employees * rows > HR_MAX_BATCH_TOTAL) {
    return `Jami ${employees * rows} ta yozuv chiqadi — chegara ${HR_MAX_BATCH_TOTAL} ta`
  }
  return null
}

/** "6 xodim × 3 topshiriq = 18 ta yaratiladi". */
export function hrBatchSummary(employees: number, rows: number): string {
  return `${employees} xodim × ${rows} topshiriq = ${employees * rows} ta yaratiladi`
}

function weekdaysOf(row: HrTaskRow): number[] {
  if (row.recurrence === 'weekly') return [Number(row.weekday)]
  if (row.recurrence === 'daily') return [...new Set(row.weekdays)].sort((a, b) => a - b)
  return []
}

function monthDaysOf(row: HrTaskRow): number[] {
  if (row.recurrence !== 'monthly') return []
  const days = [row.dayOfMonth1.trim(), row.dayOfMonth2.trim()].filter((d) => d !== '').map(Number)
  return [...new Set(days)].sort((a, b) => a - b)
}

/** Row → `POST /tasks/batch` item (`proofRequired` is not sent — the backend sets it). */
export function hrToBatchItem(row: HrTaskRow): HrTaskBatchItem {
  const numeric = row.kind === 'numeric'
  return {
    title: row.title.trim(),
    dueAt: hrToIso(row.dueDate, row.dueTime) ?? '',
    description: row.description.trim() || null,
    kind: row.kind,
    targetAmount: numeric ? Number(row.targetAmount.trim().replace(',', '.')) : null,
    unit: numeric ? row.unit.trim() || null : null,
    attachmentId: row.attachment?.id ?? null,
  }
}

/**
 * Row → `POST /templates/batch` item (no target — one template per employee).
 * ⛔️ `attachmentId` is NOT sent: a template does not copy attachments to its tasks (old decision).
 */
export function hrToTemplateBatchItem(row: HrTaskRow): HrTemplateBatchItem {
  const numeric = row.kind === 'numeric'
  return {
    title: row.title.trim(),
    description: row.description.trim() || null,
    recurrence: row.recurrence,
    weekdays: weekdaysOf(row),
    daysOfMonth: monthDaysOf(row),
    dueTime: row.templateDueTime.trim() || null,
    kind: row.kind,
    targetAmount: numeric ? Number(row.targetAmount.trim().replace(',', '.')) : null,
    unit: numeric ? row.unit.trim() : null,
  }
}

/** Days 29–31 move to the last day in short months. */
export function hrHasShortMonthDay(row: Pick<HrTaskRow, 'recurrence' | 'dayOfMonth1' | 'dayOfMonth2'>): boolean {
  if (row.recurrence !== 'monthly') return false
  return [row.dayOfMonth1.trim(), row.dayOfMonth2.trim()].filter((d) => DAY_RE.test(d)).some((d) => Number(d) >= 29)
}
