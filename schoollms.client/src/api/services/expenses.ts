/**
 * Chiqimlar (P1-17 sahifasi uchun klient).
 *
 * HOLAT — DIQQAT
 * --------------
 * `expenses` jadvali bazada bor va to'la (5 qator, 4 tasi tasdiqlangan),
 * lekin `/api/admin/billing/expenses` marshruti hali YOZILMAGAN — bugun u
 * 404 qaytaradi. Endpoint P1-13 zimmasida.
 *
 * Shuning uchun bu fayl `billing.ts` dagi kabi `notImplemented(...)`
 * tashlamaydi, balki HAQIQIY chaqiruvni qiladi: backend ulangan kunda
 * sahifa o'z-o'zidan ishlay boshlaydi, bugun esa 404 ni sahifa "bu bo'lim
 * hali ulanmagan" holati sifatida ko'rsatadi (`isEndpointMissing`).
 * Bo'sh massiv qaytarish — "ishlayotgandek ko'rinadigan ekran" — ataylab
 * qilinmadi.
 *
 * SHARTNOMA — TO'RT marshrut, `docs/PENDING_WIRING.md` da yozilgan:
 *
 *   GET  /admin/billing/expenses?from&to&category
 *   POST /admin/billing/expenses            { onDate, category, amount, note? }
 *   POST /admin/billing/expenses/{id}/approve
 *   POST /admin/billing/expenses/{id}/reverse   { reason }
 *
 * "Rad etish" ATAYLAB yo'q: chiqim — sodir bo'lgan fakt, pul allaqachon
 * ketgan. Uni "bo'lmagan" deb e'lon qilib bo'lmaydi; xato yozuv STORNO
 * bilan tuzatiladi (SPEC §4.1). Shuning uchun tasdiq navbatidagi variantlar
 * ikkita: TASDIQLASH yoki STORNO.
 */
import type { Expense } from '@/types'
import { api } from '../client'

const BASE = '/admin/expenses'

/**
 * Ikkinchi tasdiq chegarasi (SPEC §4.5 — "Expense above N so'm").
 *
 * Qiymat bazadagi seed bilan mos: 4 600 000 so'mlik chiqim tasdiqsiz,
 * 7 400 000 dan yuqorilari esa tasdiqlangan holda kelgan.
 *
 * DIQQAT: bu FAQAT interfeys uchun. Haqiqiy qoidani server qo'llaydi
 * (`ck_expenses_approver_differs` + `FinanceAction.ApproveExpense`).
 */
export const EXPENSE_APPROVAL_THRESHOLD = 5_000_000

/**
 * Server javobi. `Expense` (muzlatilgan DTO) + kelajakda qo'shilishi
 * mumkin bo'lgan ixtiyoriy maydonlar:
 *  - `createdById` — ikki qavatli nazorat uchun (hozir faqat ism keladi);
 *  - `reversalOf` / `reversedBy` / `reversalReason` — storno zanjiri.
 */
export type ExpenseRecord = Expense & {
  createdById?: string
  approvedById?: string
  /** Bu qator storno bo'lsa — qaysi chiqimni bekor qilgani. */
  reversalOf?: string
  /** Bu chiqim keyinchalik storno qilingan bo'lsa — storno qatori id'si. */
  reversedBy?: string
  /** Storno sababi (SPEC §4.3 — sabab majburiy). */
  reversalReason?: string
}

/** Chiqim holati — DTO'da yo'q, interfeys o'zi hisoblaydi (izohni pastda o'qing). */
export type ExpenseState = 'approved' | 'pending' | 'recorded' | 'reversed'

/**
 * Chiqim holati.
 *
 * `ExpenseDto` da `status` ustuni YO'Q (muzlatilgan shartnoma) — holat uch
 * dalildan kelib chiqadi va shu yerda, BITTA joyda hisoblanadi:
 *  - storno qilingan  → `reversed`;
 *  - tasdiqlovchi bor → `approved`;
 *  - chegaradan yuqori va tasdiqlovchi yo'q → `pending` (tasdiq navbati);
 *  - chegaradan past  → `recorded` (ikkinchi tasdiq talab qilinmaydi).
 */
export function expenseState(e: ExpenseRecord): ExpenseState {
  if (e.reversedBy) return 'reversed'
  if (e.approvedByName) return 'approved'
  return e.amount > EXPENSE_APPROVAL_THRESHOLD ? 'pending' : 'recorded'
}

/** Shu chiqim ikkinchi tasdiqni kutyaptimi? */
export function needsApproval(e: ExpenseRecord): boolean {
  return expenseState(e) === 'pending'
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
  note?: string
}

export async function getExpenses(filters: ExpenseFilters = {}): Promise<ExpenseRecord[]> {
  const { data } = await api.get<ExpenseRecord[]>(BASE, { params: filters })
  return data
}

/** Chiqim yozish. `created_by` JWT'dan (SPEC §4.4). */
export async function createExpense(input: ExpenseInput): Promise<ExpenseRecord> {
  const { data } = await api.post<ExpenseRecord>(BASE, input)
  return data
}

/** Tasdiqlash — faqat direktor va faqat BOSHQA shaxs (SPEC §4.5). */
export async function approveExpense(id: string): Promise<ExpenseRecord> {
  const { data } = await api.post<ExpenseRecord>(`${BASE}/${id}/approve`)
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
