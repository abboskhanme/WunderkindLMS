/**
 * The recurring-task (template) form — the old `templates/templateForm.ts` without zod:
 * invariants match the domain; numeric fields are TEXT in the form.
 */
import type { HrRecurrence, HrTargetType, HrTaskKind, HrTaskTemplate, HrTemplateCreatePayload } from '@/types/hrTasks'

const TIME_RE = /^([01]\d|2[0-3]):[0-5]\d$/
const AMOUNT_RE = /^\d+([.,]\d{1,2})?$/
const DAY_RE = /^(0?[1-9]|[12]\d|3[01])$/

export const DEFAULT_WEEKDAYS = [1, 2, 3, 4, 5, 6]

export interface TemplateFormValues {
  title: string
  description: string
  targetType: HrTargetType
  targetEmployeeId: string
  targetPositionId: string
  recurrence: HrRecurrence
  weekdays: number[]
  weekday: string
  dayOfMonth1: string
  dayOfMonth2: string
  dueTime: string
  kind: HrTaskKind
  targetAmount: string
  unit: string
}

export type TemplateFormErrors = Partial<Record<keyof TemplateFormValues, string>>

export function emptyTemplateForm(): TemplateFormValues {
  return {
    title: '',
    description: '',
    targetType: 'employee',
    targetEmployeeId: '',
    targetPositionId: '',
    recurrence: 'daily',
    weekdays: [...DEFAULT_WEEKDAYS],
    weekday: '1',
    dayOfMonth1: '',
    dayOfMonth2: '',
    dueTime: '',
    kind: 'check',
    targetAmount: '',
    unit: '',
  }
}

export function toTemplateFormValues(t: HrTaskTemplate): TemplateFormValues {
  const weekdays = t.weekdays ?? []
  const monthDays = t.daysOfMonth ?? []
  return {
    title: t.title,
    description: t.description ?? '',
    targetType: t.targetType,
    targetEmployeeId: t.targetEmployeeId ?? '',
    targetPositionId: t.targetPositionId ?? '',
    recurrence: t.recurrence,
    weekdays: t.recurrence === 'weekly' ? [...DEFAULT_WEEKDAYS] : [...weekdays],
    weekday: t.recurrence === 'weekly' ? String(weekdays[0] ?? 1) : '1',
    dayOfMonth1: monthDays[0] === undefined ? '' : String(monthDays[0]),
    dayOfMonth2: monthDays[1] === undefined ? '' : String(monthDays[1]),
    // A `time` column may arrive as `HH:MM:SS`.
    dueTime: t.dueTime ? t.dueTime.slice(0, 5) : '',
    kind: t.kind,
    targetAmount: t.targetAmount === null || t.targetAmount === undefined ? '' : String(t.targetAmount),
    unit: t.unit ?? '',
  }
}

/** All invariants (old SPEC §3.3) — checked BEFORE a backend 422. */
export function templateErrors(v: TemplateFormValues): TemplateFormErrors {
  const e: TemplateFormErrors = {}
  const title = v.title.trim()
  if (title.length < 3) e.title = 'Sarlavha kamida 3 ta belgidan iborat bo‘lsin'
  else if (title.length > 200) e.title = 'Sarlavha 200 belgidan oshmasin'
  if (v.description.trim().length > 2000) e.description = 'Tavsif 2000 belgidan oshmasin'

  if (v.targetType === 'employee' && !v.targetEmployeeId) e.targetEmployeeId = 'Xodimni tanlang'
  if (v.targetType === 'position' && !v.targetPositionId) e.targetPositionId = 'Lavozimni tanlang'

  if (v.recurrence === 'daily' && v.weekdays.length === 0) e.weekdays = 'Kamida bitta hafta kunini tanlang'
  if (v.recurrence === 'weekly' && v.weekday === '') e.weekday = 'Hafta kunini tanlang'
  if (v.recurrence === 'monthly') {
    if (!DAY_RE.test(v.dayOfMonth1.trim())) e.dayOfMonth1 = 'Oy kuni: 1–31 oralig‘idagi son'
    const second = v.dayOfMonth2.trim()
    if (second !== '') {
      if (!DAY_RE.test(second)) e.dayOfMonth2 = 'Oy kuni: 1–31 oralig‘idagi son'
      else if (Number(second) === Number(v.dayOfMonth1.trim())) e.dayOfMonth2 = 'Ikkala sana bir xil bo‘lmasin'
    }
  }
  if (v.dueTime !== '' && !TIME_RE.test(v.dueTime)) e.dueTime = 'Vaqt HH:MM ko‘rinishida bo‘lsin (masalan 18:00)'

  if (v.kind === 'numeric') {
    const amount = v.targetAmount.trim()
    if (!AMOUNT_RE.test(amount) || Number(amount.replace(',', '.')) <= 0) e.targetAmount = 'Maqsad noldan katta son bo‘lsin (masalan 200)'
    if (v.unit.trim() === '') e.unit = 'O‘lchov birligini yozing (masalan «lead»)'
    else if (v.unit.trim().length > 32) e.unit = 'O‘lchov birligi 32 belgidan oshmasin'
  }
  return e
}

function weekdaysOf(v: TemplateFormValues): number[] {
  if (v.recurrence === 'weekly') return [Number(v.weekday)]
  if (v.recurrence === 'daily') return [...new Set(v.weekdays)].sort((a, b) => a - b)
  return []
}

function monthDaysOf(v: TemplateFormValues): number[] {
  if (v.recurrence !== 'monthly') return []
  const days = [v.dayOfMonth1.trim(), v.dayOfMonth2.trim()].filter((d) => d !== '').map(Number)
  return [...new Set(days)].sort((a, b) => a - b)
}

/**
 * `proofRequired` and `overduePolicy` are deliberately NOT sent: proof is always required
 * and an unfinished task always stays open — the backend sets both.
 */
export function toTemplatePayload(v: TemplateFormValues): HrTemplateCreatePayload {
  const numeric = v.kind === 'numeric'
  return {
    title: v.title.trim(),
    description: v.description.trim() || null,
    targetType: v.targetType,
    targetEmployeeId: v.targetType === 'employee' ? v.targetEmployeeId : null,
    targetPositionId: v.targetType === 'position' ? v.targetPositionId : null,
    recurrence: v.recurrence,
    weekdays: weekdaysOf(v),
    daysOfMonth: monthDaysOf(v),
    dueTime: v.dueTime || null,
    kind: v.kind,
    targetAmount: numeric ? Number(v.targetAmount.trim().replace(',', '.')) : null,
    unit: numeric ? v.unit.trim() : null,
  }
}

export function hasShortMonthDay(v: Pick<TemplateFormValues, 'recurrence' | 'dayOfMonth1' | 'dayOfMonth2'>): boolean {
  if (v.recurrence !== 'monthly') return false
  return [v.dayOfMonth1.trim(), v.dayOfMonth2.trim()].filter((d) => DAY_RE.test(d)).some((d) => Number(d) >= 29)
}
