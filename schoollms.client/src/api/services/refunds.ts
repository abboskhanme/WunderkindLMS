/**
 * O'quvchiga pul qaytarish (F1.05) — `StudentRefundsController.cs` ning ko'zgusi.
 *
 *   POST /admin/finance/refunds                       { studentId, amount, method, reason }
 *   POST /admin/finance/refunds/{id}/reverse           { reason }
 *   POST /admin/finance/refunds/{id}/approve           —
 *   POST /admin/finance/refunds/{id}/reject            { reason }
 *   GET  /admin/finance/refunds?studentId&status
 *   GET  /admin/finance/refunds/{id}
 *   GET  /admin/finance/refunds/students/{studentId}/advance
 *
 * IKKI QAVATLI NAZORAT (SPEC §4.5) — admin SO'RAYDI, direktor TASDIQLAYDI
 * yoki RAD ETADI. So'ragan shaxs o'zi tasdiqlay olmaydi (server 403
 * `self_approval` bilan rad etadi; interfeys buni ko'rsatmasligi kerak).
 *
 * O'CHIRISH VA TAHRIRLASH YO'Q — va bo'lmaydi: jadval bazada faqat
 * qo'shiladi (`app_rw` da UPDATE/DELETE yo'q, to'rtta "qaror" ustunidan
 * boshqa). Xato (tasdiqlangan) qaytarim `reverse` + `approve` bilan
 * tuzatiladi: yangi (storno) qator so'raladi va u ham tasdiqlanadi.
 *
 * SPEC §4.4 — `requestedBy`, `approvedBy`, `cashShiftId` va h.k. HECH
 * QAYERDA yuborilmaydi: server ularni JWT'dan yoki jurnaldan aniqlaydi.
 * Tanada uchrasa 400 `identity_in_body` qaytaradi.
 */
import { api } from '../client'
import type { PaymentMethod } from '@/types'

/** Qaytarimning HISOBLANGAN holati — ustun emas, serverda jurnaldan chiqariladi. */
export type StudentRefundStatus = 'pending' | 'approved' | 'rejected' | 'reversal' | 'reversed'

/** Server javobi — `StudentRefundDto` ning AYNAN ko'zgusi (camelCase). */
export interface StudentRefund {
  id: string
  studentId: string
  studentName: string
  amount: number
  method: PaymentMethod
  reason: string
  requestedBy: string
  requestedByName: string
  requestedAt: string
  approvedBy: string | null
  approvedByName: string | null
  approvedAt: string | null
  /** Naqd qaytarim qaysi smenadan chiqdi/qaytdi. */
  cashShiftId: string | null
  rejectedReason: string | null
  /** Storno bo'lsa — qaysi qaytarimni bekor qilyapti. */
  reversalOf: string | null
  /** Shu (oddiy) qaytarim uchun TASDIQLANGAN storno so'rovi bormi. */
  reversed: boolean
  status: StudentRefundStatus
}

export interface RequestRefundInput {
  studentId: string
  amount: number
  method: PaymentMethod
  reason: string
}

export interface RefundFilters {
  studentId?: string
  status?: StudentRefundStatus
}

/** O'zbekcha yorliq — ekranlarda bitta manbadan. */
export const refundStatusLabels: Record<StudentRefundStatus, string> = {
  pending: 'Tasdiq kutmoqda',
  approved: 'Tasdiqlangan',
  rejected: 'Rad etilgan',
  reversal: 'Storno (tasdiqlangan)',
  reversed: 'Storno qilingan',
}

/**
 * Yangi qaytarim so'raydi. Har doim `pending` holatda tug'iladi — pul
 * `approveRefund` chaqirilmaguncha hech qayerga chiqmaydi. So'ralgan summa
 * o'quvchining joriy avansidan katta bo'lsa server 400 `insufficient_advance`
 * qaytaradi.
 */
export async function requestRefund(input: RequestRefundInput): Promise<StudentRefund> {
  const { data } = await api.post<StudentRefund>('/admin/finance/refunds', input)
  return data
}

/**
 * Allaqachon TASDIQLANGAN qaytarimni bekor qilishni so'raydi — yangi,
 * `pending` qator. Sabab majburiy.
 */
export async function requestRefundReversal(id: string, reason: string): Promise<StudentRefund> {
  const { data } = await api.post<StudentRefund>(`/admin/finance/refunds/${id}/reverse`, { reason })
  return data
}

/**
 * Tasdiqlaydi: oddiy qaytarim jurnalga tushadi, storno so'rovi esa asl
 * qaytarimning yozuvini teskari qiladi. FAQAT direktor.
 */
export async function approveRefund(id: string): Promise<StudentRefund> {
  const { data } = await api.post<StudentRefund>(`/admin/finance/refunds/${id}/approve`, {})
  return data
}

/** Qaytarim (yoki storno) so'rovini rad etadi — pul harakati bo'lmaydi. FAQAT direktor. */
export async function rejectRefund(id: string, reason: string): Promise<StudentRefund> {
  const { data } = await api.post<StudentRefund>(`/admin/finance/refunds/${id}/reject`, { reason })
  return data
}

/** Ro'yxat — o'quvchi va/yoki holat bo'yicha filtr, yangisidan eskisiga. */
export async function getRefunds(filters: RefundFilters = {}): Promise<StudentRefund[]> {
  const { data } = await api.get<StudentRefund[]>('/admin/finance/refunds', { params: filters })
  return data
}

/** Bitta qaytarim. */
export async function getRefund(id: string): Promise<StudentRefund> {
  const { data } = await api.get<StudentRefund>(`/admin/finance/refunds/${id}`)
  return data
}

/**
 * O'quvchining JORIY avansi (qaytarimlar ayirilgan) — so'rov formasi "eng
 * ko'pi shuncha" chegarasini shu yerdan ko'rsatadi.
 */
export async function getRefundAdvance(studentId: string): Promise<number> {
  const { data } = await api.get<number>(`/admin/finance/refunds/students/${studentId}/advance`)
  return data
}
