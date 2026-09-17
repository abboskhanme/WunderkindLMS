/**
 * Moliya katalogi: toifalar, obunalar, chegirmalar, hisob-fakturalar,
 * chiqimlar, hisobotlar, sozlamalar.
 *
 * MUZLATILGAN SHARTNOMA (P1-06) — hozircha STUB.
 * -----------------------------------------------
 * Har funksiya imzosi va URL'i shu yerda qat'iy belgilangan; tanasi esa
 * `notImplemented(...)` tashlaydi. Sabab: Faza 1.C (backend) va Faza 1.F
 * (frontend) PARALLEL yoziladi — sahifalar shu imzolarga tayanib qurila
 * boshlaydi, backend esa keyinroq keladi.
 *
 * 1.C EGASI NIMA QILADI: `notImplemented(...)` qatorini o'chirib, yonidagi
 * izohda tayyor turgan bitta `api.*` chaqiruvini ochadi. Imzo O'ZGARMAYDI.
 *
 * Nega bo'sh massiv yoki mock qaytarilmaydi: "ishlayotgandek ko'rinadigan"
 * ekran eng qimmat xato. Tashlangan xato ekranda darrov ko'rinadi va qaysi
 * vazifa hali tayyor emasligini AYTIB turadi.
 */
import type {
  AccountBalance,
  AccrualResult,
  BillingMonthly,
  BillingSettings,
  DebtorRow,
  Discount,
  Expense,
  FeeCategory,
  Invoice,
  InvoiceStatus,
  StudentBilling,
  StudentSubscription,
} from '@/types'
// Tayyor bo'lgach ochiladi: import { api } from '../client'
import { notImplemented } from './notImplemented'

/* ---------- Toifalar (P1-08) ---------- */

export async function getFeeCategories(): Promise<FeeCategory[]> {
  // return (await api.get<FeeCategory[]>('/admin/billing/categories')).data
  return notImplemented('GET /admin/billing/categories', 'P1-08')
}

export interface FeeCategoryPayload {
  code: string
  name: string
  isActive: boolean
}

export async function createFeeCategory(payload: FeeCategoryPayload): Promise<FeeCategory> {
  // return (await api.post<FeeCategory>('/admin/billing/categories', payload)).data
  return notImplemented('POST /admin/billing/categories', 'P1-08', payload)
}

export async function updateFeeCategory(
  id: string,
  payload: FeeCategoryPayload,
): Promise<FeeCategory> {
  // return (await api.put<FeeCategory>(`/admin/billing/categories/${id}`, payload)).data
  return notImplemented(`PUT /admin/billing/categories/${id}`, 'P1-08', payload)
}

/* ---------- Obunalar (P1-08) ---------- */

export interface SubscriptionPayload {
  studentId: string
  categoryId: string
  monthlyAmount: number
  detail?: string
  /** "YYYY-MM-DD" */
  startsOn: string
  endsOn?: string
}

export async function getSubscriptions(studentId?: string): Promise<StudentSubscription[]> {
  // return (await api.get<StudentSubscription[]>('/admin/billing/subscriptions', { params: { studentId } })).data
  return notImplemented('GET /admin/billing/subscriptions', 'P1-08', { studentId })
}

export async function createSubscription(
  payload: SubscriptionPayload,
): Promise<StudentSubscription> {
  // return (await api.post<StudentSubscription>('/admin/billing/subscriptions', payload)).data
  return notImplemented('POST /admin/billing/subscriptions', 'P1-08', payload)
}

export async function updateSubscription(
  id: string,
  payload: { monthlyAmount: number; detail?: string; endsOn?: string },
): Promise<StudentSubscription> {
  // return (await api.put<StudentSubscription>(`/admin/billing/subscriptions/${id}`, payload)).data
  return notImplemented(`PUT /admin/billing/subscriptions/${id}`, 'P1-08', payload)
}

/** Obunani yopish (o'quvchi avtobusdan chiqdi va h.k.) */
export async function endSubscription(id: string, endsOn: string): Promise<StudentSubscription> {
  // return (await api.post<StudentSubscription>(`/admin/billing/subscriptions/${id}/end`, { endsOn })).data
  return notImplemented(`POST /admin/billing/subscriptions/${id}/end`, 'P1-08', { endsOn })
}

/* ---------- Chegirmalar (P1-08) ---------- */

export interface DiscountPayload {
  studentId: string
  /** null/undefined = barcha toifalarga */
  categoryId?: string
  percent: number
  amount: number
  reason: string
  startsOn: string
  endsOn?: string
}

export async function getDiscounts(params: {
  studentId?: string
  status?: string
} = {}): Promise<Discount[]> {
  // return (await api.get<Discount[]>('/admin/billing/discounts', { params })).data
  return notImplemented('GET /admin/billing/discounts', 'P1-08', params)
}

/**
 * Chegirma SO'RASH. Har doim `pending` holatda yaratiladi — mijoz javobi
 * (SPEC §8.1 Q5): chegara yo'q, har qanday chegirma direktor tasdig'ini
 * talab qiladi. "Darhol qo'llash" varianti YO'Q.
 */
export async function createDiscount(payload: DiscountPayload): Promise<Discount> {
  // return (await api.post<Discount>('/admin/billing/discounts', payload)).data
  return notImplemented('POST /admin/billing/discounts', 'P1-08', payload)
}

