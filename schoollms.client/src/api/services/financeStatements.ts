/**
 * Moliya hisobotlarining IKKINCHI avlodi (docs/modules/finance-parity.md
 * §2.4, §2.5, §2.7) — "Moliya hisobotlari" paneli, yil × oy P&L, toifalar
 * kesimidagi pul oqimi va har bir katakning ortidagi jurnal satrlari.
 *
 * Server: `SchoolLms.Server/Controllers/FinanceStatementsController.cs`
 *   · GET /admin/finance/dashboard?from&to
 *   · GET /admin/finance/pnl/matrix?year
 *   · GET /admin/finance/ledger/lines?account&from&to
 *   · GET /admin/finance/cashflow/statement?from&to&account
 *   · GET /admin/finance/cashflow/lines?key&method&from&to&account
 *
 * PULNI FRONTEND HISOBLAMAYDI (`financeReports.ts` sarlavhasidagi qoida).
 * Bu yerdagi hamma summa — qator yakuni, ustun yakuni, foiz o'zgarishi,
 * qoldiq — serverdan TAYYOR keladi. Ekran faqat chizadi va bosilgan
 * katakning kalitini qaytarib yuboradi.
 *
 * RUXSAT (SPEC §4.3): `/admin/finance/*` — faqat `admin` va `superadmin`;
 * kassir 403 oladi. Shuning uchun UI bu ekranni kassirga UMUMAN
 * ko'rsatmaydi.
 */
import axios from 'axios'
import { api } from '../client'

/* =========================================================================
   Tiplar — backend DTO'lariga AYNAN mos
   (SchoolLms.Application/Billing/FinanceReportQueries.*.cs).
   ========================================================================= */

/** P&L matritsasining bitta qatori: hisob va uning 12 oyi. */
export interface ProfitLossMatrixRow {
  account: string
  /** Yanvardan dekabrgacha — har doim 12 ta. */
  months: number[]
  /** Qator yakuni = kataklar yig'indisi (server qo'shadi). */
  total: number
}

/** Yil × oy P&L. Daromad ham, chiqim ham MUSBAT ko'rsatiladi. */
export interface ProfitLossMatrix {
  year: number
  /** Ustunlar: "YYYY-MM". */
  months: string[]
  revenue: ProfitLossMatrixRow[]
  revenueMonths: number[]
  revenueTotal: number
  expense: ProfitLossMatrixRow[]
  expenseMonths: number[]
  expenseTotal: number
  netMonths: number[]
  netTotal: number
  /** Oy boshidagi pul qoldig'i (kassa + bank). */
  startBalance: number[]
  endBalance: number[]
  openingBalance: number
  closingBalance: number
}

/** Katakcha ortidagi bitta jurnal satri. */
export interface LedgerLine {
  entryId: number
  /** "YYYY-MM-DD" */
  entryDate: string
  account: string
  /** `debit` | `credit` */
  direction: 'debit' | 'credit'
  /**
   * Summa. Pul oqimi drill-down'ida — satrning KATAKKA TUSHGAN qismi
   * (taqsimlangan to'lov har toifada o'z bo'lagi bilan ko'rinadi).
   */
  amount: number
  /** Katakka qanday kirgani: tabiiy tomonda +, qarshi tomonda −. */
  signed: number
  /** payment | invoice | expense | salary | reversal */
  kind: string
  /** O'zbekcha nom — SERVERDAN. */
  kindLabel: string
  isReversal: boolean
  refId: string | null
  title: string
  person: string | null
  actorName: string | null
  receiptNo: number | null
  method: string | null
  memo: string | null
  createdAt: string
}

/** P&L katagining drill-down javobi. `total` — katakning o'zi. */
export interface LedgerLines {
  scope: string
  from: string
  to: string
  total: number
  count: number
  /** true = ro'yxat kesilgan; `total` BARIBIR to'liq. */
  truncated: boolean
  lines: LedgerLine[]
}

/** Pul oqimi katagi. `amount` HAR DOIM `inflow − outflow` (chiqim manfiy). */
export interface CashFlowCell {
  inflow: number
  outflow: number
  amount: number
}

/** Toifaning bitta qatori. */
export interface CashFlowCategoryRow {
  /** `revenue:tuition`, `expense:rent`, `advance`, `transfer`, `other` … */
  key: string
  /** income | expense | other */
  kind: string
  /** O'zbekcha nom — SERVERDAN. */
  label: string
  months: CashFlowCell[]
  total: CashFlowCell
}

/** Bo'lim: tushumlar, chiqimlar yoki boshqa harakatlar. */
export interface CashFlowSection {
  kind: string
  label: string
  rows: CashFlowCategoryRow[]
  months: CashFlowCell[]
  total: CashFlowCell
}

