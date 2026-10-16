/**
 * Avtomatik xabarlar ekranlari uchun umumiy yorliqlar va yordamchilar
 * (qoidalar va "Tarix va statistika" bir xil so'zlarni ishlatsin).
 */
import type { AxiosError } from 'axios'
import type { AutoMessageKey, AutoMessageLogStatus, AutoMessageRunStatus } from '@/api/services/autoMessages'

export const TITLES: Record<AutoMessageKey, { title: string; hint: string }> = {
  absence: {
    title: 'Kelmaganlik xabari',
    hint: "Kelmagan o'quvchining ota-onasiga — hamshira belgilagan davomat bo'yicha. Faqat kesish vaqtigacha boshlangan darslar hisoblanadi; davomati to'liq olinmagan sinf o'tkazib yuboriladi. Tugma bilan yuborilganlarga qayta ketmaydi.",
  },
  invoice: {
    title: 'Yangi oylik hisob',
    hint: "Oy hisob-fakturasi chiqqach — shu oy summasi va jami to'lanmagan. Hisob hali chiqmagan bo'lsa, keyingi 2 kun ichida qayta urinadi.",
  },
  debt: {
    title: 'Qarz eslatmasi',
    hint: 'Har oy belgilangan kuni — muddati o\'tgan qarzi eng kam summadan ko\'p bo\'lganlarga ("Qarzdorlar" hisobotidagi raqam).',
  },
  birthday: {
    title: "Tug'ilgan kun tabrigi",
    hint: "Tug'ilgan kuni o'quvchining ota-onasiga. 29-fevralda tug'ilganlar boshqa yillari 28-fevralda tabriklanadi.",
  },
  payment: {
    title: "To'lov qabul qilinganda",
    hint: "Kassada to'lov qabul qilinganda chek (PDF) bilan birga. Yoqilsa, chek ostidagi matn shu shablondan olinadi (1024 belgidan uzun bo'lsa — chekdan keyin alohida xabar). O'chiq bo'lsa ham chek doim yuboriladi.",
  },
}

export const LOG_STATUS: Record<AutoMessageLogStatus, { label: string; cls: string }> = {
  scheduled: { label: 'Rejalashtirilgan', cls: 'bg-sky-50 text-sky-700' },
  pending_approval: { label: 'Tasdiq kutmoqda', cls: 'bg-amber-50 text-amber-700' },
  sent: { label: 'Yuborildi', cls: 'bg-emerald-50 text-emerald-700' },
  failed: { label: 'Xatolik', cls: 'bg-red-50 text-red-700' },
  no_telegram: { label: "Xatolik · Telegram yo'q", cls: 'bg-slate-100 text-slate-500' },
  cancelled: { label: "To'xtatilgan", cls: 'bg-slate-100 text-slate-500 line-through decoration-slate-300' },
}

export const RUN_STATUS: Record<AutoMessageRunStatus, { label: string; cls: string }> = {
  scheduled: { label: 'Rejalashtirilgan', cls: 'bg-sky-50 text-sky-700' },
  pending_approval: { label: 'Tasdiq kutmoqda', cls: 'bg-amber-50 text-amber-700' },
  sending: { label: 'Yuborilmoqda', cls: 'bg-brand-50 text-brand-700' },
  done: { label: 'Yuborildi', cls: 'bg-emerald-50 text-emerald-700' },
  cancelled: { label: "To'xtatilgan", cls: 'bg-slate-100 text-slate-500' },
}

export function errorText(err: unknown, fallback: string): string {
  return (err as AxiosError<{ message?: string }>)?.response?.data?.message ?? fallback
}

export function fmtDateTime(iso: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('uz-UZ', {
    timeZone: 'Asia/Tashkent', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
  })
}

/** "yyyy-MM-dd" — Toshkent vaqti bilan, `daysAgo` kun oldin. */
export function isoDay(daysAgo = 0): string {
  return new Date(Date.now() - daysAgo * 86_400_000).toLocaleDateString('en-CA', { timeZone: 'Asia/Tashkent' })
}

/** "2026-10-13" → "13.10.2026", "2026-10" → "10.2026". */
export function fmtPeriod(period: string): string {
  const [y, m, d] = period.split('-')
  return d ? `${d}.${m}.${y}` : `${m}.${y}`
}
