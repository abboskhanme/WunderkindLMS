/**
 * Kassadan pul topshirish (F1.04) — `CashHandoversController.cs` ning ko'zgusi.
 *
 *   POST /cash/handovers               { amount, destination, note? }
 *   POST /cash/handovers/{id}/reverse  { reason }
 *   GET  /cash/handovers?shiftId&cashierId&from&to&destination
 *   GET  /cash/handovers/{id}
 *
 * NIMA UCHUN KERAK
 * ----------------
 * Kassadagi naqd pul faqat chiqim orqali chiqmaydi: kattaroq qismi bankka
 * topshiriladi yoki direktorning seyfiga beriladi. Bu yozuv bo'lmasa smena
 * yopilganda "kutilgan naqd" haqiqatdan har kuni uzoqlashadi.
 *
 * SPEC §4.4 — `cashShiftId` ham, `createdBy` ham HECH QAYERDA yuborilmaydi:
 * server smenani kassirning ochiq smenasidan, shaxsni JWT'dan oladi. Tanada
 * uchrasa 400 `identity_in_body` qaytaradi.
 *
 * O'CHIRISH VA TAHRIRLASH YO'Q — va bo'lmaydi: jadval bazada faqat
 * qo'shiladi (`app_rw` da UPDATE/DELETE yo'q). Xato topshiriq `reverse`
 * bilan tuzatiladi va pul storno qiluvchining javoniga qaytadi.
 */
import { api } from '../client'

/** Pul qayerga ketdi. Server yopiq ro'yxat bilan tekshiradi. */
export type CashHandoverDestination = 'bank' | 'safe'

/** Server javobi — `CashHandoverDto` ning AYNAN ko'zgusi (camelCase). */
export interface CashHandover {
  id: string
  /** Qaysi smenadan chiqdi. */
  cashShiftId: string
  /** Smenaning egasi (topshiriqni admin yozgan bo'lishi ham mumkin). */
  cashierId: string
  cashierName: string
  amount: number
  destination: CashHandoverDestination
  note: string | null
  createdBy: string
  createdByName: string
  createdAt: string
  /** Storno bo'lsa — qaysi topshiriqni bekor qilyapti. */
  reversalOf: string | null
  /** Shu topshiriq keyinchalik storno qilinganmi. */
  reversed: boolean
}

export interface CashHandoverInput {
  amount: number
  destination: CashHandoverDestination
  note?: string
}

export interface CashHandoverFilters {
  shiftId?: string
  /** Admin/direktor uchun; kassirga server baribir o'zinikini beradi. */
  cashierId?: string
  /** "YYYY-MM-DD" */
  from?: string
  to?: string
  destination?: CashHandoverDestination
}

/** O'zbekcha yorliq — ekranlarda bitta manbadan. */
export const cashHandoverDestinationLabels: Record<CashHandoverDestination, string> = {
  bank: 'Bankka topshirildi',
  safe: 'Direktor seyfiga berildi',
}

/** Topshiriqni yozish. Ochiq smenasiz server 409 `no_open_shift` qaytaradi. */
export async function recordCashHandover(input: CashHandoverInput): Promise<CashHandover> {
  const { data } = await api.post<CashHandover>('/cash/handovers', input)
  return data
}

/**
 * STORNO — topshiriqni tuzatishning YAGONA yo'li. Pul storno qiluvchining
 * ochiq smenasiga qaytadi, ya'ni uning kutilgan naqdi OSHADI.
 */
export async function reverseCashHandover(id: string, reason: string): Promise<CashHandover> {
  const { data } = await api.post<CashHandover>(`/cash/handovers/${id}/reverse`, { reason })
  return data
}

/** Registr. Kassirga server faqat o'z smenalarini beradi. */
export async function getCashHandovers(
  filters: CashHandoverFilters = {},
): Promise<CashHandover[]> {
  const { data } = await api.get<CashHandover[]>('/cash/handovers', { params: filters })
  return data
}
