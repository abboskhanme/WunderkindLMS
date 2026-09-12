/**
 * Ota-ona / o'quvchi moliya ko'rinishi (P1-19) — SPEC §3.7, §4.7.
 *
 * NEGA `billing.ts` EMAS
 * ----------------------
 * `billing.ts` va `payments.ts` — P1-06 da MUZLATILGAN admin/kassa shartnomasi:
 * ularning har bir manzili `/admin/billing/*` yoki `/cash/*` ostida, ya'ni
 * `Roles.FinanceStaff` yoki `FinanceAction.AcceptPayment` bilan yopilgan.
 * Ota-ona u yerga umuman kira olmaydi (403), va kirsa ham javobda unga
 * ko'rsatilmasligi kerak bo'lgan ichki id'lar bor (kassir, smena, hisob-faktura).
 *
 * Shuning uchun portalning o'z manzillari bor — `/api/student/*`, egasini
 * SERVER aniqlaydi (`PortalFinanceController`). Bu yerdagi tiplar ham
 * portalga xos: ekranga chiqadigan maydonlar + chek uchun bitta `paymentId`.
 *
 * PULNI FRONTEND HISOBLAMAYDI. Oy jamlari (`amount`, `discount`, `paid`,
 * `remaining`) serverdan TAYYOR keladi — bu yerda ham, sahifada ham hech narsa
 * `reduce` qilinmaydi.
 */
import { api } from '../client'

/** open | partial | paid | void */
export type PortalInvoiceStatus = 'open' | 'partial' | 'paid' | 'void'

/** FAQAT YORLIQ — provayder integratsiyasi yo'q (SPEC §8.1 Q13). */
export type PortalPaymentMethod = 'cash' | 'card' | 'transfer' | 'online'

/** Bitta oy ichidagi bitta toifa: o'qish, avtobus, ovqat, yotoqxona. */
export interface PortalCategoryLine {
  categoryCode: string
  categoryName: string
  /** To'liq summa, chegirmasiz */
  amount: number
  /** Qo'llangan (tasdiqlangan) chegirma */
  discount: number
  /** To'lash kerak = amount − discount */
  payable: number
  /** To'langan (storno qilingan pul hisobga olinmaydi) */
  paid: number
  /** Qoldiq = payable − paid */
  remaining: number
  status: PortalInvoiceStatus
  isOverdue: boolean
  /** "YYYY-MM-DD" */
  dueOn: string
}

/** Bitta oy — jamlar SERVERDA qo'shilgan; `void` qatorlar jamga kirmaydi. */
export interface PortalMonth {
  /** "YYYY-MM" */
  periodMonth: string
  amount: number
  discount: number
  payable: number
  paid: number
  remaining: number
  hasOverdue: boolean
  categories: PortalCategoryLine[]
}

/** Qarzning bitta qatori: qaysi oy, qaysi toifa, qancha. */
export interface PortalDebtLine {
  /** "YYYY-MM" */
  periodMonth: string
  categoryCode: string
  categoryName: string
  remaining: number
  isOverdue: boolean
  /** "YYYY-MM-DD" */
  dueOn: string
}

/** To'lovning bitta toifaga tushgan qismi. */
export interface PortalPaymentPart {
  /** "YYYY-MM" */
  periodMonth: string
  categoryCode: string
  categoryName: string
  amount: number
}

/** Storno (bekor qilish) yozuvi — sababi majburiy. */
export interface PortalReversal {
  /** Bekor qilish chekining raqami */
  receiptNo: number
  reversedAt: string
  reason: string
}

/**
 * To'lov qatori. `paymentId` — YAGONA ichki id va u ekranda KO'RSATILMAYDI:
 * faqat chek PDF'ining manzilini yig'ish uchun.
 *
 * Storno qatorining o'zi ro'yxatda alohida turmaydi — u bekor qilgan
 * to'lovning ichida `reversal` bo'lib keladi.
 */
export interface PortalPayment {
  paymentId: string
  receiptNo: number
  receivedAt: string
  amount: number
  method: PortalPaymentMethod
  note?: string
  /** Taqsimlanmagan qoldiq (avans) */
  unallocated: number
  /** null emas bo'lsa — bu to'lov bekor qilingan */
  reversal: PortalReversal | null
  parts: PortalPaymentPart[]
}

/** Moliya ekranining butun ma'lumoti. */
export interface PortalFinance {
  studentName: string
  className: string
  /** Jami qarz (musbat son). 0 = qarzsiz */
  debt: number
  /** Taqsimlanmagan avans */
  credit: number
  /** Yangi oydan eskisiga */
  months: PortalMonth[]
  /** Eski oydan yangisiga — avval nimani to'lash kerak */
  debtLines: PortalDebtLine[]
  /** Yangisidan eskisiga; storno qatorlarisiz */
  payments: PortalPayment[]
}

/**
 * O'quvchining moliyaviy kartochkasi.
 *
 * `studentId` FAQAT admin/xodim uchun (o'quvchi kartochkasi sahifasi).
 * O'quvchi va ota-ona uchun server uni butunlay e'tiborsiz qoldiradi va
 * egasini JWT'dan aniqlaydi — begona bolaning qarzini ko'rib bo'lmaydi.
 */
export async function getPortalFinance(studentId?: string): Promise<PortalFinance> {
  const { data } = await api.get<PortalFinance>('/student/billing', {
    params: studentId ? { studentId } : undefined,
  })
  return data
}

/**
 * Chek PDF'ini yangi oynada ochadi (SPEC §4.7 — ota-onadagi mustaqil nusxa).
 *
 * Oyna `await` dan OLDIN ochiladi: brauzer popup'ni foydalanuvchi bosgan
 * lahzada ruxsat etadi, javob kelgandan keyin esa bloklaydi. Oyna ochilmasa
 * (mobil brauzer, blokirovka) — fayl yuklab olinadi, ya'ni tugma har holda
 * ishlaydi.
 */
export async function openReceiptPdf(paymentId: string, studentId?: string): Promise<void> {
  const tab = window.open('', '_blank', 'noopener,noreferrer')

  try {
    const { data } = await api.get<Blob>(`/student/receipts/${paymentId}.pdf`, {
      responseType: 'blob',
      params: studentId ? { studentId } : undefined,
    })

    // `responseType: 'blob'` xato javobni ham blob qiladi — JSON kelsa bu PDF emas.
    if (data.type.includes('application/json')) {
      throw new Error('Chek topilmadi')
    }

    const url = URL.createObjectURL(data)
    if (tab) {
      tab.location.href = url
    } else {
      const a = document.createElement('a')
      a.href = url
      a.download = `chek-${paymentId}.pdf`
      a.click()
    }
    // Darrov bekor qilsak ochilayotgan oyna bo'sh qoladi — nusxa ko'chirilguncha turadi.
    window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
  } catch (e) {
    tab?.close()
    throw e
  }
}
