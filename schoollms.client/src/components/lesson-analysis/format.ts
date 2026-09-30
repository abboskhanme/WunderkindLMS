import type { User } from '@/types'
import { LA_CRITERIA_KEY, pageLevel } from '@/lib/access'
import { LA_BASE } from '@/pages/admin/lesson-analysis/paths'

/**
 * Dars tahlili — umumiy formatlash yordamchilari (komponent emas: react-refresh
 * qoidasi komponent fayllaridan faqat komponent eksport qilishni talab qiladi).
 */

export const asPercent = (value: number) => `${Math.round(value)}%`
export const asCount = (value: number) => String(Math.round(value))

export const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max)

/**
 * Tor oraliqdagi qatorni o'qiladigan qiladi: ma'lumot oralig'ini kengaytirib, o'nlikka
 * yaxlitlaydi va shkala shipidan (100) oshmaydi. Eski `charts.tsx` dan aynan.
 */
export function fitDomain(
  values: number[],
  { ceiling = 100, minSpan = 20 }: { ceiling?: number; minSpan?: number } = {},
): { min: number; max: number } {
  if (values.length === 0) return { min: 0, max: ceiling }

  const lowest = Math.min(...values)
  const highest = Math.max(...values)
  const pad = Math.max(4, (highest - lowest) * 0.35)

  let min = Math.max(0, Math.floor((lowest - pad) / 10) * 10)
  let max = Math.min(ceiling, Math.ceil((highest + pad) / 10) * 10)

  if (max - min < minSpan) min = Math.max(0, max - minSpan)

  // o'q chorak qadamlarni chizadi — oraliq 20 ga karrali bo'lsin, har chiziq butun songa tushsin
  const span = Math.ceil((max - min) / 20) * 20
  if (min - (span - (max - min)) >= 0) min = max - span
  else max = Math.min(ceiling, min + span)

  return { min, max }
}

const MONTHS_SHORT = ['Yan', 'Fev', 'Mar', 'Apr', 'May', 'Iyun', 'Iyul', 'Avg', 'Sen', 'Okt', 'Noy', 'Dek']
const MONTHS_LONG = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

/** "2026-09" → "Sen 26", yoki `long` bo'lsa "Sentabr 2026". */
export function formatMonth(value: string, long = false): string {
  const parts = value.split('-')
  const name = (long ? MONTHS_LONG : MONTHS_SHORT)[Number(parts[1]) - 1]
  if (!name || parts.length < 2) return value
  return long ? `${name} ${parts[0]}` : `${name} ${parts[0].slice(2)}`
}

export function truncate(text: string, limit: number): string {
  return text.length > limit ? `${text.slice(0, limit - 1).trimEnd()}…` : text
}

/** "YYYY-MM-DD" (yoki ISO) → "DD.MM.YYYY" — Date'ga o'girmasdan, kun surilmasin. */
export function fmtDate(value: string | null | undefined): string {
  if (!value) return '—'
  const [y, m, d] = value.slice(0, 10).split('-')
  return y && m && d ? `${d}.${m}.${y}` : value
}

/** Bugungi sana "YYYY-MM-DD" — mahalliy vaqt bo'yicha (toISOString UTC'ga o'tkazib, kunni surardi). */
export function todayIso(): string {
  const now = new Date()
  const mm = String(now.getMonth() + 1).padStart(2, '0')
  const dd = String(now.getDate()).padStart(2, '0')
  return `${now.getFullYear()}-${mm}-${dd}`
}

export const initials = (name: string) =>
  name
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0])
    .join('')
    .toUpperCase()

/** Eski ilovada ADMIN — bizda admin va superadmin (SPEC §2). */
export const isLaAdmin = (user: User | null | undefined) =>
  user?.role === 'admin' || user?.role === 'superadmin'

/**
 * Mezonlarni boshqara oladimi: admin/superadmin, yoki roliga «Mezonlar» belgisi berilgan va
 * «Dars tahlili» sahifasi TO'LIQ ochiq xodim. Server bilan bir xil (`CanManageCriteria` +
 * sinf darvozasi) — aks holda tugma ko'rinib, bosilganda 403 qaytardi.
 */
export const canManageLaCriteria = (user: User | null | undefined) =>
  isLaAdmin(user) ||
  (!!user?.permissions?.includes(LA_CRITERIA_KEY) && pageLevel(user ?? null, LA_BASE) === 'edit')

/** O'rtacha — 2 xonagacha yaxlitlangan (reyting qoidasi, SPEC §4). */
export function average(values: number[]): number | null {
  if (values.length === 0) return null
  const sum = values.reduce((acc, v) => acc + v, 0)
  return Math.round((sum / values.length) * 100) / 100
}

/** Model qaytargan JSON (SPEC §6) — yoki eski tizimdan ko'chgan tayyor matn. */
export interface AiSummaryParts {
  umumiy_xulosa?: string
  kuchli_tomonlar: string[]
  rivojlantirish: string[]
  tavsiyalar: string[]
}

function asList(value: unknown): string[] {
  if (typeof value === 'string') return value.trim() ? [value.trim()] : []
  if (!Array.isArray(value)) return []
  return value.map((item) => String(item).trim()).filter(Boolean)
}

/**
 * `aiSummary` ni o'qiydi. JSON bo'lsa — to'rt kalit; bo'lmasa (eski tizimdan ko'chgan
 * tahlillarda xulosa allaqachon matnga aylantirilgan) — null, sahifa matnni o'zicha ko'rsatadi.
 */
export function parseAiSummary(raw: string | null | undefined): AiSummaryParts | null {
  if (!raw) return null
  let text = raw.trim()
  // model ba'zan ```json ... ``` ichida qaytaradi
  const fence = text.match(/^```(?:json)?\s*([\s\S]*?)\s*```$/i)
  if (fence) text = fence[1]
  if (!text.startsWith('{')) return null
  try {
    const data = JSON.parse(text) as Record<string, unknown>
    const parts: AiSummaryParts = {
      umumiy_xulosa: typeof data.umumiy_xulosa === 'string' ? data.umumiy_xulosa.trim() : undefined,
      kuchli_tomonlar: asList(data.kuchli_tomonlar),
      rivojlantirish: asList(data.rivojlantirish),
      tavsiyalar: asList(data.tavsiyalar),
    }
    const empty =
      !parts.umumiy_xulosa &&
      parts.kuchli_tomonlar.length === 0 &&
      parts.rivojlantirish.length === 0 &&
      parts.tavsiyalar.length === 0
    return empty ? null : parts
  } catch {
    return null
  }
}
