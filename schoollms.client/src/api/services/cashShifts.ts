/**
 * Kassa smenasi: ochish, yopish, Z-hisobot, nomuvofiqlik (SPEC §4.2, §4.6).
 *
 * MUZLATILGAN SHARTNOMA (P1-06) — hozircha STUB. Batafsil: `billing.ts`
 * faylining boshidagi izoh.
 *
 * MUHIM (SPEC §4.4): `cashierId` va `closedBy` so'rovda YUBORILMAYDI —
 * server ularni JWT'dan oladi. Shuning uchun bu funksiyalarning birortasida
 * ham kassir id'si parametri YO'Q, va bo'lmasligi kerak.
 */
import type { CashShift, ZReport } from '@/types'
// Tayyor bo'lgach ochiladi: import { api } from '../client'
import { notImplemented } from './notImplemented'

/**
 * Kassirning joriy OCHIQ smenasi. Yo'q bo'lsa `null` — kassa ekrani shunda
 * "smenani oching" holatini ko'rsatadi.
 *
 * Backend 404 emas, `null` qaytaradi: "smena yo'q" — kutilgan holat, xato emas.
 */
export async function getCurrentShift(): Promise<CashShift | null> {
  // return (await api.get<CashShift | null>('/cashier/shifts/current')).data
  return notImplemented('GET /cashier/shifts/current', 'P1-10')
}

/**
 * Smena ochish. Ochiq smena allaqachon bo'lsa backend 409 qaytaradi —
 * buni bazadagi `ux_cash_shifts_one_open_per_cashier` indeksi ham kafolatlaydi.
 *
 * @param openingFloat Smena boshidagi kassadagi pul (sukut 0).
 */
export async function openShift(openingFloat: number): Promise<CashShift> {
  // return (await api.post<CashShift>('/cashier/shifts/open', { openingFloat })).data
  return notImplemented('POST /cashier/shifts/open', 'P1-10', { openingFloat })
}

/**
 * Smenani yopish. `countedCash` — kassir QO'LDA sanagan naqd, MAJBURIY.
 * `expectedCash` ni server ledger'dan hisoblaydi, `variance` ni esa BAZA
 * (generated column) — ya'ni yopilgandan keyin uni hech kim tuzata olmaydi.
 */
export async function closeShift(
  id: string,
  countedCash: number,
  note?: string,
): Promise<CashShift> {
  // return (await api.post<CashShift>(`/cashier/shifts/${id}/close`, { countedCash, note })).data
  return notImplemented(`POST /cashier/shifts/${id}/close`, 'P1-10', { countedCash, note })
}

/** Smena yakuni: usullar va toifalar kesimi, chek raqamlari oralig'i. */
export async function getZReport(id: string): Promise<ZReport> {
  // return (await api.get<ZReport>(`/cashier/shifts/${id}/z-report`)).data
  return notImplemented(`GET /cashier/shifts/${id}/z-report`, 'P1-10')
}

export interface ShiftFilters {
  cashierId?: string
  /** "YYYY-MM-DD" */
  from?: string
  to?: string
  status?: 'open' | 'closed'
  /** true = faqat nomuvofiqligi nol bo'lmaganlar (direktor paneli) */
  onlyWithVariance?: boolean
}

/**
 * Smenalar ro'yxati. Kassirlar kesimi — faqat admin/direktor uchun
 * (SPEC §4.3 "See variance report across cashiers": kassirga ⛔).
 */
export async function getShifts(filters: ShiftFilters = {}): Promise<CashShift[]> {
  // return (await api.get<CashShift[]>('/admin/billing/shifts', { params: filters })).data
  return notImplemented('GET /admin/billing/shifts', 'P1-10', filters)
}
