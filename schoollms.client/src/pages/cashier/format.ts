import type { PaymentMethod } from '@/types'

/**
 * Kassa ekranining formatlash yordamchilari (P1-16).
 *
 * Nega alohida fayl: bir xil summa uchta joyda ko'rsatiladi (to'lov shakli,
 * taqsimot muharriri, chek) va ikkita joyda KIRITILADI (to'lov summasi,
 * sanalgan naqd). Formatlashning ikkinchi nusxasi — bir kun raqamlari
 * ajralib ketadigan ikkita ekran degani.
 */

/**
 * Guruh ajratgichlari: oddiy probel, uzilmas probel (U+00A0 — `Intl` aynan
 * shuni qo'yadi) va tor uzilmas probel (U+202F). Belgilar ATAYLAB `\u` bilan
 * yozilgan: manba faylda ular oddiy probeldan farq qilmaydi va keyingi
 * tahrirda jimgina yo'qolib ketardi.
 */
const SEPARATORS = /[\s\u00a0\u202f]/g

/**
 * Summani guruhlab ko'rsatadi: `750000` → `"750 000"`. Valyuta qo'shilmaydi —
 * "so'm" so'zi maydon yorlig'ida bir marta turadi, har qatorda emas.
 *
 * Tiyin faqat mavjud bo'lsagina chiqadi: baza ustuni `numeric(14,2)`, lekin
 * amaldagi summalar butun so'mda va `750 000,00` ni o'qish qiyinroq.
 */
export function formatSum(value: number): string {
  if (!Number.isFinite(value)) return '0'
  const rounded = Math.round(value * 100) / 100
  return new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 0,
    maximumFractionDigits: Number.isInteger(rounded) ? 0 : 2,
  }).format(rounded)
}

/** `750000` → `"750 000 so'm"`. Ro'yxatlarda va chekda ishlatiladi. */
export function formatSumWithUnit(value: number): string {
  return `${formatSum(value)} so'm`
}

/**
 * Kiritilgan matnni songa aylantiradi. Bo'sh yoki noto'g'ri matn — `null`
 * (0 EMAS): "hech narsa kiritmadim" bilan "nol kiritdim" farqli holatlar.
 * Smenani yopishda aynan shu farq muhim (SPEC §4.2).
 */
export function parseSum(text: string): number | null {
  const cleaned = text.replace(SEPARATORS, '').replace(',', '.')
  if (!cleaned) return null
  if (!/^\d+(\.\d{0,2})?$/.test(cleaned)) return null
  const value = Number(cleaned)
  return Number.isFinite(value) ? value : null
}

/**
 * Kiritish maydonining matnini tozalaydi: faqat raqamlar va bitta o'nlik
 * ajratgich qoladi. Kassir klaviaturadan tasodifan harf bosgani — xato
 * xabari sababi bo'lmasligi kerak.
 */
export function sanitizeSumInput(text: string): string {
  const cleaned = text.replace(SEPARATORS, '').replace(',', '.').replace(/[^\d.]/g, '')
  const [whole, ...rest] = cleaned.split('.')
  if (rest.length === 0) return whole
  return `${whole}.${rest.join('').slice(0, 2)}`
}

/** ISO sana-vaqtni `DD.MM.YYYY HH:MM` ko'rinishida (brauzer mahalliy vaqtida). */
export function formatDateTime(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** `"2026-09-01"` → `"2026-yil sentabr"`. Hisob-faktura davri uchun. */
const MONTHS = [
  'yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun',
  'iyul', 'avgust', 'sentabr', 'oktabr', 'noyabr', 'dekabr',
]

export function formatPeriod(periodMonth: string): string {
  const [year, month] = periodMonth.split('-')
  const index = Number(month) - 1
  if (!year || Number.isNaN(index) || index < 0 || index > 11) return periodMonth
  return `${year}-yil ${MONTHS[index]}`
}

/**
 * To'lov usullarining o'zbekcha nomlari (SPEC §8.1 Q13).
 *
 * Bu FAQAT YORLIQ: tizim Payme/Click/Uzum yoki terminal bilan gaplashmaydi,
 * kassir to'lovchi nima bilan to'laganini belgilaydi. Smena yopilishida
 * faqat `cash` sanaladi, qolgan uchtasi bankka tushadi.
 */
export const methodLabels: Record<PaymentMethod, string> = {
  cash: 'Naqd',
  card: 'Karta (terminal)',
  transfer: "Bank o'tkazmasi",
  online: 'Onlayn (Payme/Click/Uzum)',
}