/** Tasdiqlash — FAQAT direktor (superadmin). O'zi yaratganini tasdiqlay olmaydi. */
export async function approveDiscount(id: string): Promise<Discount> {
  // return (await api.post<Discount>(`/admin/billing/discounts/${id}/approve`)).data
  return notImplemented(`POST /admin/billing/discounts/${id}/approve`, 'P1-08')
}

export async function rejectDiscount(id: string, reason: string): Promise<Discount> {
  // return (await api.post<Discount>(`/admin/billing/discounts/${id}/reject`, { reason })).data
  return notImplemented(`POST /admin/billing/discounts/${id}/reject`, 'P1-08', { reason })
}

/* ---------- Hisob-fakturalar (P1-09) ---------- */

export interface InvoiceFilters {
  studentId?: string
  categoryId?: string
  /** "YYYY-MM-DD" (oyning 1-kuni) */
  fromMonth?: string
  toMonth?: string
  status?: InvoiceStatus
  onlyOverdue?: boolean
  className?: string
}

/**
 * ULANDI (F10.01) — lekin `api.get` bu yerda EMAS.
 *
 * `GET /admin/billing/invoices` endi SAHIFALANGAN javob qaytaradi
 * (`{ rows, page, pageSize, total, totals }`, qarang
 * `SchoolLms.Server/Controllers/InvoicesController.cs`), bu funksiyaning
 * muzlatilgan imzosi esa `Invoice[]`. Ikkovini yarashtirishning yagona
 * to'g'ri yo'li — bitta manzilga bitta klient: shartnoma
 * `api/services/invoices.ts` da, bu yerda faqat eski imzoga moslash qoladi.
 *
 * Yangi ekranlar TO'G'RIDAN-TO'G'RI `listInvoices` ni chaqirsin: u sahifani
 * ham, yakunni ham beradi.
 */
export async function getInvoices(filters: InvoiceFilters = {}): Promise<Invoice[]> {
  const { listInvoices } = await import('./invoices')
  return (await listInvoices({ ...filters, pageSize: 200 })).rows
}

/** O'quvchining moliyaviy kartochkasi: qarz, obunalar, oylar, to'lovlar */
export async function getStudentBilling(studentId: string): Promise<StudentBilling> {
  // return (await api.get<StudentBilling>(`/admin/billing/students/${studentId}`)).data
  return notImplemented(`GET /admin/billing/students/${studentId}`, 'P1-09')
}

/** Oylik hisoblashni qo'lda ishga tushirish. month berilmasa — hisoblanmagan hamma oy. */
export async function accrue(month?: string): Promise<AccrualResult[]> {
  // return (await api.post<AccrualResult[]>('/admin/billing/accrue', null, { params: { month } })).data
  return notImplemented('POST /admin/billing/accrue', 'P1-09', { month })
}

/* ---------- Chiqimlar (P1-13) ---------- */

export interface ExpensePayload {
  /** "YYYY-MM-DD" */
  onDate: string
  category: string
  amount: number
  note?: string
}

export async function getExpenses(params: { from?: string; to?: string } = {}): Promise<Expense[]> {
  // return (await api.get<Expense[]>('/admin/billing/expenses', { params })).data
  return notImplemented('GET /admin/billing/expenses', 'P1-13', params)
}

export async function createExpense(payload: ExpensePayload): Promise<Expense> {
  // return (await api.post<Expense>('/admin/billing/expenses', payload)).data
  return notImplemented('POST /admin/billing/expenses', 'P1-13', payload)
}

/* ---------- Hisobotlar (P1-13) ---------- */

export async function getDebtors(className?: string): Promise<DebtorRow[]> {
  // return (await api.get<DebtorRow[]>('/admin/billing/reports/debtors', { params: { className } })).data
  return notImplemented('GET /admin/billing/reports/debtors', 'P1-13', { className })
}

export async function getMonthlyDynamics(year: number): Promise<BillingMonthly[]> {
  // return (await api.get<BillingMonthly[]>('/admin/billing/reports/monthly', { params: { year } })).data
  return notImplemented('GET /admin/billing/reports/monthly', 'P1-13', { year })
}

/** Hisoblar kesimi — P&L va Cash Flow shundan quriladi */
export async function getTrialBalance(from?: string, to?: string): Promise<AccountBalance[]> {
  // return (await api.get<AccountBalance[]>('/admin/billing/reports/trial-balance', { params: { from, to } })).data
  return notImplemented('GET /admin/billing/reports/trial-balance', 'P1-13', { from, to })
}

/* ---------- Sozlamalar (SPEC §8.1 Q6) ---------- */

export async function getBillingSettings(): Promise<BillingSettings> {
  // return (await api.get<BillingSettings>('/admin/billing/settings')).data
  return notImplemented('GET /admin/billing/settings', 'P1-09')
}

export async function updateBillingSettings(payload: {
  paymentDueDay: number
  overdueAfterDay: number
}): Promise<BillingSettings> {
  // return (await api.put<BillingSettings>('/admin/billing/settings', payload)).data
  return notImplemented('PUT /admin/billing/settings', 'P1-09', payload)
}
