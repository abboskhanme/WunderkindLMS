/**
 * Helpers shared by the discount screens (docs/modules/billing-plans.md §8): the catalog card,
 * the student-profile discounts card and its attach / end modals.
 *
 * Kept out of the component files: `react-refresh/only-export-components` flags non-component
 * exports there. Months are "yyyy-MM", dates "yyyy-MM-dd", both in LOCAL time — `toISOString`
 * would shift the day across midnight in Tashkent (UTC+5).
 */
import type { Discount } from '@/types'
import { formatMoney } from '@/lib/utils'

const pad = (n: number) => String(n).padStart(2, '0')

/** Today, "yyyy-MM-dd", local time. */
export function todayIso(now: Date = new Date()): string {
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

/** The current month, "yyyy-MM", local time. */
export function currentMonth(now: Date = new Date()): string {
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}`
}

/** "2026-09" → "2026-09-01". */
export function monthStart(month: string): string {
  return `${month}-01`
}

/** "2026-02" → "2026-02-28" — the last day of the month. */
export function monthEnd(month: string): string {
  const [y, m] = month.split('-').map(Number)
  const last = new Date(y, m, 0).getDate()
  return `${month}-${pad(last)}`
}

const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

/** "2026-09" → "Sentabr 2026" (the MonthPicker's own wording). */
export function monthLabel(month: string): string {
  const [y, m] = month.split('-')
  const name = MONTHS[Number(m) - 1]
  return name && y ? `${name} ${y}` : month
}

/** "2026-09-05" → "05.09.2026". */
function dayLabel(iso: string): string {
  const [y, m, d] = iso.split('-')
  return y && m && d ? `${d}.${m}.${y}` : iso
}

/**
 * "10%", "200 000 so'm" or "10% + 50 000 so'm" — percent first, then amount, the order the
 * server applies them in.
 */
export function discountValueLabel(percent: number, amount: number): string {
  const parts: string[] = []
  if (percent > 0) parts.push(`${percent}%`)
  if (amount > 0) parts.push(formatMoney(amount))
  return parts.length > 0 ? parts.join(' + ') : '—'
}

/**
 * A discount's period. Catalog discounts run in whole months (first day → last day), so they
 * read as months; a hand-written one with arbitrary dates reads as dates.
 */
export function discountPeriodLabel(startsOn: string, endsOn: string | null | undefined): string {
  const startMonth = startsOn.slice(0, 7)
  const start = startsOn.endsWith('-01') ? monthLabel(startMonth) : dayLabel(startsOn)
  if (!endsOn) return `${start} — muddatsiz`
  const endMonth = endsOn.slice(0, 7)
  const end = endsOn === monthEnd(endMonth) ? monthLabel(endMonth) : dayLabel(endsOn)
  return start === end ? start : `${start} — ${end}`
}

/** What a student-profile row shows: the server status, plus "ended" for a finished approved one. */
export type StudentDiscountState = 'running' | 'pending' | 'rejected' | 'ended'

export function studentDiscountState(
  row: Pick<Discount, 'status' | 'endsOn'>,
  today: string = todayIso(),
): StudentDiscountState {
  if (row.status === 'pending') return 'pending'
  if (row.status === 'rejected') return 'rejected'
  return row.endsOn && row.endsOn < today ? 'ended' : 'running'
}
