/**
 * Small helpers shared by the abonement screens (docs/modules/billing-plans.md).
 *
 * Kept out of the component files: `react-refresh/only-export-components` flags
 * non-component exports there.
 */
import type { SubscriptionPlan } from '@/types'

export interface PlanGroup {
  categoryId: string
  categoryName: string
  plans: SubscriptionPlan[]
}

/** Plans grouped by type (category), keeping the incoming order — the server's is type, then name. */
export function groupPlansByType(plans: SubscriptionPlan[]): PlanGroup[] {
  const map = new Map<string, PlanGroup>()
  for (const plan of plans) {
    const group = map.get(plan.categoryId)
    if (group) group.plans.push(plan)
    else map.set(plan.categoryId, { categoryId: plan.categoryId, categoryName: plan.categoryName, plans: [plan] })
  }
  return [...map.values()]
}

/* ---------- Price-change date (§3: "from next month") ---------- */

const MONTHS = [
  'yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun',
  'iyul', 'avgust', 'sentabr', 'oktabr', 'noyabr', 'dekabr',
]

/** First day of next month in local time, "yyyy-MM-dd" — what the server uses as `effectiveFrom`. */
export function nextMonthStart(now: Date = new Date()): string {
  const d = new Date(now.getFullYear(), now.getMonth() + 1, 1)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`
}

/** "2026-10-01" → "1-oktabr"; the year is added only when it is not the current one. */
export function formatDayMonth(iso: string, now: Date = new Date()): string {
  const [y, m, d] = iso.split('-').map(Number)
  if (!y || !m || !d || m < 1 || m > 12) return iso
  const label = `${d}-${MONTHS[m - 1]}`
  return y === now.getFullYear() ? label : `${y}-yil ${label}`
}

/* ---------- Schedule (§7: "Butun oy" / "Haftasiga N kun") ---------- */

/** 1..7 — "Haftasiga N kun"; null stands for "Butun oy". */
export const DAYS_PER_WEEK_OPTIONS = [1, 2, 3, 4, 5, 6, 7] as const

/** null → "Butun oy", 6 → "Haftasiga 6 kun". */
export function scheduleLabel(daysPerWeek: number | null): string {
  // `== null`: a server that predates §7 omits the field — that reads as "Butun oy" too.
  return daysPerWeek == null ? 'Butun oy' : `Haftasiga ${daysPerWeek} kun`
}