/** Pul oqimi toifalar kesimida. */
export interface CashFlowStatement {
  from: string
  to: string
  account: string | null
  /** Ustunlar: "YYYY-MM". */
  months: string[]
  sections: CashFlowSection[]
  monthTotals: CashFlowCell[]
  total: CashFlowCell
  opening: number[]
  closing: number[]
  openingBalance: number
  closingBalance: number
}

/** Toifa katagining drill-down javobi. */
export interface CashFlowLines {
  scope: string
  from: string
  to: string
  total: CashFlowCell
  count: number
  truncated: boolean
  lines: LedgerLine[]
}

/** KPI: joriy davr, oldingi davr va o'zgarish foizi (null = taqqoslab bo'lmaydi). */
export interface FinanceKpi {
  current: number
  previous: number
  changePercent: number | null
}

/** Kunlik grafikning bitta ustuni. */
export interface FinanceDay {
  date: string
  inflow: number
  outflow: number
  net: number
}

/** To'lov usuli kesimi — FAQAT to'lovlar (chiqimda usul saqlanmaydi). */
export interface FinanceMethodRow {
  method: string
  label: string
  inflow: number
  outflow: number
  amount: number
  count: number
}

/** "Moliya hisobotlari" paneli. */
export interface FinanceDashboard {
  from: string
  to: string
  previousFrom: string
  previousTo: string
  inflow: FinanceKpi
  outflow: FinanceKpi
  net: FinanceKpi
  openingBalance: number
  closingBalance: number
  days: FinanceDay[]
  sections: CashFlowSection[]
  methods: FinanceMethodRow[]
  methodsTotal: FinanceMethodRow
}

/* =========================================================================
   Xatolarni o'zbekcha xabarga aylantirish
   ========================================================================= */

/**
 * Axios xatosini bitta o'qiladigan jumlaga aylantiradi (`financeReports.ts`
 * dagi `toUzbekError` bilan bir xil naqsh — `useAsync` xatoning `message`
 * ini to'g'ridan-to'g'ri ekranga chiqaradi).
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

/** `undefined` filtrlarni query'dan olib tashlaydi. */
function clean(params: Record<string, string | number | undefined>) {
  return Object.fromEntries(Object.entries(params).filter(([, v]) => v !== undefined))
}

/* =========================================================================
   So'rovlar
   ========================================================================= */

/** "Moliya hisobotlari" paneli. Sana formati "YYYY-MM-DD". */
export async function getFinanceDashboard(from: string, to: string): Promise<FinanceDashboard> {
  try {
    const { data } = await api.get<FinanceDashboard>('/admin/finance/dashboard', {
      params: { from, to },
    })
    return data
  } catch (e) {
    throw toUzbekError(e, 'Moliya hisobotlari')
  }
}

/** Yil × oy P&L. */
export async function getProfitLossMatrix(year: number): Promise<ProfitLossMatrix> {
  try {
    const { data } = await api.get<ProfitLossMatrix>('/admin/finance/pnl/matrix', {
      params: { year },
    })
    return data
  } catch (e) {
    throw toUzbekError(e, 'Yillik foyda va zarar')
  }
}

/**
 * P&L katagining ortidagi jurnal satrlari.
 * @param account bitta hisob kodi yoki guruh (`revenue:*`, `expense:*`).
 */
export async function getLedgerLines(
  account: string,
  from: string,
  to: string,
): Promise<LedgerLines> {
  try {
    const { data } = await api.get<LedgerLines>('/admin/finance/ledger/lines', {
      params: { account, from, to },
    })
    return data
  } catch (e) {
    throw toUzbekError(e, 'Jurnal satrlari')
  }
}

/** Pul oqimi toifalar kesimida. Davr 12 oydan uzun bo'lsa server 400 beradi. */
export async function getCashFlowStatement(
  from: string,
  to: string,
  account?: string,
): Promise<CashFlowStatement> {
  try {
    const { data } = await api.get<CashFlowStatement>('/admin/finance/cashflow/statement', {
      params: clean({ from, to, account }),
    })
    return data
  } catch (e) {
    throw toUzbekError(e, 'Pul oqimi (toifalar)')
  }
}

/** Toifa yoki to'lov usuli katagining ortidagi pul satrlari. */
export async function getCashFlowLines(params: {
  key?: string
  method?: string
  from: string
  to: string
  account?: string
}): Promise<CashFlowLines> {
  try {
    const { data } = await api.get<CashFlowLines>('/admin/finance/cashflow/lines', {
      params: clean({ ...params }),
    })
    return data
  } catch (e) {
    throw toUzbekError(e, 'Pul harakatlari')
  }
}
