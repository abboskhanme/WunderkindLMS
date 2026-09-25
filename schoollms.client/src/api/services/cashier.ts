/**
 * Kassa ish o'rnining API klienti (P1-16). SPEC §4.2, §4.3, §4.4, §4.7.
 *
 * NEGA ALOHIDA FAYL — `payments.ts` / `cashShifts.ts` NIMA UCHUN ISHLATILMADI
 * ==========================================================================
 * Ikkalasi ham P1-06 da MUZLATILGAN shartnoma va hozircha `notImplemented(...)`
 * tashlaydi; ustiga, ular yozilgan paytda manzillar TAXMIN qilingan edi va
 * haqiqiy marshrutlardan farq qiladi (docs/PENDING_WIRING.md, P1-10 §11):
 *
 *   stub                              haqiqiy (jonli stack'da tekshirilgan)
 *   GET  /cashier/shifts/current  →   GET  /cash/shifts/current
 *   POST /cashier/shifts/open     →   POST /cash/shifts/open
 *   GET  /admin/billing/shifts    →   GET  /cash/shifts
 *
 * O'sha ikki fayl Faza 1.F da PARALLEL ishlayotgan boshqa agentlarga tegishli,
 * shuning uchun bu vazifa ularga tegmaydi. Stub'lar haqiqiy `api.*` chaqiruvga
 * o'tkazilgach, bu fayl ularga bir qatorlik delegatsiyaga aylanishi mumkin —
 * imzolar ataylab bir xil qilib yozilgan.
 *
 * BU YERDA `updatePayment` YOKI `deletePayment` YO'Q — VA HECH QACHON BO'LMAYDI.
 * To'lov o'zgarmas (SPEC §4.1): backend'da bunday endpoint yozilmagan, bazada
 * esa `app_rw` roli `payments` jadvalini o'zgartira olmaydi (42501). Kassir
 * xatosi storno bilan tuzatiladi va storno — admin/direktor amali, kassirniki
 * emas (SPEC §4.3), shuning uchun bu klientda uning ham o'rni yo'q.
 *
 * SPEC §4.4: `cashierId`, `cashShiftId`, `receiptNo`, `receivedAt` HECH QAYERDA
 * yuborilmaydi — backend ularni JWT'dan va kassirning ochiq smenasidan oladi,
 * so'rov tanasida uchrasa 400 `identity_in_body` qaytaradi.
 */
import type { AllocationSuggestion, CashShift, Payment, PaymentMethod, ZReport } from '@/types'
import { api } from '../client'

/* ========================================================================
   O'quvchi qidiruvi
   ======================================================================== */

/**
 * Kassa qidiruvi qaytaradigan minimal o'quvchi kartochkasi.
 * Backend: `CashierStudentDto` (`CashierStudentsController.cs`).
 *
 * Balans bu yerda ATAYLAB yo'q: qarz summasining yagona manbasi
 * `suggestAllocation` qaytaradigan `remaining` qiymatlari (storno qilingan
 * to'lovlarni hisobga olmaydigan qoida bilan).
 */
export interface CashierStudent {
  id: string
  fullName: string
  className: string
  parentFullName: string
  parentPhone: string
}

/** Qidiruv satrining backend qabul qiladigan eng qisqa uzunligi. */
export const MIN_SEARCH_LENGTH = 2

/**
 * O'quvchini ismi, ota-onasi yoki telefoni bo'yicha qidirish.
 * 2 belgidan qisqa so'rovga backend bo'sh ro'yxat qaytaradi.
 */
export async function searchStudents(q: string, signal?: AbortSignal): Promise<CashierStudent[]> {
  const res = await api.get<CashierStudent[]>('/cash/students', { params: { q }, signal })
  return res.data
}

/* ========================================================================
   Smena (SPEC §4.2)
   ======================================================================== */

