import type { SubscriptionPlanColumn } from '@/api/services/subscriptionAnalytics'

/** Abonementlar analitikasining manzili. */
export const SUBSCRIPTION_ANALYTICS_PATH = '/admin/finance/subscription-analytics'

/** Sinf sahifasi — davr query'si bilan. */
export function classPath(classId: string, search: string): string {
  return `${SUBSCRIPTION_ANALYTICS_PATH}/${encodeURIComponent(classId)}${search}`
}

/** Jadval katagi uchun qisqa summa: "12,4 mln", "850 ming". To'lig'i — `title` da. */
export function compactMoney(n: number): string {
  const abs = Math.abs(n)
  const fmt = (v: number) => new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 1 }).format(v)
  if (abs >= 1_000_000) return `${fmt(n / 1_000_000)} mln`
  if (abs >= 1_000) return `${fmt(n / 1_000)} ming`
  return fmt(n)
}

export interface PlanGroup {
  key: string
  name: string
  plans: SubscriptionPlanColumn[]
}

/** Ustunlarni toifa bo'yicha guruhlaydi; server bergan tartib (o'qish birinchi) saqlanadi. */
export function groupByCategory(plans: SubscriptionPlanColumn[]): PlanGroup[] {
  const groups: PlanGroup[] = []
  for (const p of plans) {
    const key = p.categoryCode || p.categoryName
    let g = groups.find((x) => x.key === key)
    if (!g) {
      g = { key, name: p.categoryName || 'Boshqa', plans: [] }
      groups.push(g)
    }
    g.plans.push(p)
  }
  return groups
}

/** "2026-10-01" → "01.10.2026" */
export function shortDate(iso: string): string {
  const [y, m, d] = iso.split('-')
  return `${d}.${m}.${y}`
}
