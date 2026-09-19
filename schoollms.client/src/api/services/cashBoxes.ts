/**
 * KASSALAR — mijoz smenani butunlay olib tashlashni so'radi (2026-09-18):
 * "bizni tizimda smena degan tushuncha umuman bo'lmasin ... shunchaki kassa
 * degan narsa bo'lsin xolos, bizda bir nechta kassa bo'lishi mumkin, ular
 * har bir alohida pul kirim chiqim qilishi va o'zaro o'tkazma qilishi
 * mumkin." Bu fayl shu talabning API klienti.
 *
 * Bir nechta KASSA (cash box) bor, har birining o'z qoldig'i va to'rtta
 * amali: Kirim, Chiqim, Ko'chirish (boshqa kassaga), Ayirboshlash (bir usuldan
 * ikkinchisiga, masalan naqddan bankka). Smena, ochish/yopish, sanalgan naqd,
 * nomuvofiqlik — bularning HECH BIRI YO'Q: shu tushunchaning o'zi olib
 * tashlanmoqda.
 *
 * Backend (`docs/modules` da SPEC bo'lishi mumkin) bu vazifa bilan PARALLEL
 * yozilmoqda — marshrutlar vazifa tavsifidagi shartnomadan olingan:
 *
 *   GET    /api/admin/cash-boxes
 *   POST   /api/admin/cash-boxes
 *   PUT    /api/admin/cash-boxes/{id}
 *   POST   /api/admin/cash-boxes/{id}/in
 *   POST   /api/admin/cash-boxes/{id}/out
 *   POST   /api/admin/cash-boxes/{id}/transfer
 *   POST   /api/admin/cash-boxes/{id}/exchange
 *   GET    /api/admin/cash-boxes/transactions?from=&to=&boxId=&q=
 *   POST   /api/admin/cash-boxes/transactions/{id}/cancel
 *
 * `kind` va `status` backend'dan qanday matn kelishi ANIQ emas (backend
 * hali yozilyapti) — shuning uchun ular bu yerda ochiq `string` va
 * ko'rsatilishda noma'lum qiymat ham o'ziga o'zi ko'rinadi
 * (`reportLabels.ts` dagi `paymentMethodLabel` bilan bir xil naqsh).
 *
 * O'QUVCHI TO'LOVI BU YERDAN O'TMAYDI. Hisob-fakturaga taqsimlanadigan
 * to'lov ilgarigidek `cashier.ts` (`/cash/payments`) orqali ketadi — bu
 * fayldagi `in` amali umumiy (masalan, boshlang'ich mablag' yoki boshqa
 * kirim), na hisob-faktura, na FIFO taqsimotni biladi.
 */
import type { PaymentMethod } from '@/types'
import { api } from '../client'

/* ========================================================================
   Kassa (cash box)
   ======================================================================== */

export interface CashBox {
  id: string
  name: string
  responsibleName: string | null
  balance: number
  /** Usul kesimida qoldiq — jami to'rtta usul chiqishi shart emas. */
  byMethod: Partial<Record<PaymentMethod, number>>
  isDefault: boolean
  isActive: boolean
}

export interface CashBoxInput {
  name: string
  responsibleUserId?: string
  isDefault?: boolean
}

export interface CashBoxUpdateInput {
  name?: string
  responsibleUserId?: string
  isDefault?: boolean
  isActive?: boolean
}

export async function getCashBoxes(): Promise<CashBox[]> {
  const res = await api.get<CashBox[]>('/admin/cash-boxes')
  return res.data
}

export async function createCashBox(payload: CashBoxInput): Promise<CashBox> {
  const res = await api.post<CashBox>('/admin/cash-boxes', payload)
  return res.data
}

export async function updateCashBox(id: string, payload: CashBoxUpdateInput): Promise<CashBox> {
  const res = await api.put<CashBox>(`/admin/cash-boxes/${id}`, payload)
  return res.data
}

/* ========================================================================
   To'rt amal: Kirim, Chiqim, Ko'chirish, Ayirboshlash
   ======================================================================== */

export interface CashBoxInPayload {
  amount: number
  method: PaymentMethod
  note?: string
  /** Ixtiyoriy — pul aynan qaysi o'quvchiga tegishli ekanini yorliqlaydi. */
  studentId?: string
  /**
   * Tranzaksiya turi (`api/services/transactionTypes.ts`, kind `in`).
   * Backend darajasida ixtiyoriy (`CashBoxPayInRequest.TransactionTypeId`
   * izohi), lekin `PlainIncomeForm` uni MAJBURIY qiladi — mijoz yuborgan
   * EduSchool shaklidagi "Tranzaksiya turi *" talabi.
   */
  transactionTypeId?: string
  /**
   * Kirim qaysi KUN bilan yozilishi — "YYYY-MM-DD" (ixtiyoriy; bo'lmasa
   * bugun). Mijoz so'radi (2026-09-18): "oldingi sana uchun tanlash mumkin
   * bo'lsin". Server chegaralaydi: kelajak yo'q, bir yildan uzoq orqaga ham
   * yo'q (`CashBoxPayInRequest.Date`).
   */
  date?: string
}