/**
 * Kassirning joriy OCHIQ smenasi, yoki `null`.
 *
 * Backend ochiq smena bo'lmaganda **204 No Content** qaytaradi — bu kutilgan
 * holat, xato emas. Axios'da 204 ning `data` si bo'sh satr (`''`), ya'ni
 * "falsy, lekin null emas", shuning uchun status ANIQ tekshiriladi.
 */
export async function getCurrentShift(): Promise<CashShift | null> {
  const res = await api.get<CashShift | ''>('/cash/shifts/current')
  return res.status === 204 || !res.data ? null : (res.data as CashShift)
}

/**
 * Smena ochish. Kassirda allaqachon ochiq smena bo'lsa — 409
 * `shift_already_open` (buni bazadagi noyob indeks ham kafolatlaydi).
 *
 * @param openingFloat Smena boshidagi kassadagi pul (sukut 0 — ASSUMPTIONS Q11).
 */
export async function openShift(openingFloat: number): Promise<CashShift> {
  const res = await api.post<CashShift>('/cash/shifts/open', { openingFloat })
  return res.data
}

/**
 * Smenani yopish. `countedCash` — kassir QO'LDA sanagan naqd, MAJBURIY.
 *
 * Kutilayotgan summani server ledger'dan hisoblaydi, farqni esa BAZA
 * (generated column). Ikkalasi ham FAQAT shu chaqiruvning javobida keladi —
 * yopishdan oldin ularni so'raydigan endpoint yo'q, va bo'lmasligi kerak:
 * kutilayotgan summani oldindan ko'rgan kassir sanoqni o'shanga moslab
 * yozib qo'yishi mumkin (SPEC §4.2).
 */
export async function closeShift(
  id: string,
  countedCash: number,
  note?: string,
): Promise<CashShift> {
  const res = await api.post<CashShift>(`/cash/shifts/${id}/close`, { countedCash, note })
  return res.data
}

/** Smena yakuni: usullar va toifalar kesimi, chek raqamlari oralig'i (SPEC §4.6). */
export async function getZReport(id: string): Promise<ZReport> {
  const res = await api.get<ZReport>(`/cash/shifts/${id}/z-report`)
  return res.data
}

/* ========================================================================
   To'lov
   ======================================================================== */

/** To'lovning bitta hisob-fakturaga yo'naltiriladigan qismi. */
export interface AllocationInput {
  invoiceId: string
  amount: number
}

/**
 * To'lov qabul qilish so'rovi.
 *
 * `cashierId` ham, `cashShiftId` ham YO'Q (SPEC §4.4) — ularni server
 * aniqlaydi. Ochiq smena bo'lmasa 409 `no_open_shift` va bitta ham pul
 * qatori yozilmaydi.
 */
export interface AcceptPaymentPayload {
  studentId: string
  amount: number
  /** FAQAT YORLIQ — provayder integratsiyasi yo'q (SPEC §8.1 Q13). */
  method: PaymentMethod
  note?: string
  /** Bo'sh qoldirilsa pul avans (taqsimlanmagan qoldiq) bo'lib qoladi. */
  allocations: AllocationInput[]
  /**
   * To'lov qaysi KUN qabul qilingani — "YYYY-MM-DD" (ixtiyoriy; bo'lmasa
   * bugun). Chek raqami va taqsimot o'zgarmaydi, faqat `received_at` va
   * jurnal sanasi o'sha kunga tushadi (`AcceptPaymentRequest.ReceivedOn`).
   */
  receivedOn?: string
  /**
   * To'lov QAYSI kassada olindi. Berilmasa server sukutdagi kassaga yozadi —
   * ya'ni ikkinchi kassirning puli birinchisining kassasida ko'rinardi
   * (mijoz, 2026-09-22). Kirim oynasi har doim o'zi ochilgan kassani yuboradi.
   */
  cashBoxId?: string
}

/**
 * To'lovni qabul qiladi va chek raqamini beradi. Server tomonda bitta
 * tranzaksiya: chek raqami, to'lov, taqsimot, hisob-faktura statuslari va
 * ledger yozuvlari.
 */
