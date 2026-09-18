/**
 * Tranzaksiya turi katalogi (Kirim/Chiqim) — mijoz yuborgan EduSchool kassa
 * kirim shakli ("Tranzaksiya turi *" — majburiy dropdown) va moliya
 * sozlamalari ekrani (pill-tab, jadval "№ / Nomi / Amallar"), 2026-09-18.
 *
 * `TransactionTypesController` ga to'g'ridan-to'g'ri boradi
 * (`/api/admin/finance/transaction-types`). Shakl `expenseTemplates.ts` bilan
 * bir xil: xatoni bu yerda o'rab qo'ymaydi, sahifa o'zi `billingErrorMessage`
 * bilan o'qiydi.
 *
 * FAQAT IKKITA KIND — Bonus/Jarima uchun EMAS. Ular uchun bu katalog
 * ALLAQACHON mavjud: `api/services/payrollAdjustments.ts` (`adjustment_reasons`,
 * F11.02) — sabab: `SchoolLms.Domain/TransactionTypes.cs` boshidagi izoh.
 */
import { api } from '../client'

const BASE = '/admin/finance/transaction-types'

export type TransactionTypeKind = 'in' | 'out'

export interface TransactionType {
  id: string
  kind: TransactionTypeKind
  name: string
  isActive: boolean
  /** true — migratsiya seed qilgan: faqat nomi o'zgaradi, o'chirib bo'lmaydi. */
  isSeeded: boolean
  /** Ro'yxatdagi tartib (kichigi tepada). */
  position: number
}

export interface TransactionTypeInput {
  kind: TransactionTypeKind
  name: string
  isActive: boolean
  position: number
}

/** Qisman tahrir — `kind` YO'Q: yaratilgandan keyin o'zgarmaydi (server izohi). */
export type TransactionTypePatch = Partial<Omit<TransactionTypeInput, 'kind'>>

export async function getTransactionTypes(kind?: TransactionTypeKind): Promise<TransactionType[]> {
  const { data } = await api.get<TransactionType[]>(BASE, { params: kind ? { kind } : undefined })
  return data
}

export async function createTransactionType(input: TransactionTypeInput): Promise<TransactionType> {
  const { data } = await api.post<TransactionType>(BASE, input)
  return data
}

export async function updateTransactionType(
  id: string,
  patch: TransactionTypePatch,
): Promise<TransactionType> {
  const { data } = await api.put<TransactionType>(`${BASE}/${id}`, patch)
  return data
}

/** Haqiqiy o'chirish — seed qilingan yoki ishlatilgan tur serverda 409 bilan rad etiladi. */
export async function deleteTransactionType(id: string): Promise<void> {
  await api.delete(`${BASE}/${id}`)
}
