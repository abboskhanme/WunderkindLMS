/**
 * "Kassa kuni" — kunlik kassa paneli klienti.
 *
 * Backend: `SchoolLms.Server/Controllers/CashDayController.cs`
 *   · `GET /api/admin/finance/cash-day?date=YYYY-MM-DD`
 *   · `GET /api/admin/finance/cash-day/calendar?month=YYYY-MM`
 *
 * PULNI FRONTEND HISOBLAMAYDI (`financeReports.ts` sarlavhasidagi qoida,
 * manba — `BillingDtos.cs` 2-qoidasi). Bu yerdagi HAMMA summa serverdan
 * tayyor keladi: ochilish, kirim, chiqim, yopilish, turlar va toifalar
 * kesimi, taqsimlanmagan avans, ochiq smenadagi kutilgan naqd. Sahifada
 * birorta `+` yoki `reduce` yo'q — u faqat chizadi.
 *
 * RUXSAT (SPEC §4.3): `/admin/finance/*` — faqat `admin` va `superadmin`.
 * Kassir 403 oladi. Shuning uchun menyuda ham, marshrutda ham bu ekran
 * kassirga ko'rsatilmaydi — 403 ni ushlab ko'rsatish emas, tugmani
 * chizmaslik.
 *
 * Tiplar SHU YERDA (moneyFlow.ts bilan bir xil sabab): bu modulning
 * shartnomasi bitta faylda tursin, `types/index.ts` esa boshqa vazifalarniki.
 */
import axios from 'axios'
import { api } from '../client'

/* =========================================================================
   Tiplar — backend DTO'lariga AYNAN mos
   (SchoolLms.Application/Billing/CashDayQueries.cs).
   ========================================================================= */

/** Bitta pul hisobi (`cash` yoki `bank`) kesimi. `closing` = `opening` + `net`. */
export interface CashDayAccount {
  /** `cash` | `bank` | `total` (jami satr uchun). */
  account: string
  opening: number
  inflow: number
  outflow: number
  net: number
  closing: number
}

/** Jurnal manba turi — `ledger_entries.ref_type`. */
export type CashDayKind = 'payment' | 'expense' | 'reversal' | 'invoice' | 'salary'

/** Kunning bitta pul harakati = jurnalning `cash`/`bank` hisobiga tushgan bitta satri. */
export interface CashDayMovement {
  /** Jurnal satri id'si — ro'yxatdagi barqaror kalit. */
  entryId: number
  /** `cash` | `bank` */
  account: string
  /** `debit` = kirim, `credit` = chiqim. */
  direction: 'debit' | 'credit'
  /** Har doim musbat (jurnal qoidasi). */
  amount: number
  /** Kirim uchun +amount, chiqim uchun −amount. */
  signed: number
  kind: CashDayKind
  /** true = storno. YASHIRILMAYDI — alohida belgi bilan ko'rsatiladi. */
  isReversal: boolean
  /** To'lov / chiqim id'si. Storno'da — ASL to'lovniki. */
  refId: string | null
  /** Bir qatorlik tavsif — SERVER yig'adi (o'quvchi ismi yoki chiqim toifasi). */
  title: string
  /** Jurnaldagi izoh. Storno'da — SABAB (majburiy, SPEC §4.3). */
  memo: string | null
  receiptNo: number | null
  /** `cash` | `card` | `transfer` | `online` */
  method: string | null
  /** Kassir (to'lov) yoki yozuvni kiritgan xodim (chiqim/storno). */
  actorName: string | null
  /** Chiqim toifasi (`salary`, `utilities` …). */
  category: string | null
  createdAt: string
}

/** Turlar kesimi: qarshi hisob bo'yicha. `amount` belgili (kirim +, chiqim −). */
export interface CashDayTypeRow {
  key: string
  account: string
  /** O'zbekcha nom — SERVERDAN. UI o'z lug'atini saqlamaydi. */
  label: string
  isReversal: boolean
  count: number
  amount: number
}

/** To'lov toifalari kesimi. `amount` belgili: to'lov +, storno −. */
export interface CashDayCategoryRow {
  categoryId: string
  categoryCode: string
  categoryName: string
  amount: number
}