export async function acceptPayment(payload: AcceptPaymentPayload): Promise<Payment> {
  const res = await api.post<Payment>('/cash/payments', payload)
  return res.data
}

/**
 * Taqsimot TAKLIFI: summani eng eski qarzdan boshlab bo'lish (FIFO).
 * FIFO mantiqi SERVERDA — bu yerda qayta yozilmaydi (P1-11,
 * `PaymentService.SuggestAllocationAsync`).
 *
 * Javob o'quvchining qoldig'i bor BARCHA hisob-fakturalarini qaytaradi
 * (`remaining`), summa esa faqat `suggested` ustunini to'ldiradi. Shu sababli
 * ochiq hisob-fakturalar ro'yxati ham aynan shu chaqiruvdan olinadi:
 * `amount <= 0` da backend bo'sh ro'yxat qaytargani uchun ro'yxatni ko'rish
 * uchun {@link PROBE_AMOUNT} ishlatiladi.
 */
export async function suggestAllocation(
  studentId: string,
  amount: number,
  signal?: AbortSignal,
): Promise<AllocationSuggestion[]> {
  const res = await api.get<AllocationSuggestion[]>('/cash/payments/suggest-allocation', {
    params: { studentId, amount },
    signal,
  })
  return res.data
}

/**
 * Kassir hali summa kiritmaganda ochiq hisob-fakturalarni KO'RISH uchun
 * yuboriladigan eng kichik summa.
 *
 * Backend `amount <= 0` ni "so'rov yo'q" deb hisoblab bo'sh ro'yxat
 * qaytaradi, shu sababli 0 yubora olmaymiz. 0.01 esa ro'yxatni to'liq
 * qaytaradi va faqat birinchi qatorga 1 tiyin "taklif" qo'yadi — UI bu holda
 * `suggested` ni umuman o'qimaydi, faqat `remaining` ni ko'rsatadi.
 */
export const PROBE_AMOUNT = 0.01

/* ========================================================================
   Chek (SPEC §4.7)
   ======================================================================== */

/**
 * Chek PDF'i — blob sifatida.
 *
 * Nega oddiy `<a href>` emas: PDF endpoint'i JWT talab qiladi, token esa
 * `localStorage` da va uni faqat axios interceptor'i qo'shadi. Brauzer
 * havolasi Authorization sarlavhasini yubormaydi va 401 oladi.
 */
export async function getReceiptPdf(paymentId: string): Promise<Blob> {
  const res = await api.get<Blob>(`/receipts/${paymentId}.pdf`, { responseType: 'blob' })
  return res.data
}

/**
 * 58 mm termal chekning bitta qatori. Backend: `ReceiptPrintLineDto`.
 * `*Text` maydonlari serverda formatlangan — brauzer ularni o'zgartirmay chop etadi.
 */
export interface ReceiptPrintLine {
  /** `allocation` — abonement oyi, `advance` — avans, `refund` — storno'da qaytarilgan summa. */
  kind: 'allocation' | 'advance' | 'refund'
  invoiceId: string | null
  categoryName: string
  periodMonth: string | null
  /** `2026-yil sentyabr`; oyga bog'lanmagan qatorda bo'sh satr. */
  periodText: string
  amount: number
  amountText: string
  /** Oyning CHOP ETILGAN paytdagi qoldig'i (serverda hisoblangan). */
  remaining: number | null
  isClosed: boolean | null
  /** "to'liq yopildi" | "qoldi: …" | "hisob bekor qilingan" | null. */
  statusText: string | null
}

/** 58 mm termal chek ma'lumoti. Backend: `ReceiptPrintDto` (`GET /api/receipts/{id}`). */
export interface ReceiptPrint {
  paymentId: string
  receiptNo: number
  schoolName: string
  schoolAddress: string | null
  schoolPhone: string | null
  receivedAt: string
  receivedAtText: string
  studentId: string
  studentName: string
  className: string | null
  lines: ReceiptPrintLine[]
  total: number
  totalText: string
  method: string
  methodText: string
  cashierName: string
  isReversal: boolean
  cancelledAt: string | null
  cancelledStamp: string | null
  cancelledAtText: string | null
  printedAt: string
  printedAtText: string
  /** Chek pastidagi QR: tekshirish sahifasi manzili va uning PNG rasmi (data URL). */
  verifyUrl?: string | null
  qrDataUrl?: string | null
}

