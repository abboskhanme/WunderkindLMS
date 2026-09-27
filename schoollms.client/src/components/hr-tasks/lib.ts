/**
 * HR tasks — pure helpers (no React, no network).
 *
 * Ported from WunderkindHR's `entities/hr/lib.ts` and `pages/hr/tasks/lib.ts`:
 * code → Uzbek label, status → tone, number → percent, date (Tashkent) arithmetic.
 */
import type {
  HrAmount,
  HrBranch,
  HrDateString,
  HrEmployee,
  HrEmployeeNode,
  HrEmployeeTree,
  HrProofKind,
  HrRecurrence,
  HrReportRange,
  HrTargetType,
  HrTask,
  HrTaskKind,
  HrTaskProof,
  HrTaskSource,
  HrTaskStatus,
  HrTaskTemplate,
  HrWeekday,
} from '@/types/hrTasks'
import {
  HR_BLOCKED_PROOF_TYPES,
  HR_MAX_ATTACHMENT_BYTES,
  HR_MAX_PROOF_BYTES,
  HR_TASK_STATUSES,
} from '@/types/hrTasks'

export type HrTone = 'info' | 'success' | 'warning' | 'danger' | 'neutral' | 'brand'

// ── Labels ────────────────────────────────────────────────────────────────────

export const HR_TASK_STATUS_LABEL: Record<HrTaskStatus, string> = {
  open: 'Ochiq',
  done: 'Bajarildi',
  returned: 'Qaytarildi',
  missed: 'Bajarilmadi',
  cancelled: 'Bekor qilingan',
}

export const HR_TASK_STATUS_TONE: Record<HrTaskStatus, HrTone> = {
  open: 'info',
  done: 'success',
  returned: 'warning',
  missed: 'danger',
  cancelled: 'neutral',
}

export const HR_TASK_KIND_LABEL: Record<HrTaskKind, string> = {
  check: 'Belgilash',
  numeric: 'Raqamli (KPI)',
}

export const HR_TASK_SOURCE_LABEL: Record<HrTaskSource, string> = {
  director: 'Direktor',
  self: 'O‘zi qo‘shgan',
  template: 'Takroriy',
}

export const HR_RECURRENCE_LABEL: Record<HrRecurrence, string> = {
  daily: 'Har kuni',
  weekly: 'Har hafta',
  monthly: 'Har oy',
}

export const HR_TARGET_TYPE_LABEL: Record<HrTargetType, string> = {
  employee: 'Xodim',
  position: 'Lavozim',
}

export const HR_PROOF_KIND_LABEL: Record<HrProofKind, string> = {
  photo: 'Rasm',
  file: 'Fayl',
  link: 'Havola',
  text: 'Matn',
}

export const HR_BRANCH_LABEL: Record<HrBranch, string> = {
  center: 'Markaz',
  school: 'Maktab',
}

/** HR scope (`hrRole`) — hr-tasks.md §3. */
export const HR_ROLE_LABEL: Record<HrEmployee['hrRole'], string> = {
  employee: 'Xodim',
  manager: 'Rahbar',
  director: 'Direktor',
}

export const HR_ROLE_HINT: Record<HrEmployee['hrRole'], string> = {
  employee: 'Faqat o‘z topshiriqlarini ko‘radi (Telegram va «Mening topshiriqlarim»)',
  manager: 'O‘ziga bo‘ysunadigan butun shoxni ko‘radi (bo‘ysunuvchisi yo‘q bo‘lsa — faqat o‘zini)',
  director: 'Butun tashkilotni ko‘radi; lavozimlar va HR sozlamalarini boshqaradi',
}

/** Our role names — shown on the employee card. */
export const USER_ROLE_LABEL: Record<string, string> = {
  staff: 'Xodim',
  teacher: 'O‘qituvchi',
  admin: 'Administrator',
  superadmin: 'Tizim egasi',
  cashier: 'Kassir',
}

