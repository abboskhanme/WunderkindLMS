/**
 * Yangiliklar bo'limining o'zbekcha yorliqlari va kichik formatlovchilari
 * (SM-10). Sahifa ham, tasdiq oynalari ham shu yerdan oladi — "ota-ona"
 * so'zi ikki joyda ikki xil yozilib qolmasligi uchun.
 */
import type { NewsAdminDto, NewsAudience, NewsState } from '@/api/services/news'

/** `audience[]` qiymatlari — ekranda ko'rinadigan tartibda (§6.2). */
export const AUDIENCE_ORDER: readonly NewsAudience[] = ['employee', 'parent', 'student']

const AUDIENCE_LABELS: Record<NewsAudience, string> = {
  employee: 'Xodim',
  parent: 'Ota-ona',
  student: "O'quvchi",
}

/** Bitta qatnashuvchi yorlig'i: `xodim` → `Xodim`. */
export function audienceLabel(audience: NewsAudience): string {
  return AUDIENCE_LABELS[audience]
}

const AUDIENCE_PLURALS: Record<NewsAudience, string> = {
  employee: 'xodimlar',
  parent: 'ota-onalar',
  student: "o'quvchilar",
}

/** Gap ichida ishlatish uchun: `ota-onalar va o'quvchilar`. */
export function audienceListText(audience: NewsAudience[]): string {
  const parts = sortAudience(audience).map((a) => AUDIENCE_PLURALS[a])
  if (parts.length === 0) return 'hech kim'
  if (parts.length === 1) return parts[0]
  return `${parts.slice(0, -1).join(', ')} va ${parts[parts.length - 1]}`
}

/** Server qanday tartibda bersa ham, ekranda tartib bir xil bo'lsin. */
export function sortAudience(audience: NewsAudience[]): NewsAudience[] {
  return AUDIENCE_ORDER.filter((a) => audience.includes(a))
}

/** Ro'yxat filtrlari — segment tugmalari uchun. */
export const STATE_TABS: ReadonlyArray<{ value: NewsState; label: string }> = [
  { value: 'all', label: 'Hammasi' },
  { value: 'draft', label: 'Qoralama' },
  { value: 'published', label: "E'lon qilingan" },
  { value: 'archived', label: 'Arxiv' },
]

/** Filtr bo'yicha bo'sh holat matni — "hech narsa yo'q" ham aniq bo'lsin. */
export function emptyText(state: NewsState): string {
  if (state === 'draft') return "Qoralama yo'q"
  if (state === 'published') return "E'lon qilingan yangilik yo'q"
  if (state === 'archived') return "Arxiv bo'sh — hali hech narsa o'chirilmagan"
  return 'Hali birorta yangilik yozilmagan'
}

/** ISO sana-vaqtni `DD.MM.YYYY HH:mm` ko'rinishida (brauzer mahalliy vaqtida). */
export function formatWhen(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  const day = `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()}`
  if (!iso.includes('T')) return day
  return `${day} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/**
 * Qatorda ko'rinadigan Telegram natijasi (§3.3 N4).
 *
 * `telegramSentAt === null` uchta holatni bildirishi mumkin: hali e'lon
 * qilinmagan, `sendTelegram: false` bilan e'lon qilingan yoki bot
 * sozlanmagan. DTO ularni ajratmaydi, shuning uchun qatorda faqat
 * ishonchli gap yoziladi — "yuborilmagan". Bot sozlanmagani haqidagi aniq
 * jumla esa AYNAN yuborishni so'ragan odamga, e'lon qilingan zahoti
 * ko'rsatiladi.
 */
export function telegramSummary(item: NewsAdminDto): string | null {
  if (!item.publishedAt) return null
  if (!item.telegramSentAt) return 'Telegram xabari yuborilmagan'
  return `Telegram: ${item.telegramSentCount}/${item.telegramRecipientCount} yetkazildi · ${formatWhen(item.telegramSentAt)}`
}