/**
 * 58 mm termal chek uchun JSON (2026-09-22). Faqat O'QIYDI; ruxsat PDF bilan
 * bir xil (kassir — faqat o'z cheki, admin/direktor — hammasi).
 */
export async function getReceiptPrint(paymentId: string, original = false): Promise<ReceiptPrint> {
  // `original` — qayta chop etish: birinchi chek bilan AYNAN bir xil ("qoldi" va "Chop etildi" to'lov paytidagi).
  const res = await api.get<ReceiptPrint>(`/receipts/${paymentId}`, { params: original ? { original: true } : undefined })
  return res.data
}

/**
 * Telegramga yuborish natijasi. Backend: `ReceiptDeliveryDto`.
 *
 * `delivered = false` — XATO EMAS: pul allaqachon qabul qilingan, ota-ona
 * esa botga ulanmagan bo'lishi mumkin. Javob har doim 200.
 */
export interface ReceiptDelivery {
  delivered: boolean
  message: string
}

/** Chekni ota-onaning Telegramiga yuborish (SPEC §4.7). */
export async function sendReceiptToTelegram(paymentId: string): Promise<ReceiptDelivery> {
  const res = await api.post<ReceiptDelivery>(`/receipts/${paymentId}/telegram`)
  return res.data
}

/* ========================================================================
   Xatolar
   ======================================================================== */

/**
 * Moliya controller'larining xato tanasi: `{ code, message }`.
 * `code` — MASHINA uchun barqaror kalit (`no_open_shift`,
 * `allocation_exceeds_invoice`, ...), `message` — ekranga chiqadigan
 * o'zbekcha matn. Qaror har doim `code` bo'yicha qabul qilinadi, matn bo'yicha
 * emas.
 */
export interface FinanceErrorBody {
  code?: string
  message?: string
}

interface AxiosLikeError {
  response?: { status?: number; data?: unknown }
  code?: string
  message?: string
}

function asAxiosLike(err: unknown): AxiosLikeError {
  return typeof err === 'object' && err !== null ? (err as AxiosLikeError) : {}
}

/** So'rov bekor qilinganmi (yangi qidiruv eskisini uzdi)? Bu xato emas. */
export function isAborted(err: unknown): boolean {
  const e = asAxiosLike(err)
  return e.code === 'ERR_CANCELED' || e.message === 'canceled'
}

/** Backend xatosining barqaror kodi (topilmasa `undefined`). */
export function financeErrorCode(err: unknown): string | undefined {
  const data = asAxiosLike(err).response?.data
  if (typeof data === 'object' && data !== null) {
    const code = (data as FinanceErrorBody).code
    if (typeof code === 'string') return code
  }
  return undefined
}

/**
 * Foydalanuvchiga ko'rsatiladigan xabar. Backend o'zbekcha matn bersa —
 * o'shani; bermasa — statusga qarab tushunarli matn.
 */
export function financeErrorMessage(err: unknown, fallback = 'Kutilmagan xatolik yuz berdi.'): string {
  const e = asAxiosLike(err)
  const data = e.response?.data
  if (typeof data === 'object' && data !== null) {
    const message = (data as FinanceErrorBody).message
    if (typeof message === 'string' && message.trim()) return message
  }
  if (e.response?.status === 403) return "Bu amal uchun ruxsatingiz yo'q."
  if (e.response?.status === 401) return 'Sessiya tugadi — qaytadan kiring.'
  if (!e.response) return "Server bilan aloqa yo'q. Internetni tekshirib, qaytadan urinib ko'ring."
  return fallback
}
