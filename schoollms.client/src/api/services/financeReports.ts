/**
 * Direktor moliya paneli uchun HISOBOT klienti (P1-18).
 *
 * Bu fayl `billing.ts` va `cashShifts.ts` dan FARQ QILADI: u stub emas,
 * barcha endpoint'lar HOZIR ishlaydi (P1-10 va P1-13 da yozilgan). Shuning
 * uchun bu yerda `notImplemented(...)` yo'q — har funksiya haqiqiy so'rov
 * yuboradi.
 *
 * RUXSAT (SPEC §4.3). `/admin/finance/*` — faqat `admin` va `superadmin`.
 * Kassir 403 oladi: "See variance report across cashiers — ⛔". Shuning
 * uchun UI bu tablarni kassirga UMUMAN ko'rsatmaydi — 403 ni ushlab
 * ko'rsatish emas, tugmani chizmaslik.
 *
 * PULNI FRONTEND HISOBLAMAYDI (BillingDtos.cs, 2-qoida). Bu yerdagi hamma
 * summa serverdan TAYYOR keladi. Yagona istisno — Cash Flow'dagi
 * "Jami (naqd + bank)" ustuni: u ikkita hisobning bir oyidagi qiymatini
 * qo'shadi, ya'ni yangi ma'no yaratmaydi, faqat serverning o'zi bergan
 * ikki qatorni ekranda birlashtiradi. Davr yakunlari (jami kirim/chiqim/
 * qoldiq) esa har doim serverning ildiz maydonlaridan olinadi.
 */
import axios from 'axios'
import type { BillingMonthly, CashShift, CashShiftStatus, DebtorRow, ZReport } from '@/types'
import { api } from '../client'

/* =========================================================================
   Tiplar — backend DTO'lariga AYNAN mos (SchoolLms.Application/Billing/
   FinanceReportQueries.cs va Dtos/BillingDtos.cs).
   ========================================================================= */

/** P&L ning bitta satri: hisob kodi va davr bo'yicha sof summasi. */
export interface ProfitLossLine {
  /** `revenue:tuition`, `expense:salary` … — yopiq ro'yxat + bazada uchragani. */
  account: string
  /** Daromad uchun kredit−debet, chiqim uchun debet−kredit (storno hisobga olingan). */
  amount: number
}

/** Foyda va zarar: `net` = `revenueTotal` − `expenseTotal`. */
export interface ProfitLoss {
  /** "YYYY-MM-DD" */
  from: string
  to: string
  revenue: ProfitLossLine[]
  revenueTotal: number
  expense: ProfitLossLine[]
  expenseTotal: number
  net: number
}

/** Pul harakatining bitta oyi (bitta hisob bo'yicha). */
export interface CashFlowMonth {
  /** Oyning birinchi kuni, "YYYY-MM-01" */
  month: string
  opening: number
  inflow: number
  outflow: number
  net: number
  closing: number
}

/** Bitta hisob (`cash` yoki `bank`) bo'yicha butun davr va oylar kesimi. */
export interface CashFlowAccount {
  account: string
  opening: number
  inflow: number
  outflow: number
  net: number
  closing: number
  months: CashFlowMonth[]
}

/** Pul oqimi: `cash` va `bank` harakati, oylar kesimida. */
export interface CashFlow {
  from: string
  to: string
  opening: number
  inflow: number
  outflow: number
  net: number
  closing: number
  accounts: CashFlowAccount[]
}

/* =========================================================================
   Xatolarni o'zbekcha xabarga aylantirish
   ========================================================================= */

/**
 * Axios xatosini foydalanuvchi o'qiy oladigan bitta jumlaga aylantiradi.
 *
 * Nega kerak: `useAsync` xatoning `message` maydonini ekranga chiqaradi,
 * axios esa u yerga "Request failed with status code 403" yozadi — direktor
 * uchun bu hech narsa demaydi. Server bergan `message` bo'lsa — o'sha,
 * bo'lmasa — holat kodiga qarab tayyor matn.
 */
