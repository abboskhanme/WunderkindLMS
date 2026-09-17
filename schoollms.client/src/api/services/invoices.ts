/**
 * HISOB-FAKTURALAR REGISTRI — har bir hisoblangan oy
 * (docs/modules/finance-parity.md §2.10, F10.01–F10.03).
 *
 * SHARTNOMA (server: `SchoolLms.Server/Controllers/InvoicesController.cs`):
 *
 *   GET  /api/admin/billing/invoices             → InvoicePage
 *   POST /api/admin/billing/invoices/{id}/void   → Invoice   (sabab majburiy)
 *   POST /api/admin/billing/accrual/run?month=   → AccrualResult[]
 *
 * RUXSAT (SPEC §4.3): faqat `admin` va `superadmin`.
 *
 * `Invoice` TIPI BU YERDA QAYTA E'LON QILINMAYDI — u `@/types` da
 * (P1-06 shartnomasi) va server DTO'si bilan maydonma-maydon bir xil.
 * Ikkinchi nusxa yasalsa, ular bir kun ajralib ketardi va farqni faqat
 * mijoz sezardi.
 *
 * O'CHIRISH YO'Q — VA BO'LMAYDI. Xato hisoblangan oy `voidInvoice` bilan
 * bekor qilinadi: qator joyida qoladi, jurnal partiyasi esa ko'zgu satrlar
 * bilan qaytariladi (SPEC §4.1).
 */
import type { AccrualResult, Invoice, InvoiceStatus } from '@/types'
import { api } from '../client'

const BASE = '/admin/billing/invoices'

/** Yakun — BUTUN FILTR bo'yicha, sahifa bo'yicha emas. */
export interface InvoiceTotals {
  /** Σ to'liq summa (chegirmasiz). */
  amount: number
  discount: number
  /** Σ to'lanadigan = amount − discount. */
  payable: number
  /** Σ to'langan — faqat KUCHDAGI taqsimotlar (storno qilingani kirmaydi). */
  paid: number
  remaining: number
}

export interface InvoicePage {
  rows: Invoice[]
  page: number
  pageSize: number
  total: number
  totals: InvoiceTotals
  /**
   * `studentId → sinf`, faqat shu sahifadagi o'quvchilar uchun.
   *
   * Sinf qatorning ICHIDA emas, chunki `Invoice` — P1-06 da muzlatilgan
   * shartnoma; registrga esa sinf ustuni kerak (§2.10).
   */
  classNames: Record<string, string>
}

export interface InvoiceRegisterFilters {
  studentId?: string
  categoryId?: string
  /** "YYYY-MM-DD" (oyning 1-kuni). Sukut: joriy oy. */
  fromMonth?: string
  toMonth?: string
  status?: InvoiceStatus
  onlyOverdue?: boolean
  /** Qoldig'i borlargina. */
  onlyDebtors?: boolean
  className?: string
  page?: number
  pageSize?: number
}

function clean(filters: InvoiceRegisterFilters): Record<string, string | number | boolean> {
  const out: Record<string, string | number | boolean> = {}
  for (const [key, value] of Object.entries(filters)) {
    if (value === undefined || value === null || value === '') continue
    if (value === false) continue
    out[key] = value as string | number | boolean
  }
  return out
}

/** Registrning bitta sahifasi + butun filtr yakuni. */
export async function listInvoices(filters: InvoiceRegisterFilters = {}): Promise<InvoicePage> {
  const { data } = await api.get<InvoicePage>(BASE, { params: clean(filters) })
  return data
}

/**
 * Xato hisoblangan oyni BEKOR qiladi. Sabab majburiy — u jurnal yozuvida
 * va audit qatorida qoladi.
 *
 * Server rad etishi mumkin (hammasi o'zbekcha `message` bilan):
 *  · 409 `has_effective_allocation` — avval to'lovni storno qilish kerak;
 *  · 403 `self_reversal` — jurnalga o'zi qo'ygan odam o'zi bekor qila olmaydi;
 *  · 409 `already_void` — allaqachon bekor qilingan.
 */
export async function voidInvoice(id: string, reason: string): Promise<Invoice> {
  const { data } = await api.post<Invoice>(`${BASE}/${id}/void`, { reason })
  return data
}

/**
 * F10.03 — oyni qo'lda hisoblash. IDEMPOTENT: mavjud qator qayta
 * yaratilmaydi, natijada `created` / `skipped` qaytadi.
 *
 * @param month "YYYY-MM"
 */
export async function runAccrual(month: string): Promise<AccrualResult[]> {
  const { data } = await api.post<AccrualResult[]>('/admin/billing/accrual/run', null, {
    params: { month },
  })
  return data
}

/* ---------- Yorliqlar ---------- */

export const invoiceStatusLabels: Record<InvoiceStatus, string> = {
  open: 'Ochiq',
  partial: "Qisman to'langan",
  paid: "To'langan",
  void: 'Bekor qilingan',
}

/** Holat rangi — `StatusPill` ohangi. */
export function invoiceStatusTone(status: InvoiceStatus): 'neutral' | 'success' | 'warning' | 'danger' {
  if (status === 'paid') return 'success'
  if (status === 'partial') return 'warning'
  if (status === 'void') return 'neutral'
  return 'danger'
}

/**
 * Shu hisob-fakturani bekor qilish mumkinmi (interfeys darajasida).
 *
 * Bu HIMOYA EMAS — haqiqiy darvoza serverda. Bu yerda faqat ishlatib
 * bo'lmaydigan tugma ko'rsatilmaydi: to'langan oyni bekor qilishga urinib
 * ko'rgan odam "avval to'lovni storno qiling" degan 409 ni tugmani
 * bosgandan KEYIN emas, oldin bilgani yaxshi.
 */
export function canVoid(invoice: Invoice): boolean {
  return invoice.status !== 'void' && invoice.paid <= 0
}
