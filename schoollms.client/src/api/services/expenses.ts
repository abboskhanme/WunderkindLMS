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
import type { Expense, PaymentMethod } from '@/types'
import { api } from '../client'

const BASE = '/admin/expenses'

/**
 * Ikkinchi tasdiq chegarasi BU YERDA YO'Q — va ataylab.
 *
 * Ilgari shu faylda `5_000_000` qattiq yozilgan edi va holat o'shanga qarab
 * hisoblanardi. Chegara esa bazada (`BillingSettings.ExpenseApprovalThreshold`)
 * va uni maktab o'zgartirishi mumkin: o'zgartirgan kuni ekran jimgina yolg'on
 * ko'rsata boshlardi. Endi holat SERVERDAN keladi (`Expense.status`), interfeys
 * esa raqamni takrorlamaydi.

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
 * Chiqim holati — SERVERNING `status` maydonidan.
 *
 * Server uchta holat biladi (`ExpenseStatus`): `pending` · `posted` ·
 * `reversed`. Ekranda to'rttasi ko'rinadi, chunki jurnalga tushgan chiqim
 * tasdiq bilan tushganmi yoki tasdiqsizmi — buni foydalanuvchi ajratishi
 * kerak. Shuning uchun `posted` ikkiga bo'linadi: tasdiqlovchisi bor bo'lsa
 * `approved`, bo'lmasa `recorded`.
 *
 * `status` bo'lmagan yagona holat — mock ma'lumot; o'shanda eski mantiq
 * ishlaydi, lekin chegarasiz: tasdiqlovchi bor/yo'qligiga qarab.
 */
export function expenseState(e: ExpenseRecord): ExpenseState {
  if (e.status === 'reversed' || e.reversedBy) return 'reversed'
  if (e.status === 'pending') return 'pending'
  return e.approvedByName ? 'approved' : 'recorded'
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
  /**
   * Pul qaysi usulda chiqdi — server buni TALAB QILADI (`RequireMethod`).
   * Shu maydon yuborilmagani uchun "Yangi chiqim" hech qachon saqlanmasdi.
   */
  method: PaymentMethod
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

/**
 * Tasdiqlash — faqat direktor va faqat BOSHQA shaxs (SPEC §4.5).
 *
 * `method` MAJBURIY: tasdiq lahzasida pul jurnalga tushadi va jurnalning
 * kredit satri qaysi hisobdan chiqishini shu belgilaydi. Ilgari bu funksiya
 * so'rov tanasini umuman yubormasdi, shuning uchun tasdiqlash har safar
 * xato bilan qaytardi.
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