function toUzbekError(error: unknown, what: string): Error {
  if (!axios.isAxiosError(error)) {
    return error instanceof Error ? error : new Error(`${what}: noma'lum xatolik.`)
  }

  const status = error.response?.status
  const body = error.response?.data as { message?: string } | undefined
  if (body?.message) return new Error(body.message)

  if (status === undefined) {
    return new Error("Serverga ulanib bo'lmadi. Internet aloqasini tekshiring.")
  }
  if (status === 403) {
    return new Error("Bu hisobotni ko'rishga ruxsatingiz yo'q — u faqat admin va direktor uchun.")
  }
  if (status === 404) {
    return new Error(`${what}: server bu manzilni topmadi.`)
  }
  if (status >= 500) {
    return new Error(`${what}: serverda xatolik (${status}). Keyinroq urinib ko'ring.`)
  }
  return new Error(`${what}: so'rov rad etildi (${status}).`)
}

/** `undefined` filtrlarni query'dan olib tashlaydi (aks holda `?a=undefined` ketadi). */
function clean(params: Record<string, string | number | boolean | undefined>) {
  return Object.fromEntries(Object.entries(params).filter(([, v]) => v !== undefined))
}

/* =========================================================================
   1) Qarzdorlar — GET /api/admin/finance/debtors
   ========================================================================= */

export interface DebtorFilters {
  /** Sinf (aniq moslik). Bo'sh = barcha sinflar. */
  className?: string
  /** Shu summadan kam qarz ko'rsatilmaydi (server sukuti 0.01). */
  minDebt?: number
  /** true = faqat muddati o'tganlar. */
  onlyOverdue?: boolean
  /** false = arxivlangan (maktabdan ketgan) o'quvchilarni yashirish. */
  includeArchived?: boolean
}

/**
 * Qarzdorlar: har o'quvchi bitta qator, toifalar kesimidagi yoyilma bilan.
 * Server jami qarz bo'yicha KAMAYISH tartibida qaytaradi.
 */
export async function getDebtors(filters: DebtorFilters = {}): Promise<DebtorRow[]> {
  try {
    const { data } = await api.get<DebtorRow[]>('/admin/finance/debtors', {
      params: clean({ ...filters }),
    })
    return data
  } catch (e) {
    throw toUzbekError(e, 'Qarzdorlar hisoboti')
  }
}

/* =========================================================================
   2) P&L — GET /api/admin/finance/pnl
   ========================================================================= */

/** Foyda va zarar. Sana formati "YYYY-MM-DD". */
export async function getProfitLoss(from: string, to: string): Promise<ProfitLoss> {
  try {
    const { data } = await api.get<ProfitLoss>('/admin/finance/pnl', { params: { from, to } })
    return data
  } catch (e) {
    throw toUzbekError(e, 'Foyda va zarar hisoboti')
  }
}

/* =========================================================================
   3) Pul oqimi — GET /api/admin/finance/cashflow
   ========================================================================= */

/** Pul oqimi: `cash` va `bank`, oylar kesimida. Davr 120 oydan uzun bo'lsa server 400 beradi. */
export async function getCashFlow(from: string, to: string): Promise<CashFlow> {
  try {
    const { data } = await api.get<CashFlow>('/admin/finance/cashflow', { params: { from, to } })
    return data
  } catch (e) {
    throw toUzbekError(e, 'Pul oqimi hisoboti')
  }
}

/* =========================================================================
   4) Yig'ilish darajasi — GET /api/admin/finance/collection-rate
   ========================================================================= */

/**
 * Oylar kesimida hisoblangan va yig'ilgan summa. Oy — HISOB-FAKTURA oyi
 * (`period_month`), pul kelgan kun emas.
 */
export async function getCollectionRate(from?: string, to?: string): Promise<BillingMonthly[]> {
  try {
    const { data } = await api.get<BillingMonthly[]>('/admin/finance/collection-rate', {
      params: clean({ from, to }),
    })
    return data
  } catch (e) {
    throw toUzbekError(e, "Yig'ilish darajasi hisoboti")
  }
}

/* =========================================================================
   5) Kassa smenalari va Z-hisobot — GET /api/cash/shifts[/{id}/z-report]
   ========================================================================= */

export interface CashShiftFilters {
  cashierId?: string
  /** "YYYY-MM-DD" */
  from?: string
  to?: string
  status?: CashShiftStatus
  /** true = faqat nomuvofiqligi nolga teng bo'lmagan smenalar (direktor paneli). */
  onlyWithVariance?: boolean
}

/**
 * Smenalar ro'yxati. Admin va direktor — hamma kassirlar kesimida;
 * kassir esa faqat o'zinikini (serverda filtr jimgina toraytiriladi).
 */