export interface CashBoxOutPayload {
  amount: number
  method: PaymentMethod
  note?: string
  /** Chiqim turi (`transactionTypes.ts`, kind `out`) — serverda `out` ekani tekshiriladi. */
  transactionTypeId?: string
  /** Qaysi kun bilan yozilishi — "YYYY-MM-DD" (`CashBoxInPayload.date` bilan bir xil qoida). */
  date?: string
}

export interface CashBoxTransferPayload {
  toBoxId: string
  amount: number
  method: PaymentMethod
  note?: string
  date?: string
}

export interface CashBoxExchangePayload {
  amount: number
  fromMethod: PaymentMethod
  toMethod: PaymentMethod
  note?: string
  date?: string
}

export async function cashBoxIn(id: string, payload: CashBoxInPayload): Promise<void> {
  await api.post(`/admin/cash-boxes/${id}/in`, payload)
}

export async function cashBoxOut(id: string, payload: CashBoxOutPayload): Promise<void> {
  await api.post(`/admin/cash-boxes/${id}/out`, payload)
}

export async function cashBoxTransfer(id: string, payload: CashBoxTransferPayload): Promise<void> {
  await api.post(`/admin/cash-boxes/${id}/transfer`, payload)
}

export async function cashBoxExchange(id: string, payload: CashBoxExchangePayload): Promise<void> {
  await api.post(`/admin/cash-boxes/${id}/exchange`, payload)
}

/* ========================================================================
   Tranzaksiyalar jadvali
   ======================================================================== */

/** Backend'dan qanday kelishi hali aniq emas — noma'lum qiymat ham ko'rsatiladi. */
/**
 * Server AYNAN shu qiymatlarni yuboradi (`CashBoxTransactionKind.cs`):
 * `pay_in` / `pay_out` / `transfer` / `exchange`. Ilgari bu yerda `in`/`out`
 * yozilgan edi va jadvalda yorliq o'rniga xom `pay_in` chiqib, "Tranzaksiya"
 * filtri esa hech qachon mos kelmasdi.
 */
export type CashTransactionKind = 'pay_in' | 'pay_out' | 'transfer' | 'exchange' | string

/** Server: `posted` | `cancelled` | `reversal` (`CashBoxService.DisplayStatus`). */
export type CashTransactionStatus = 'posted' | 'cancelled' | 'reversal' | string

export interface CashBoxTransactionRow {
  id: string
  no: number
  date: string
  who: string
  contractNo: string | null
  amount: number
  kind: CashTransactionKind
  method: PaymentMethod
  status: CashTransactionStatus
  /** Tanlangan tranzaksiya turining nomi (bo'lsa) — hozircha faqat Kirimda. */
  transactionTypeName: string | null
  /** Kassir yozgan izoh (jadvaldagi "Izoh" ustuni). */
  note: string | null
  /** Bekor qilish sababi — bekor qilingan qatorda va stornoning o'zida. */
  cancelReason: string | null
  /** Yozuv lahzasi (ISO) — jadvalda sana yonidagi soat va chek uchun. */
  createdAt: string
}

export interface CashBoxTransactionsResult {
  rows: CashBoxTransactionRow[]
  totalsByMethod: Partial<Record<PaymentMethod, number>>
  inTotal: number
  outTotal: number
}

export interface CashBoxTransactionsFilters {
  /** "YYYY-MM-DD" */
  from?: string
  /** "YYYY-MM-DD" */
  to?: string
  boxId?: string
  /** Chek/shartnoma raqami yoki F.I.Sh. bo'yicha qidiruv. */
  q?: string
}

export async function getCashBoxTransactions(
  filters: CashBoxTransactionsFilters = {},
  signal?: AbortSignal,
): Promise<CashBoxTransactionsResult> {
  const res = await api.get<CashBoxTransactionsResult>('/admin/cash-boxes/transactions', {
    params: filters,
    signal,
  })
  return res.data
}

export async function cancelCashBoxTransaction(id: string, reason: string): Promise<void> {
  await api.post(`/admin/cash-boxes/transactions/${id}/cancel`, { reason })
}