/** 1 = Monday … 7 = Sunday. */
export const HR_WEEKDAY_LABEL: Record<HrWeekday, string> = {
  1: 'dushanba',
  2: 'seshanba',
  3: 'chorshanba',
  4: 'payshanba',
  5: 'juma',
  6: 'shanba',
  7: 'yakshanba',
}

export const HR_WEEKDAY_SHORT: Record<HrWeekday, string> = {
  1: 'Du',
  2: 'Se',
  3: 'Ch',
  4: 'Pa',
  5: 'Ju',
  6: 'Sh',
  7: 'Ya',
}

export function hrWeekdayLabel(day: number): string {
  return HR_WEEKDAY_LABEL[day as HrWeekday] ?? String(day)
}

export function hrWeekdayShort(day: number): string {
  return HR_WEEKDAY_SHORT[day as HrWeekday] ?? String(day)
}

export function hrTaskStatusLabel(status: HrTaskStatus): string {
  return HR_TASK_STATUS_LABEL[status] ?? status
}

export function hrTaskStatusTone(status: HrTaskStatus): HrTone {
  return HR_TASK_STATUS_TONE[status] ?? 'neutral'
}

export function hrTaskKindLabel(kind: HrTaskKind): string {
  return HR_TASK_KIND_LABEL[kind] ?? kind
}

export function hrTaskSourceLabel(source: HrTaskSource): string {
  return HR_TASK_SOURCE_LABEL[source] ?? source
}

export function hrProofKindLabel(kind: HrProofKind): string {
  return HR_PROOF_KIND_LABEL[kind] ?? kind
}

export function hrBranchLabel(branch: HrBranch | null | undefined): string | null {
  return branch ? (HR_BRANCH_LABEL[branch] ?? branch) : null
}

/** Managers: "Aliyev A., Karimova D." or "—". */
export function hrManagerNames(employee: Pick<HrEmployee, 'managerNames'> | null | undefined): string {
  const names = (employee?.managerNames ?? []).filter((name) => Boolean(name?.trim()))
  return names.length > 0 ? names.join(', ') : '—'
}

// ── Recurrence ────────────────────────────────────────────────────────────────

function joinWeekdays(days: readonly number[]): string {
  return [...new Set(days)].map(hrWeekdayLabel).join(', ')
}

/** "Har kuni" · "Har hafta: dushanba" · "Oyning 14-sanasi" · "Oyning 14, 28-sanalari". */
export function hrRecurrenceLabel(
  template: Pick<HrTaskTemplate, 'recurrence' | 'weekdays' | 'daysOfMonth'>,
): string {
  const weekdays = template.weekdays ?? []
  const monthDays = template.daysOfMonth ?? []

  if (template.recurrence === 'monthly') {
    if (monthDays.length === 0) return 'Har oy'
    const days = [...new Set(monthDays)].join(', ')
    return monthDays.length === 1 ? `Oyning ${days}-sanasi` : `Oyning ${days}-sanalari`
  }
  if (template.recurrence === 'weekly') {
    if (weekdays.length === 0) return 'Har hafta'
    return `Har hafta: ${joinWeekdays(weekdays)}`
  }
  if (weekdays.length === 0 || new Set(weekdays).size >= 7) return 'Har kuni'
  return `Har kuni: ${joinWeekdays(weekdays)}`
}

// ── KPI ───────────────────────────────────────────────────────────────────────

/** `numeric(12,2)` — arrives as a string (`"147.00"`) or a number; 0 when invalid. */
export function hrAmount(value: HrAmount | null | undefined): number {
  if (value === null || value === undefined || value === '') return 0
  const numeric = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(numeric) ? numeric : 0
}

type KpiShape = {
  kind: HrTaskKind
  targetAmount: HrAmount | null
  currentAmount: HrAmount | null
  unit?: string | null
}

/** KPI percent 0..100 (capped at 100 so it fits the bar). */
export function hrProgressPercent(task: KpiShape): number {
  if (task.kind !== 'numeric') return 0
  const target = hrAmount(task.targetAmount)
  if (target <= 0) return 0
  const percent = (hrAmount(task.currentAmount) / target) * 100
  if (!Number.isFinite(percent) || percent <= 0) return 0
  return Math.min(Math.round(percent * 10) / 10, 100)
}