export async function getCashShifts(filters: CashShiftFilters = {}): Promise<CashShift[]> {
  try {
    const { data } = await api.get<CashShift[]>('/cash/shifts', { params: clean({ ...filters }) })
    return data
  } catch (e) {
    throw toUzbekError(e, 'Kassa smenalari')
  }
}

/** Smena yakuni: usullar va toifalar kesimi, chek oralig'i, kutilgan/sanalgan/farq. */
export async function getZReport(shiftId: string): Promise<ZReport> {
  try {
    const { data } = await api.get<ZReport>(`/cash/shifts/${shiftId}/z-report`)
    return data
  } catch (e) {
    throw toUzbekError(e, 'Z-hisobot')
  }
}

/* =========================================================================
   6) Anomaliya bayroqlari — P1-14 (server: FinanceFlagsController)
   ========================================================================= */

/**
 * Nightly job topgan anomaliya (SPEC §4.6, docs/TASKS.md P1-14).
 *
 * Qator O'CHIRILMAYDI: u faqat `resolvedReason` yozilib yopiladi. Shuning
 * uchun UI'da "x" tugmasi yo'q va bo'lmaydi.
 */
export interface FinanceFlag {
  id: string
  /** shift_variance | fast_reversal | off_hours_payment | paid_without_allocation | broken_promise (hisoblanadi, saqlanmaydi) */
  kind: string
  /** O'zbekcha nom — SERVERDAN keladi, UI o'z lug'atini saqlamaydi. */
  kindLabel: string
  /** cash_shift | payment | invoice | debtor_action */
  refType: string
  /** Tegishli yozuv (smena / to'lov / hisob-faktura) id'si. */
  refId: string
  /** Hodisaning o'zi sodir bo'lgan vaqt. */
  occurredAt: string
  /** Skaner uni topgan vaqt. */
  detectedAt: string
  amount?: number | null
  /** Bir qatorlik izoh — ro'yxatda shu ko'rinadi. */
  summary: string
  details?: string | null
  resolvedAt?: string | null
  resolvedBy?: string | null
  resolvedByName?: string | null
  resolvedReason?: string | null
}

/** Turlar kesimidagi hisoblagich — panelning yuqori qatori. */
export interface FinanceFlagKindCount {
  kind: string
  kindLabel: string
  unresolved: number
  total: number
}

/**
 * Bayroqlar so'rovining natijasi.
 *
 * Nega oddiy massiv emas: `GET /api/admin/finance/flags` ni P1-14 yozadi va
 * u HALI YO'Q (bugun 404 qaytaradi). "Bo'sh massiv" qaytarish eng yomon
 * variant bo'lardi — direktor "anomaliya yo'q" deb o'qiydi, aslida esa
 * skaner umuman ishlamayapti. Shuning uchun natija ikki holatli: bayroqlar
 * BOR, yoki jurnal hali ULANMAGAN.
 */
export type FinanceFlagsResult =
  | {
      available: true
      /** HISOBLAGICH SHU YERDAN olinadi, `flags.length` dan EMAS: ro'yxat
       *  sahifalanishi yoki filtrlanishi mumkin, bu raqam esa serverning
       *  o'zi sanagan to'liq soni. */
      unresolved: number
      total: number
      unresolvedAmount: number
      byKind: FinanceFlagKindCount[]
      flags: FinanceFlag[]
    }
  | { available: false; reason: string }

/** `GET /admin/finance/flags` javobi (server: `AnomalyFlagsDto`). */
interface FinanceFlagsResponse {
  unresolved: number
  total: number
  unresolvedAmount: number
  byKind: FinanceFlagKindCount[]
  items: FinanceFlag[]
}

/**
 * Hal qilinmagan anomaliyalar. P1-14 endpoint'i hali yo'q bo'lsa (404) —
 * `available: false`, ya'ni ekran buni ochiq aytadi.
 */
export async function getFinanceFlags(unresolved = true): Promise<FinanceFlagsResult> {
  try {
    const { data } = await api.get<FinanceFlagsResponse>('/admin/finance/flags', {
      params: { unresolved },
    })
    return {
      available: true,
      unresolved: data.unresolved,
      total: data.total,
      unresolvedAmount: data.unresolvedAmount,
      byKind: data.byKind ?? [],
      flags: data.items ?? [],
    }
  } catch (e) {
    if (axios.isAxiosError(e) && (e.response?.status === 404 || e.response?.status === 501)) {
      return {
        available: false,
        reason:
          "Anomaliya jurnali hali ulanmagan — nomuvofiqliklar to'g'ridan-to'g'ri yopilgan smenalardan olinmoqda.",
      }
    }
    throw toUzbekError(e, 'Anomaliya jurnali')
  }
}