/** Hozir ochiq smena: kim kassada, qachondan beri, javonda qancha bo'lishi kerak. */
export interface CashDayShift {
  shiftId: string
  cashierName: string
  openedAt: string
  openingFloat: number
  cashSoFar: number
  /** `openingFloat` + `cashSoFar`. Karta/o'tkazma SANALMAYDI (SPEC §8.1 Q13). */
  expectedCashSoFar: number
  nonCashSoFar: number
  paymentsCount: number
}

/** Bir kunning to'liq manzarasi. */
export interface CashDay {
  /** "YYYY-MM-DD" */
  date: string
  /** Naqd + bank birgalikda. */
  total: CashDayAccount
  /** `cash`, keyin `bank` — har doim ikkalasi ham. */
  accounts: CashDayAccount[]
  /** Yangisidan eskisiga. */
  movements: CashDayMovement[]
  /** true = ro'yxat kesilgan; yig'indilar BARIBIR to'liq ma'lumotdan. */
  movementsTruncated: boolean
  movementsTotal: number
  topFive: CashDayMovement[]
  byType: CashDayTypeRow[]
  byCategory: CashDayCategoryRow[]
  allocatedTotal: number
  /** Taqsimlanmagan qism = avans (pul keldi, hisob-fakturaga biriktirilmadi). */
  unallocatedTotal: number
  openShifts: CashDayShift[]
}

/** Kalendar katakchasi. */
export interface CashMonthDay {
  /** "YYYY-MM-DD" */
  date: string
  inflow: number
  outflow: number
  net: number
  closing: number
  /** false = o'sha kuni jurnalda bitta ham yozuv yo'q. */
  hasMovement: boolean
}

/** Oylik kalendar. `days` — oyning HAR kuni, harakatsizi ham. */
export interface CashMonth {
  /** "YYYY-MM-01" */
  month: string
  opening: number
  inflow: number
  outflow: number
  net: number
  closing: number
  days: CashMonthDay[]
}

/* =========================================================================
   So'rovlar
   ========================================================================= */

/**
 * Bir kunning to'liq manzarasi. Sana berilmasa — server bugunni oladi
 * (maktab mintaqasida, brauzerning mintaqasida EMAS).
 *
 * @param date "YYYY-MM-DD". Boshqa format — server 400 beradi (ataylab:
 *   "01.02.2026" ni ikki xil o'qish mumkin, moliyada bu bir oylik siljish).
 */
export async function getCashDay(date?: string): Promise<CashDay> {
  try {
    const { data } = await api.get<CashDay>('/admin/finance/cash-day', {
      params: date ? { date } : undefined,
    })
    return data
  } catch (e) {
    throw toUzbekError(e, 'Kassa kuni')
  }
}

/**
 * Oylik kalendar: har kun uchun sof harakat va kun oxiridagi qoldiq.
 * @param month "YYYY-MM" (to'liq sana ham qabul qilinadi).
 */
export async function getCashMonth(month?: string): Promise<CashMonth> {
  try {
    const { data } = await api.get<CashMonth>('/admin/finance/cash-day/calendar', {
      params: month ? { month } : undefined,
    })
    return data
  } catch (e) {
    throw toUzbekError(e, 'Kassa kalendari')
  }
}

/* =========================================================================
   Xatolarni o'zbekcha xabarga aylantirish
   ========================================================================= */

/**
 * Axios xatosini foydalanuvchi o'qiy oladigan bitta jumlaga aylantiradi
 * (`financeReports.ts` dagi `toUzbekError` bilan bir xil naqsh: `useAsync`
 * xatoning `message` ini to'g'ridan-to'g'ri ekranga chiqaradi, axios esa u
 * yerga "Request failed with status code 403" yozadi).
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
    return new Error("Bu ekranni ko'rishga ruxsatingiz yo'q — u faqat admin va direktor uchun.")
  }
  if (status === 404) {
    return new Error(`${what}: server bu manzilni topmadi.`)
  }
  if (status >= 500) {
    return new Error(`${what}: serverda xatolik (${status}). Keyinroq urinib ko'ring.`)
  }
  return new Error(`${what}: so'rov rad etildi (${status}).`)
}