/** "147/200 lead" (`null` for `check`). */
export function hrProgressText(task: KpiShape): string | null {
  if (task.kind !== 'numeric') return null
  const body = `${hrAmount(task.currentAmount)}/${hrAmount(task.targetAmount)}`
  return task.unit ? `${body} ${task.unit}` : body
}

// ── Status ────────────────────────────────────────────────────────────────────

/** Overdue — a computed flag, not a column. */
export function hrIsOverdue(task: { dueAt: string | null; status: HrTaskStatus }, now: Date = new Date()): boolean {
  if (task.status !== 'open' && task.status !== 'returned') return false
  if (!task.dueAt) return false
  const due = new Date(task.dueAt)
  if (Number.isNaN(due.getTime())) return false
  return due.getTime() < now.getTime()
}

/** `returned` behaves like open. */
export function hrIsOpen(task: Pick<HrTask, 'status'>): boolean {
  return task.status === 'open' || task.status === 'returned'
}

/** Final state — no action buttons. */
export function hrIsFinal(task: Pick<HrTask, 'status'>): boolean {
  return task.status === 'missed' || task.status === 'cancelled'
}

const DAY_MS = 86_400_000

/** "3 kun kechikdi" / "Bugun muddati o‘tdi". */
export function hrOverdueLabel(dueAt: string | null | undefined, now: Date = new Date()): string {
  const due = dueAt ? new Date(dueAt) : null
  if (!due || Number.isNaN(due.getTime())) return 'Bugun muddati o‘tdi'
  const diff = now.getTime() - due.getTime()
  const days = diff > 0 ? Math.floor(diff / DAY_MS) : 0
  return days > 0 ? `${days} kun kechikdi` : 'Bugun muddati o‘tdi'
}

// ── Proofs and attachments ────────────────────────────────────────────────────