/**
 * Anomaliyani SABAB yozib yopish. Bekor qilish yoki o'chirish yo'q —
 * SPEC §4.6: "cannot be dismissed, only resolved with a written reason".
 */
export async function resolveFinanceFlag(id: string, reason: string): Promise<FinanceFlag> {
  try {
    const { data } = await api.post<FinanceFlag>(`/admin/finance/flags/${id}/resolve`, { reason })
    return data
  } catch (e) {
    throw toUzbekError(e, 'Nomuvofiqlikni hal qilish')
  }
}

/* =========================================================================
   7) Oyma-oy qarzdorlik — GET /api/admin/finance/arrears-pivot
   ========================================================================= */

/**
 * Jadvalning bitta katagi: bitta o'quvchining bitta oyi.
 *
 * `toBePaid` SERVERDAN keladi (`max(0, amount − paid)`), bu yerda qayta
 * hisoblanmaydi — fayl boshidagi qoida: pulni frontend hisoblamaydi.
 */
export interface ArrearsCell {
  /** Shu oyga hisoblangan, chegirmadan keyin. */
  amount: number
  /** Shu oyga HAQIQATAN tushgan pul (storno chiqarib tashlangan). */
  paid: number
  /** Qolgan qarz. */
  toBePaid: number
}

/** Jadvalning bitta qatori — bitta o'quvchi. */
export interface ArrearsRow {
  studentId: string
  fullName: string
  className: string
  /** Arxivdagi (maktabdan ketgan) o'quvchi — qarzi qoladi. */
  isArchived: boolean
  /**
   * Kalit — "YYYY-MM". Oyda hisob-faktura bo'lmasa kalit UMUMAN yo'q:
   * "maktabda bo'lmagan oy" va "to'lab bo'lingan oy" ekranda ham bir xil
   * ko'rinmasligi kerak.
   */
  cells: Record<string, ArrearsCell>
  /** Qator yakuni — kataklar yig'indisi (server hisoblaydi). */
  total: ArrearsCell
}

/** Oyma-oy qarzdorlik jadvali. */
export interface ArrearsPivot {
  /** Ustunlar tartibi — "YYYY-MM", bo'sh oy ham ro'yxatda qoladi. */
  months: string[]
  rows: ArrearsRow[]
  /** Ustun yakunlari, kalit "YYYY-MM". */
  footer: Record<string, ArrearsCell>
  /** Butun jadval yakuni. */
  total: ArrearsCell
}

export interface ArrearsFilters {
  /** Birinchi oy, "YYYY-MM". Sukut: joriy o'quv yilining sentyabri. */
  fromMonth?: string
  /** Oxirgi oy, "YYYY-MM". Sukut: joriy oy. */
  toMonth?: string
  /** Sinf (aniq moslik). */
  className?: string
  /** Bitta to'lov toifasi. Berilmasa — hammasi bitta katakka yig'iladi. */
  categoryId?: string
  /** true = qoldig'i bor o'quvchilargina. */
  debtorsOnly?: boolean
  /** false = arxivlangan o'quvchilarni yashirish. */
  includeArchived?: boolean
}

/**
 * Oyma-oy qarzdorlik: o'quvchi × oy.
 *
 * Server ikki chegara qo'yadi va ikkovi ham 400 bilan qaytadi: davr 12 oydan
 * uzun bo'lsa, va filtrdan keyin 600 dan ko'p o'quvchi qolsa (sinf filtri
 * talab qilinadi). Ikkovining ham xabari o'zbekcha va to'g'ridan-to'g'ri
 * ekranga chiqariladi.
 */
export async function getArrearsPivot(filters: ArrearsFilters = {}): Promise<ArrearsPivot> {
  try {
    const { data } = await api.get<ArrearsPivot>('/admin/finance/arrears-pivot', {
      params: clean({ ...filters }),
    })
    return data
  } catch (e) {
    throw toUzbekError(e, 'Oyma-oy qarzdorlik hisoboti')
  }
}
