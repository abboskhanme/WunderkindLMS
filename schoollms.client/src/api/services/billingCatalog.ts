/**
 * Moliya ma'lumotnomasi — TIRIK klient (P1-17).
 *
 * `BillingCatalogController` ning 13 ta endpoint'i allaqachon ishlaydi
 * (`/api/admin/billing/...`). Bu fayl ularga to'g'ridan-to'g'ri boradi.
 *
 * NEGA `billing.ts` EMAS
 * ----------------------
 * `api/services/billing.ts` — P1-06 da MUZLATILGAN shartnoma va uning har
 * bir funksiyasi hozircha `notImplemented(...)` tashlaydi. Uni ochish 1.C
 * egasining ishi (fayl boshidagi izoh shuni aytadi) va u fayl bilan parallel
 * agentlar ishlayapti. Shuning uchun bu yerda ALOHIDA, faqat toifa/obuna/
 * chegirma uchun klient turadi — imzolar `billing.ts` dagilar bilan ataylab
 * bir xil shaklda, ya'ni 1.C `billing.ts` ni ochgach sahifalarni bitta
 * import almashtirish bilan ko'chirish mumkin.
 *
 * IKKI QAVATLI NAZORAT (SPEC §4.5)
 * --------------------------------
 * `createdById` — DTO'da HOZIRCHA YO'Q (`DiscountDto` faqat `CreatedByName`
 * beradi) va shuning uchun `?` bilan. Backend uni qo'shgan kunda (ixtiyoriy
 * maydon — `BillingDtos.cs` boshidagi izohga ko'ra buzmaydigan o'zgarish)
 * interfeys avtomatik ravishda ism solishtirishdan id solishtirishga o'tadi;
 * qarang `pages/admin/billing/dualControl.ts`.
 */
import type { Discount, FeeCategory, Invoice, StudentSubscription } from '@/types'
import { api } from '../client'

const BASE = '/admin/billing'

/**
 * Server javobi + kelajakda qo'shiladigan `createdById`.
 * Hozir `undefined` — UI ism bo'yicha zaxira solishtiruvga tushadi.
 */
export type DiscountRecord = Discount & { createdById?: string }
export type SubscriptionRecord = StudentSubscription & { createdById?: string }

/* ---------- To'lov toifalari ---------- */

export interface FeeCategoryInput {
  /** Yaratilgandan keyin O'ZGARMAYDI — unga daromad hisobi bog'langan. */
  code: string
  name: string
  isActive: boolean
}

export async function getFeeCategories(activeOnly = false): Promise<FeeCategory[]> {
  const { data } = await api.get<FeeCategory[]>(`${BASE}/categories`, { params: { activeOnly } })
  return data
}

export async function createFeeCategory(input: FeeCategoryInput): Promise<FeeCategory> {
  const { data } = await api.post<FeeCategory>(`${BASE}/categories`, input)
  return data
}

export async function updateFeeCategory(
  id: string,
  input: FeeCategoryInput,
): Promise<FeeCategory> {
  const { data } = await api.put<FeeCategory>(`${BASE}/categories/${id}`, input)
  return data
}

/* ---------- Obunalar ---------- */

export interface SubscriptionFilters {
  studentId?: string
  categoryId?: string
  /** true = bugungi kunda amal qiladiganlar (starts_on ≤ bugun ≤ ends_on). */
  activeOnly?: boolean
}

export interface SubscriptionInput {
  studentId: string
  categoryId: string
  monthlyAmount: number
  /** Avtobus yo'nalishi, yotoqxona xonasi va h.k. */
  detail?: string
  /** "YYYY-MM-DD" */
  startsOn: string
  endsOn?: string
}

export interface SubscriptionUpdate {
  monthlyAmount: number
  detail?: string
  endsOn?: string
}

/** Formaga oldindan qo'yiladigan summa va u qayerdan kelgani. */
export interface SubscriptionDefault {
  studentId: string
  categoryId: string
  categoryCode: string
  monthlyAmount: number
  /** `class_fee` — sinf oylik to'lovidan; `none` — taklif yo'q. */
  source: 'class_fee' | 'none' | string
}

export async function getSubscriptions(
  filters: SubscriptionFilters = {},
): Promise<SubscriptionRecord[]> {
  const { data } = await api.get<SubscriptionRecord[]>(`${BASE}/subscriptions`, { params: filters })
  return data
}