/** Relative file path → browser URL (`/uploads/...`); external links are kept as is. */
export function hrMediaSrc(path: string | null | undefined): string | null {
  if (!path) return null
  if (/^https?:\/\//i.test(path) || path.startsWith('/') || path.startsWith('blob:')) return path
  const relative = path.replace(/^\/+/, '')
  return relative.startsWith('uploads/') || relative.startsWith('media/') ? `/${relative}` : `/uploads/${relative}`
}

export function hrProofUrl(proof: Pick<HrTaskProof, 'kind' | 'filePath' | 'url'>): string | null {
  if (proof.kind === 'link') return proof.url
  return hrMediaSrc(proof.url ?? proof.filePath)
}

export function hrIsImageProof(proof: Pick<HrTaskProof, 'kind' | 'contentType'>): boolean {
  if (proof.kind === 'photo') return true
  return Boolean(proof.contentType?.startsWith('image/'))
}

function isBlocked(type: string): boolean {
  return HR_BLOCKED_PROOF_TYPES.includes(type.split(';')[0]?.trim().toLowerCase() ?? '')
}

export function hrProofFileError(file: Pick<File, 'size' | 'type'>, maxBytes = HR_MAX_PROOF_BYTES): string | null {
  if (file.type && isBlocked(file.type)) return 'Bu turdagi faylni isbot qilib bo‘lmaydi (HTML, SVG yoki skript).'
  if (file.size > maxBytes) return `Fayl juda katta: eng ko‘pi ${Math.round(maxBytes / (1024 * 1024))} MB.`
  return null
}

export function hrAttachmentFileError(
  file: Pick<File, 'size' | 'type'>,
  maxBytes = HR_MAX_ATTACHMENT_BYTES,
): string | null {
  if (file.type && isBlocked(file.type)) return 'Bu turdagi faylni biriktirib bo‘lmaydi (HTML, SVG yoki skript).'
  if (file.size > maxBytes) return `Fayl juda katta: eng ko‘pi ${Math.round(maxBytes / (1024 * 1024))} MB.`
  return null
}

/** Seconds → "1:23". */
export function hrVoiceDuration(seconds: number | null | undefined): string | null {
  if (seconds === null || seconds === undefined || !Number.isFinite(seconds) || seconds < 0) return null
  const total = Math.round(seconds)
  const mm = Math.floor(total / 60) % 60
  const ss = total % 60
  const hh = Math.floor(total / 3600)
  const tail = `${String(mm).padStart(hh > 0 ? 2 : 1, '0')}:${String(ss).padStart(2, '0')}`
  return hh > 0 ? `${hh}:${tail}` : tail
}

export function hrFormatBytes(bytes: number | null | undefined): string {
  if (!bytes || bytes <= 0) return ''
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

// ── Batch creation ────────────────────────────────────────────────────────────

const HR_BATCH_SKIP_REASON: Record<string, string> = {
  inactive: 'Nofaol xodim',
  not_found: 'Xodim topilmadi',
  no_telegram: 'Telegram ulanmagan',
  limit: 'Chegaradan oshdi',
}

export function hrBatchSkipReason(reason: string): string {
  return HR_BATCH_SKIP_REASON[reason] ?? reason
}

// ── Date and time (Asia/Tashkent, fixed +05:00) ──────────────────────────────

const TZ = 'Asia/Tashkent'
const TZ_OFFSET = '+05:00'

const dateFmt = new Intl.DateTimeFormat('en-CA', { timeZone: TZ, year: 'numeric', month: '2-digit', day: '2-digit' })
const timeFmt = new Intl.DateTimeFormat('en-GB', { timeZone: TZ, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })

/** Today in Tashkent (`yyyy-MM-dd`) — not the browser's time zone. */
export function hrToday(now: Date = new Date()): HrDateString {
  return dateFmt.format(now)
}

/** ISO → Tashkent `{ date: 'yyyy-MM-dd', time: 'HH:mm' }`. */
export function hrTashkentParts(iso: string | null | undefined): { date: string; time: string } | null {
  if (!iso) return null
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return null
  return { date: dateFmt.format(d), time: timeFmt.format(d) }
}

/** `yyyy-MM-dd` + `HH:mm` (Tashkent) → ISO `2026-09-02T18:00:00+05:00`. */
export function hrToIso(date: string, time: string): string | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || !/^([01]\d|2[0-3]):[0-5]\d$/.test(time)) return null
  return `${date}T${time}:00${TZ_OFFSET}`
}

/** `yyyy-MM-dd` → `dd.MM.yyyy`. */
export function hrFormatDate(value: string | null | undefined): string {
  if (!value) return '—'
  const [y, m, d] = value.slice(0, 10).split('-')
  return y && m && d ? `${d}.${m}.${y}` : value
}

/** ISO → `dd.MM.yyyy HH:mm` (Tashkent). */
export function hrFormatDateTime(iso: string | null | undefined): string {
  const parts = hrTashkentParts(iso)
  return parts ? `${hrFormatDate(parts.date)} ${parts.time}` : '—'
}

/** ISO → `HH:mm` (Tashkent). */
export function hrFormatTime(iso: string | null | undefined): string {
  return hrTashkentParts(iso)?.time ?? '—'
}

/** "02.09.2026 — 08.09.2026" (a single date for one day). */
export function hrFormatRange(from: string, to: string): string {
  if (!from && !to) return '—'
  if (from === to) return hrFormatDate(from)
  return `${hrFormatDate(from)} — ${hrFormatDate(to)}`
}

const numberFmt = new Intl.NumberFormat('ru-RU')

export function hrFormatNumber(value: number | null | undefined): string {
  return numberFmt.format(value ?? 0)
}

export function hrFormatPercent(value: number | null | undefined): string {
  const safe = Number.isFinite(value) ? (value as number) : 0
  return `${Math.round(safe * 10) / 10}%`
}

// ── Period picker (dashboard, task list, reports) ────────────────────────────

export type HrPeriodPreset = 'today' | '3d' | '7d' | '15d' | '30d' | 'week' | 'month' | 'prev_month'

