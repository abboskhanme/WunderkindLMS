/**
 * TRANZAKSIYALAR JURNALI — bitta ro'yxatda hamma pul harakati
 * (docs/modules/finance-parity.md §2.9, F9.01–F9.05).
 *
 * SHARTNOMA (server: `SchoolLms.Application/Billing/TransactionJournalQuery.cs`,
 * `SchoolLms.Server/Controllers/TransactionJournalController.cs`):
 *
 *   GET /api/admin/finance/transactions          → TransactionPage
 *   GET /api/admin/finance/transactions/export   → .xlsx (o'sha filtr)
 *
 * RUXSAT (SPEC §4.3): faqat `admin` va `superadmin`. Kassir 403 oladi —
 * u o'z smenasini Z-hisobotdan ko'radi.
 *
 * STORNO SHU YERDA EMAS. Jurnaldagi "Storno qilish" tugmasi mavjud
 * endpoint'ga boradi — `reversePayment` (`./payments`). Ikkinchi yo'l
 * ochilmaydi: bitta amal — bitta manzil.
 *
 * PUL ARIFMETIKASI SERVERDA. `amount` allaqachon ishorali (kirim +,
 * chiqim −), yakun esa BUTUN FILTR bo'yicha keladi — sahifadagi qatorlardan
 * qayta hisoblanmaydi (JavaScript'da `number` — float64).
 */
import type { PaymentMethod } from '@/types'
import { api, USE_MOCK } from '../client'

const BASE = '/admin/finance/transactions'

/** Qator turi. */
export type TransactionKind = 'payment' | 'reversal' | 'expense'

/** Pul yo'nalishi. */
export type TransactionDirection = 'in' | 'out'

/** Qator holati. `pending` faqat chiqimda bo'ladi (SPEC §4.5). */
export type TransactionStatus = 'active' | 'reversed' | 'pending'

/** Jurnalning bitta qatori. */
export interface TransactionRow {
  /** Manba yozuv id'si: `payments.id` yoki `expenses.id`. */
  id: string
  kind: TransactionKind
  direction: TransactionDirection
  /** Biznes kuni, "YYYY-MM-DD". */
  occurredOn: string
  /** Aniq lahza (ISO) — kun ichidagi tartib uchun. */
  occurredAt: string
  /** Chek raqami — faqat to'lov va storno. */
  receiptNo: number | null
  /** Hujjat summasi, ISHORALI: kirim +, chiqim −. */
  amount: number
  /**
   * Pul harakati. Tasdiq kutayotgan va storno qilingan CHIQIMDA 0 —
   * ularda pul qo'ldan chiqmagan. Yakun aynan shundan yig'iladi.
   */
  settledAmount: number
  personId: string | null
  personName: string | null
  personKind: 'student' | 'teacher' | null
  className: string | null
  category: string | null
  categoryLabel: string | null
  /** Jurnal hisobi (`expense:rent`, …) — chiqimda to'ladi. */
  account: string | null
  /** To'lov usuli yoki chiqimning pul hisobi (`cash` / `bank`). */
  method: string | null
  actorId: string | null
  actorName: string | null
  note: string | null
  status: TransactionStatus
  /** Storno qatorida — bekor qilingan to'lov id'si. */
  reversalOf: string | null
  /** Storno qilingan to'lovda — storno qatori id'si. */
  reversedBy: string | null
}

/** Yakun — BUTUN FILTR bo'yicha, sahifa bo'yicha emas. */
export interface TransactionTotals {
  totalIn: number
  totalOut: number
  net: number
  /** Tasdiq kutayotgan chiqimlar — yakunga kirmaydi, alohida ko'rsatiladi. */
  pendingOut: number
}

export interface TransactionPage {
  rows: TransactionRow[]
  page: number
  pageSize: number
  total: number
  totals: TransactionTotals
}

export interface TransactionFilters {
  /** "YYYY-MM-DD" */
  from?: string
  to?: string
  direction?: TransactionDirection
  kind?: TransactionKind
  method?: PaymentMethod
  /** Kassir yoki chiqimni yozgan/tasdiqlagan foydalanuvchi. */
  actorId?: string
  studentId?: string
  className?: string
  category?: string
  status?: TransactionStatus
  receiptNo?: number
  /** F9.05 — faqat o'quvchining birinchi (storno qilinmagan) to'lovi. */
  firstPaymentOnly?: boolean
  page?: number
  pageSize?: number
  sort?: 'date' | 'amount'
  desc?: boolean
}

/** Bo'sh va `undefined` qiymatlar so'rovga qo'shilmasin. */
function clean(filters: TransactionFilters): Record<string, string | number | boolean> {
  const out: Record<string, string | number | boolean> = {}
  for (const [key, value] of Object.entries(filters)) {
    if (value === undefined || value === null || value === '') continue
    if (value === false) continue
    out[key] = value as string | number | boolean
  }
  return out
}

/** Jurnalning bitta sahifasi + butun filtr yakuni. */
export async function getTransactions(filters: TransactionFilters = {}): Promise<TransactionPage> {
  const { data } = await api.get<TransactionPage>(BASE, { params: clean(filters) })
  return data
}

/**
 * O'sha filtr bo'yicha .xlsx (F9.03). Summalar HAQIQIY son bo'lib tushadi
 * va oxirida yakun qatori turadi.
 */
export async function downloadTransactions(filters: TransactionFilters = {}): Promise<void> {
  if (USE_MOCK) {
    alert('Eksport faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }

  const res = await api.get(`${BASE}/export`, {
    params: clean({ ...filters, page: undefined, pageSize: undefined }),
    responseType: 'blob',
  })

  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^"]+)"?/)
  a.download = m?.[1] ?? `tranzaksiyalar_${new Date().toISOString().slice(0, 10)}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/* ---------- Yorliqlar (ekranda ko'rinadigan matn) ---------- */

export const transactionKindLabels: Record<TransactionKind, string> = {
  payment: "To'lov",
  reversal: 'Storno',
  expense: 'Chiqim',
}

export const transactionStatusLabels: Record<TransactionStatus, string> = {
  active: 'Faol',
  reversed: 'Storno qilingan',
  pending: 'Tasdiq kutmoqda',
}

export const transactionDirectionLabels: Record<TransactionDirection, string> = {
  in: 'Kirim',
  out: 'Chiqim',
}

/**
 * Shu qatorni storno qilish mumkinmi: faqat KUCHDAGI to'lov.
 *
 * Storno qatorining o'zi storno qilinmaydi (`cannot_reverse_reversal`),
 * chiqim esa o'z ekranidan (`/admin/billing/expenses`) bekor qilinadi —
 * u yerda tasdiqlash va storno bitta joyda turadi.
 */
export function canReverse(row: TransactionRow): boolean {
  return row.kind === 'payment' && row.status === 'active'
}
