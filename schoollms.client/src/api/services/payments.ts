/**
 * To'lov qabul qilish, taqsimlash, storno va chek (SPEC §3.7, §4.1, §4.7).
 *
 * MUZLATILGAN SHARTNOMA (P1-06) — hozircha STUB. Batafsil: `billing.ts`
 * faylining boshidagi izoh.
 *
 * BU YERDA `updatePayment` YOKI `deletePayment` YO'Q — VA HECH QACHON
 * BO'LMAYDI. To'lov o'zgarmas (SPEC §4.1): ilova roli `app_rw` bazada
 * `payments` jadvalida UPDATE/DELETE qila olmaydi (42501 bilan tekshirilgan,
 * `tools/verify-billing-guards.sh`). Xato to'lov `reversePayment` bilan
 * tuzatiladi: original ko'rinib turadi, ustiga qarshi yozuv qo'shiladi.
 * Agar kimdir shu faylga "tahrirlash" funksiyasi qo'shmoqchi bo'lsa —
 * javob yo'q, chunki backend'da bunday endpoint yozilmaydi.
 */
import type { AllocationSuggestion, Payment, PaymentMethod } from '@/types'
// Tayyor bo'lgach ochiladi: import { api } from '../client'
import { notImplemented } from './notImplemented'

/** To'lovning bitta hisob-fakturaga yo'naltiriladigan qismi. */
export interface AllocationInput {
  invoiceId: string
  amount: number
}

/**
 * To'lov qabul qilish so'rovi.
 *
 * DIQQAT (SPEC §4.4): bu yerda `cashierId` ham, `cashShiftId` ham YO'Q.
 * Kassirni server JWT'dan, smenani esa uning ochiq smenasidan oladi.
 * Ochiq smena bo'lmasa — 409.
 */
export interface AcceptPaymentPayload {
  studentId: string
  amount: number
  /** FAQAT YORLIQ — provayder integratsiyasi yo'q (SPEC §8.1 Q13). */
  method: PaymentMethod
  note?: string
  /**
   * Taqsimot. Bo'sh bo'lsa pul avans sifatida taqsimlanmagan qoladi.
   * Yig'indi `amount` dan oshsa — baza trigger'i rad etadi
   * ("Allocation exceeds payment amount").
   */
  allocations: AllocationInput[]
}

/**
 * To'lovni qabul qiladi va chek raqamini beradi. Bitta tranzaksiyada:
 * chek raqami, to'lov, taqsimot, hisob-faktura statusi va ledger yozuvlari.
 *
 * BITTA TO'LOV — BITTA O'QUVCHI (mijoz javobi, SPEC §8.1 Q14). Ikki
 * farzandga to'layotgan ota-ona ikkita chek oladi.
 */
export async function acceptPayment(payload: AcceptPaymentPayload): Promise<Payment> {
  // return (await api.post<Payment>('/cashier/payments', payload)).data
  return notImplemented('POST /cashier/payments', 'P1-11', payload)
}

/**
 * Taqsimot TAKLIFI: summani eng eski qarzdan boshlab bo'lish (FIFO).
 * Bu faqat taklif — kassir uni ekranda o'zgartirishi mumkin, haqiqiy
 * taqsimot `acceptPayment` ga yuborilgan `allocations` bo'ladi.
 */
export async function suggestAllocation(
  studentId: string,
  amount: number,
): Promise<AllocationSuggestion[]> {
  // return (await api.get<AllocationSuggestion[]>('/cashier/payments/suggest-allocation', { params: { studentId, amount } })).data
  return notImplemented('GET /cashier/payments/suggest-allocation', 'P1-11', { studentId, amount })
}

export async function getPayment(id: string): Promise<Payment> {
  // return (await api.get<Payment>(`/billing/payments/${id}`)).data
  return notImplemented(`GET /billing/payments/${id}`, 'P1-11')
}

export interface PaymentFilters {
  studentId?: string
  cashierId?: string
  cashShiftId?: string
  /** "YYYY-MM-DD" */
  from?: string
  to?: string
  method?: PaymentMethod
  /** true = faqat storno qatorlari */
  onlyReversals?: boolean
}

export async function getPayments(filters: PaymentFilters = {}): Promise<Payment[]> {
  // return (await api.get<Payment[]>('/billing/payments', { params: filters })).data
  return notImplemented('GET /billing/payments', 'P1-11', filters)
}

/**
 * STORNO — xato to'lovni tuzatishning YAGONA yo'li.
 *
 * Kassir buni chaqira OLMAYDI (SPEC §4.3): "pulni oldim, keyin storno
 * qildim" — tavsifdagi firibgarlikning aynan o'zi. Sabab majburiy.
 * Original qator tegilmaydi; qarshi qator qo'shiladi.
 */
export async function reversePayment(id: string, reason: string): Promise<Payment> {
  // return (await api.post<Payment>(`/admin/billing/payments/${id}/reverse`, { reason })).data
  return notImplemented(`POST /admin/billing/payments/${id}/reverse`, 'P1-11', { reason })
}

/** Chek PDF'i (blob). Kassir ekranida yuklab olish/chop etish uchun. */
export async function getReceiptPdf(id: string): Promise<Blob> {
  // return (await api.get(`/billing/payments/${id}/receipt.pdf`, { responseType: 'blob' })).data
  return notImplemented(`GET /billing/payments/${id}/receipt.pdf`, 'P1-12')
}

/**
 * Chekni ota-onaning Telegram chatiga yuborish (SPEC §4.7).
 * Ota-ona ro'yxatdan o'tmagan bo'lsa `false` — bu xato emas, kutilgan holat.
 */
export async function sendReceiptToGuardian(id: string): Promise<{ sent: boolean }> {
  // return (await api.post<{ sent: boolean }>(`/billing/payments/${id}/send-receipt`)).data
  return notImplemented(`POST /billing/payments/${id}/send-receipt`, 'P1-12')
}