export async function getSubscriptionDefault(
  studentId: string,
  categoryId: string,
): Promise<SubscriptionDefault> {
  const { data } = await api.get<SubscriptionDefault>(`${BASE}/subscriptions/default`, {
    params: { studentId, categoryId },
  })
  return data
}

export async function createSubscription(input: SubscriptionInput): Promise<SubscriptionRecord> {
  const { data } = await api.post<SubscriptionRecord>(`${BASE}/subscriptions`, input)
  return data
}

export async function updateSubscription(
  id: string,
  input: SubscriptionUpdate,
): Promise<SubscriptionRecord> {
  const { data } = await api.put<SubscriptionRecord>(`${BASE}/subscriptions/${id}`, input)
  return data
}

/** Obunani yopadi — ko'rsatilgan sanadan keyin hisoblanmaydi. */
export async function endSubscription(id: string, endsOn: string): Promise<SubscriptionRecord> {
  const { data } = await api.post<SubscriptionRecord>(`${BASE}/subscriptions/${id}/end`, { endsOn })
  return data
}

/**
 * F1.06 (docs/modules/finance-parity.md §2.1) — obunani yopishdan OLDIN:
 * `endsOn` oyidan KEYINGI oylarga allaqachon hisoblangan (hali bekor
 * qilinmagan) hisob-fakturalar. Tugash oyining o'zi bu ro'yxatda YO'Q — u
 * proratsiya qilinmay, to'liq qarz bo'lib qoladi (bizning qoidamiz).
 *
 * Hech narsa yozmaydi — sof o'qish. Tanlangan qatorlarni bekor qilish uchun
 * `voidInvoice` (`@/api/services/invoices`) har biriga ALOHIDA chaqiriladi;
 * "bekor qilish mumkinmi" degan javob esa yangi maydon emas, mavjud
 * `canVoid(invoice)` orqali (o'sha faylda).
 */
export async function previewEndSubscription(
  id: string,
  endsOn: string,
): Promise<EndSubscriptionPreview> {
  const { data } = await api.post<EndSubscriptionPreview>(
    `${BASE}/subscriptions/${id}/end/preview`,
    { endsOn },
  )
  return data
}

export interface EndSubscriptionPreview {
  subscriptionId: string
  studentName: string
  categoryName: string
  endsOn: string
  futureInvoices: Invoice[]
}

/* ---------- Chegirmalar (SPEC §8.1 Q5 — chegara YO'Q) ---------- */

export interface DiscountFilters {
  studentId?: string
  categoryId?: string
  /** pending | approved | rejected. Bo'sh = hammasi. */
  status?: string
}

export interface DiscountInput {
  studentId: string
  /** Bo'sh = barcha toifalarga. */
  categoryId?: string
  /** Foiz 0..100 — avval shu ayriladi. */
  percent: number
  /** Aniq summa (so'm) — foizdan keyin ayriladi. */
  amount: number
  reason: string
  startsOn: string
  endsOn?: string
}

export async function getDiscounts(filters: DiscountFilters = {}): Promise<DiscountRecord[]> {
  const { data } = await api.get<DiscountRecord[]>(`${BASE}/discounts`, { params: filters })
  return data
}

/** Tasdiq navbati — eng eski so'rov birinchi. */
export async function getPendingDiscounts(): Promise<DiscountRecord[]> {
  const { data } = await api.get<DiscountRecord[]>(`${BASE}/discounts/pending`)
  return data
}

/**
 * Chegirma SO'RAYDI. HAR DOIM `pending` holatda yaratiladi — mijoz javobi
 * (SPEC §8.1 Q5). "Darhol qo'llash" varianti yo'q va bo'lmaydi.
 */
export async function createDiscount(input: DiscountInput): Promise<DiscountRecord> {
  const { data } = await api.post<DiscountRecord>(`${BASE}/discounts`, input)
  return data
}

/** Tasdiqlash — faqat direktor va faqat BOSHQA shaxs (server 403 beradi). */
export async function approveDiscount(id: string): Promise<DiscountRecord> {
  const { data } = await api.post<DiscountRecord>(`${BASE}/discounts/${id}/approve`)
  return data
}

/** Rad etish — sabab majburiy. Qator tarix uchun qoladi. */
export async function rejectDiscount(id: string, reason: string): Promise<DiscountRecord> {
  const { data } = await api.post<DiscountRecord>(`${BASE}/discounts/${id}/reject`, { reason })
  return data
}
