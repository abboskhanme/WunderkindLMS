/**
 * Chiqimlar (P1-17 sahifasi uchun klient).
 *
 * SHARTNOMA — `SchoolLms.Application/Billing/ExpenseService.cs` ning ko'zgusi:
 *
 *   GET  /admin/expenses?from&to&category&status
 *   GET  /admin/expenses/approval-policy      → { threshold }
 *   GET  /admin/expenses/{id}
 *   POST /admin/expenses            { onDate, category, amount, method, note? }
 *   POST /admin/expenses/{id}/approve   { method }
 *   POST /admin/expenses/{id}/reverse   { reason }
 *
 * IKKI TUZATILGAN NOSOZLIK (finance-parity.md §1.1)
 * -------------------------------------------------
 * F1.01 — `ExpenseInput` da `method` YO'Q edi, server esa uni TALAB qiladi
 *   (`CreateExpenseRequest.Method`). Ya'ni interfeysdan kiritilgan har bir
 *   chiqim `400 invalid_method` bilan rad etilardi.
 * F1.02 — `approveExpense` tanasiz `POST` qilardi, server esa `{ method }`
 *   kutadi (usul jurnalning kredit satrini belgilaydi: naqd — kassadan,
 *   qolgani — bankdan). Tasdiqlash har safar 400 bilan yiqilardi.
 *   Shu yerda ikkinchi xato ham bor edi: holat KLIENTDA, 5 000 000 so'mlik
 *   konstanta bo'yicha hisoblanardi. Chegara esa sozlama
 *   (`billing_settings.expense_approval_threshold`) — o'zgargan kuni ekran
 *   tasdiq kutayotgan chiqimni "yozib olingan" deb ko'rsatardi. Endi holat
 *   ham, chegara ham SERVERDAN keladi.
 *
 * "Rad etish" ATAYLAB yo'q: chiqim — sodir bo'lgan fakt, pul allaqachon
 * ketgan. Uni "bo'lmagan" deb e'lon qilib bo'lmaydi; xato yozuv STORNO
 * bilan tuzatiladi (SPEC §4.1). Shuning uchun tasdiq navbatidagi variantlar
 * ikkita: TASDIQLASH yoki STORNO.
 */
import type { PaymentMethod } from '@/types'
import { api } from '../client'

const BASE = '/admin/expenses'

/**
 * Serverdagi `ExpenseStatus` — jurnaldan hisoblanadi, ustun emas:
 *  - `pending`  — chegaradan yuqori, jurnalga hali tushmagan (tasdiq navbati);
 *  - `posted`   — jurnalga tushgan;
 *  - `reversed` — storno qilingan.
 */
export type ExpenseStatus = 'pending' | 'posted' | 'reversed'

/**
 * Server javobi — `ExpenseDto` ning AYNAN ko'zgusi (camelCase). Maydon
 * qo'shilsa, avval serverda qo'shiladi: bu yerda "bo'lishi mumkin" degan
 * ixtiyoriy maydon saqlash aynan F1.01/F1.02 ga olib kelgan edi.
 */
export interface ExpenseRecord {
  id: string
  /** "YYYY-MM-DD" — buxgalteriya sanasi. */
  onDate: string
  category: string
  /** Jurnaldagi chiqim hisobi: `expense:<toifa>`. */
  account: string
  amount: number
  note: string | null
  status: ExpenseStatus
  /** Pul qayerdan chiqdi: `cash` | `bank`. Tasdiq kutayotganida `null`. */
  settlementAccount: string | null
  postedOn: string | null
  createdBy: string
  createdByName: string
  createdAt: string
  /** Tasdiqlovchi yaratuvchidan BOSHQA shaxs bo'lishi shart (SPEC §4.5). */
  approvedBy: string | null
  approvedByName: string | null
  reversedOn: string | null
  reversedBy: string | null
  reversedByName: string | null
  /** Storno sababi (SPEC §4.3 — sabab majburiy). */
  reversalReason: string | null
  /** Maosh chiqimi kimga berilgani (faqat `salary` toifasida). */
  teacherId: string | null
  teacherName: string | null
}

/** Ekrandagi holat — serverning `status` iga qarab, chegara HISOBLANMAYDI. */
export type ExpenseViewState = 'approved' | 'pending' | 'recorded' | 'reversed'

/**
 * Chiqim holati ekran uchun:
 *  - storno qilingan               → `reversed`;
 *  - jurnalga tushgan, tasdiqlovchi bor → `approved`;
 *  - jurnalga tushgan, tasdiqsiz   → `recorded` (chegaradan past edi);
 *  - jurnalga tushmagan            → `pending` (tasdiq navbati).
 */
export function expenseState(e: ExpenseRecord): ExpenseViewState {
  if (e.status === 'reversed') return 'reversed'
  if (e.status === 'pending') return 'pending'
  return e.approvedBy ? 'approved' : 'recorded'
}

/** Shu chiqim ikkinchi tasdiqni kutyaptimi? */
export function needsApproval(e: ExpenseRecord): boolean {
  return e.status === 'pending'
}

export interface ExpenseFilters {
  /** "YYYY-MM-DD" */
  from?: string
  to?: string
  category?: string
}

export interface ExpenseInput {
  /** "YYYY-MM-DD" */
  onDate: string
  category: string
  amount: number
  /** Pul qaysi usulda chiqdi — server uni TALAB qiladi (F1.01). */
  method: PaymentMethod
  note?: string
}

export async function getExpenses(filters: ExpenseFilters = {}): Promise<ExpenseRecord[]> {
  const { data } = await api.get<ExpenseRecord[]>(BASE, { params: filters })
  return data
}

/**
 * Ikkinchi tasdiq chegarasi — SERVERDAN (SPEC §4.5). Klientda konstanta
 * SAQLANMAYDI: sozlama o'zgarganda ekran jimgina yolg'on gapira boshlardi.
 */
export async function getExpenseApprovalThreshold(): Promise<number> {
  const { data } = await api.get<{ threshold: number }>(`${BASE}/approval-policy`)
  return data.threshold
}

/** Chiqim yozish. `created_by` JWT'dan (SPEC §4.4). */
export async function createExpense(input: ExpenseInput): Promise<ExpenseRecord> {
  const { data } = await api.post<ExpenseRecord>(BASE, input)
  return data
}

/**
 * Tasdiqlash — faqat direktor va faqat BOSHQA shaxs (SPEC §4.5).
 *
 * `method` MAJBURIY: chegaradan yuqori chiqimda pul qaysi hisobdan chiqishi
 * aynan shu lahzada hal bo'ladi (jurnalning kredit satri), chunki `expenses`
 * jadvalida usul ustuni yo'q.
 */
export async function approveExpense(id: string, method: PaymentMethod): Promise<ExpenseRecord> {
  const { data } = await api.post<ExpenseRecord>(`${BASE}/${id}/approve`, { method })
  return data
}

/**
 * STORNO — chiqimni tuzatishning YAGONA yo'li.
 *
 * O'chirish endpoint'i yo'q va bo'lmaydi (SPEC §4.1): yozuv o'chirilmaydi,
 * uning ustiga teskari yozuv qo'yiladi. Sabab majburiy.
 */
export async function reverseExpense(id: string, reason: string): Promise<ExpenseRecord> {
  const { data } = await api.post<ExpenseRecord>(`${BASE}/${id}/reverse`, { reason })
  return data
}
