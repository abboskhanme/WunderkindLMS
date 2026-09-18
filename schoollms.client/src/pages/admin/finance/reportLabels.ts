/**
 * Direktor moliya panelidagi yorliqlar va formatlash (P1-18).
 *
 * Interfeys tili — O'ZBEKCHA (loyiha qoidasi, i18n kutubxonasi YO'Q).
 * Hisob kodlari (`revenue:tuition`, `expense:salary` …) — backend'ning
 * yopiq ro'yxati; bu yerda ular faqat KO'RSATISH uchun o'zbekchaga
 * o'giriladi, hech qachon mantiq uchun ishlatilmaydi.
 */
import type { PaymentMethod } from '@/types'
import { formatMonth } from '@/config/constants'
import { formatMoney } from '@/lib/utils'

/**
 * Hisob kodi → o'zbekcha nom.
 *
 * Ro'yxatda YO'Q kod ham kelishi mumkin: `FinanceReportQueries.Lines`
 * bazada uchragan har qanday hisobni qatorga qo'shadi ("pulni jimgina
 * yo'qotgandan ko'ra, kutilmagan qator ko'rsatilgani yaxshi"). Shuning
 * uchun bu yerda ham tushib qolish YO'Q — noma'lum kod prefiksi olinib,
 * o'zi ko'rsatiladi.
 */
const accountNames: Record<string, string> = {
  cash: 'Kassa (naqd)',
  bank: 'Bank hisobi',
  receivable: "Debitorlik (yig'ilmagan qarz)",

  'revenue:tuition': "O'qish to'lovi",
  'revenue:bus': 'Avtobus',
  'revenue:dormitory': 'Yotoqxona',
  'revenue:meals': 'Ovqatlanish',
  'revenue:other': 'Boshqa daromad',

  'expense:salary': 'Maosh',
  'expense:rent': 'Ijara',
  'expense:utilities': 'Kommunal',
  'expense:supplies': 'Jihoz va materiallar',
  'expense:repair': "Ta'mirlash",
  'expense:other': 'Boshqa chiqim',
}

/** Hisob kodini o'qiladigan nomga aylantiradi. Noma'lum kod — o'zi ko'rinadi. */
export function accountLabel(account: string): string {
  const known = accountNames[account]
  if (known) return known
  const tail = account.includes(':') ? account.slice(account.indexOf(':') + 1) : account
  return tail.charAt(0).toUpperCase() + tail.slice(1)
}

/** To'lov usuli — FAQAT YORLIQ (SPEC §8.1 Q13), integratsiya yo'q. */
export const paymentMethodLabels: Record<PaymentMethod, string> = {
  cash: 'Naqd',
  card: 'Karta',
  transfer: "O'tkazma",
  online: 'Onlayn',
}

/** Noma'lum usul kelsa ham ekran buzilmasin. */
export function paymentMethodLabel(method: string): string {
  return paymentMethodLabels[method as PaymentMethod] ?? method
}

/** "2026-09-01" → "Sen 2026" */
export function formatMonthLabel(isoDate: string): string {
  return formatMonth(isoDate.slice(0, 7))
}

/** "2026-09-11T09:34:34+00:00" → "11.09.2026, 14:34" (brauzer mintaqasida) */
export function formatDateTime(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const dd = String(d.getDate()).padStart(2, '0')
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const hh = String(d.getHours()).padStart(2, '0')
  const mi = String(d.getMinutes()).padStart(2, '0')
  return `${dd}.${mm}.${d.getFullYear()}, ${hh}:${mi}`
}

/** Belgisi bilan: "+1 200 000 so'm" / "−1 200 000 so'm" / "0 so'm" */
export function formatSignedMoney(amount: number): string {
  if (amount === 0) return formatMoney(0)
  return amount > 0 ? `+${formatMoney(amount)}` : `−${formatMoney(Math.abs(amount))}`
}

/** Foiz — `null` bo'lsa "—" (nolga bo'linish emas, "ma'nosiz" degani, backend qoidasi). */
export function pctText(value: number | null): string {
  return value === null ? '—' : `${value.toFixed(2)}%`
}

/** Musbat — yashil, manfiy — qizil, nol — kulrang. */
export function signClass(amount: number): string {
  if (amount > 0) return 'text-emerald-600'
  if (amount < 0) return 'text-red-600'
  return 'text-slate-400'
}

/** Grafiklarda ishlatiladigan ranglar — Tailwind palitrasidagi qiymatlar. */
export const chartColors = {
  inflow: '#16a34a',
  outflow: '#dc2626',
  balance: '#1f47f5',
  neutral: '#94a3b8',
  grid: '#eef0f4',
} as const

/**
 * O'zgarishlar jurnali (F6.03) turi → o'zbekcha yorliq
 * (`ChangeJournalKind`, `financeReports.ts`).
 */
export const changeKindLabels: Record<string, string> = {
  studentJoined: "O'quvchi qo'shildi",
  studentLeft: 'Obuna yopildi',
  studentArchived: 'Arxivlandi (obuna ochiq)',
  tariffChanged: "Tarif o'zgardi",
  discountRequested: "Chegirma so'raldi",
  discountApproved: 'Chegirma tasdiqlandi',
  discountRejected: 'Chegirma rad etildi',
  discountExpired: 'Chegirma tugadi',
}

/** Noma'lum tur kelsa ham ekran buzilmasin. */
export function changeKindLabel(kind: string): string {
  return changeKindLabels[kind] ?? kind
}

/**
 * Jurnal qatorining rangi — musbat/manfiy/betaraf ta'sirga qarab (studentJoined
 * yashil, studentLeft qizil, qolgani neytral kulrang/ko'k soyasi).
 */
export function changeKindClass(kind: string): string {
  switch (kind) {
    case 'studentJoined':
    case 'discountRejected':
    case 'discountExpired':
      return 'bg-emerald-50 text-emerald-700'
    case 'studentLeft':
      return 'bg-red-50 text-red-700'
    case 'studentArchived':
      return 'bg-amber-50 text-amber-700'
    case 'tariffChanged':
      return 'bg-brand-50 text-brand-700'
    case 'discountRequested':
    case 'discountApproved':
      return 'bg-slate-100 text-slate-600'
    default:
      return 'bg-slate-100 text-slate-600'
  }
}

/** Grafik o'qi uchun qisqa summa: 12 500 000 → "12.5M" */
export function shortAmount(value: number): string {
  const abs = Math.abs(value)
  if (abs >= 1_000_000) return `${(value / 1_000_000).toFixed(1)}M`
  if (abs >= 1_000) return `${Math.round(value / 1_000)}k`
  return String(value)
}