export const HR_PERIOD_LABEL: Record<HrPeriodPreset, string> = {
  today: 'Bugun',
  '3d': '3 kun',
  '7d': '1 hafta',
  '15d': '15 kun',
  '30d': '1 oy',
  week: 'Shu hafta',
  month: 'Shu oy',
  prev_month: 'O‘tgan oy',
}

const HR_PERIOD_PRESETS: readonly HrPeriodPreset[] = ['today', '3d', '7d', '15d', '30d', 'week', 'month', 'prev_month']

/** The presets drawn as buttons — one row. */
export const HR_PERIOD_BUTTONS: readonly HrPeriodPreset[] = ['today', '3d', '7d', '15d', '30d']

const ROLLING_DAYS: Partial<Record<HrPeriodPreset, number>> = { '3d': 3, '7d': 7, '15d': 15, '30d': 30 }

export function hrParsePeriod(value: string | null | undefined): HrPeriodPreset {
  return HR_PERIOD_PRESETS.includes(value as HrPeriodPreset) ? (value as HrPeriodPreset) : 'today'
}

function parseDay(value: HrDateString): Date {
  const [year, month, day] = value.split('-').map(Number)
  return new Date(Date.UTC(year ?? 1970, (month ?? 1) - 1, day ?? 1))
}

function isoOf(value: Date): HrDateString {
  const pad = (n: number): string => String(n).padStart(2, '0')
  return `${value.getUTCFullYear()}-${pad(value.getUTCMonth() + 1)}-${pad(value.getUTCDate())}`
}

/** Preset range — backwards from today (future days never included). */
export function hrPresetRange(preset: HrPeriodPreset, todayIso: HrDateString): HrReportRange {
  const today = parseDay(todayIso)
  if (preset === 'today') return { dateFrom: isoOf(today), dateTo: isoOf(today) }

  const days = ROLLING_DAYS[preset]
  if (days !== undefined) {
    return { dateFrom: isoOf(new Date(today.getTime() - (days - 1) * DAY_MS)), dateTo: isoOf(today) }
  }
  if (preset === 'week') {
    const weekday = today.getUTCDay() === 0 ? 7 : today.getUTCDay()
    return { dateFrom: isoOf(new Date(today.getTime() - (weekday - 1) * DAY_MS)), dateTo: isoOf(today) }
  }
  if (preset === 'month') {
    return { dateFrom: isoOf(new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), 1))), dateTo: isoOf(today) }
  }
  return {
    dateFrom: isoOf(new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth() - 1, 1))),
    dateTo: isoOf(new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), 0))),
  }
}

export function hrIsValidRange(range: HrReportRange): boolean {
  if (!range.dateFrom || !range.dateTo) return false
  return range.dateFrom <= range.dateTo
}

// ── Filter options ────────────────────────────────────────────────────────────

export const HR_STATUS_OPTIONS = HR_TASK_STATUSES.map((status) => ({ value: status, label: hrTaskStatusLabel(status) }))

export function hrParseStatuses(values: readonly string[]): HrTaskStatus[] {
  return values.filter((value): value is HrTaskStatus => (HR_TASK_STATUSES as readonly string[]).includes(value))
}

export const HR_ACCEPTED_OPTIONS = [
  { value: 'false', label: 'Tasdiq kutilmoqda' },
  { value: 'true', label: 'Tasdiqlangan' },
]

export function hrParseAccepted(value: string | null | undefined): boolean | null {
  if (value === 'false') return false
  if (value === 'true') return true
  return null
}

// ── Reporting tree ────────────────────────────────────────────────────────────

/** Tree → flat list (each `id` once, cycle-safe). */
export function hrFlattenTree(tree: HrEmployeeTree | null | undefined): HrEmployeeNode[] {
  const result: HrEmployeeNode[] = []
  const seen = new Set<string>()
  const walk = (nodes: readonly HrEmployeeNode[] | undefined): void => {
    for (const node of nodes ?? []) {
      if (!node || seen.has(node.id)) continue
      seen.add(node.id)
      result.push(node)
      walk(node.children)
    }
  }
  walk(tree?.roots)
  walk(tree?.unassigned)
  return result
}
