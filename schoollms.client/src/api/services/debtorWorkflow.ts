/**
 * QARZDORLAR BILAN ISHLASH — klient
 * (docs/modules/existing-module-gaps.md §3.5).
 *
 * Server: `SchoolLms.Server/Controllers/DebtorWorkflowController.cs`.
 * Tiplar backend DTO'lariga AYNAN mos (`Dtos/DebtorWorkflowDtos.cs`,
 * camelCase) — nom o'zgarsa ekran jimgina bo'sh qolardi.
 *
 * RUXSAT (SPEC §4.3): `/admin/finance/*` — faqat `admin` va `superadmin`.
 * Kassir 403 oladi, shuning uchun UI bu tugmalarni unga UMUMAN chizmaydi.
 *
 * PULNI FRONTEND HISOBLAMAYDI. Bu yerda bitta summa bor —
 * `BrokenPromise.debt` — va u serverdan tayyor keladi. "Va'da buzildimi"
 * degan hukm ham serverniki (`promiseBroken`): u sana bilan qarzni BIRGA
 * tekshiradi, brauzer esa faqat ko'rsatadi.
 */
import axios from 'axios'
import { api } from '../client'

/* =========================================================================
   Tiplar
   ========================================================================= */

/** Rangli holat katalogining bitta qatori. */
export interface DebtorStatus {
  id: string
  name: string
  /** `#RRGGBB` yoki bo'sh satr (rang yo'q). */
  color: string
  /** Bu holat aynan qachon qo'yiladi — administratorlar uchun eslatma. */
  hint?: string | null
  position: number
  /** false = yangi amalda tanlab bo'lmaydi, eskilarida ko'rinib turaveradi. */
  isActive: boolean
}

/** Holat yaratish/tahrirlash so'rovi. */
export interface SaveDebtorStatus {
  name: string
  color?: string
  hint?: string | null
  position?: number
  isActive?: boolean
}

/** Qarzdor bo'yicha bitta amal (tarix qatori). */
export interface DebtorAction {
  id: string
  studentId: string
  statusId?: string | null
  statusName?: string | null
  statusColor?: string | null
  comment: string
  /** "YYYY-MM-DD" yoki null. */
  promisedOn?: string | null
  /** Va'da sanasi o'tib ketganmi (qarz tekshirilmagan — u `promiseBroken` da). */
  promiseOverdue: boolean
  createdBy: string
  createdByName: string
  createdAt: string
}

/** "Amal qo'shish" so'rovi. Kim yozayotgani SERVERDA aniqlanadi (SPEC §4.4). */
export interface CreateDebtorAction {
  comment: string
  statusId?: string | null
  promisedOn?: string | null
}

/** Qarzdorlar ro'yxatiga qo'shiladigan ustunlar (bitta o'quvchi). */
export interface DebtorWorkflowRow {
  studentId: string
  fullName: string
  className: string
  /** JORIY holat = eng oxirgi tirik amalning holati (server hisoblaydi). */
  statusId?: string | null
  statusName?: string | null
  statusColor?: string | null
  lastActionAt?: string | null
  lastComment?: string | null
  lastActionByName?: string | null
  promisedOn?: string | null
  /** Va'da o'tib ketgan VA qarz hali ochiq — server hukmi. */
  promiseBroken: boolean
  actionCount: number
}

/** Buzilgan va'da (direktor panelidagi beshinchi anomaliya bilan bir xil shart). */
export interface BrokenPromise {
  actionId: string
  studentId: string
  fullName: string
  className: string
  parentPhone: string
  promisedOn: string
  daysLate: number
  /** Hali ochiq qarz — SERVER hisoblaydi. */
  debt: number
  statusName?: string | null
  comment: string
  createdByName: string
  createdAt: string
}

/* =========================================================================
   Xatolarni o'zbekcha xabarga aylantirish
   ========================================================================= */

/** Server bergan `message` bo'lsa — o'sha; bo'lmasa holat kodiga qarab matn. */
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
    return new Error('Qarzdorlar bilan ishlash faqat admin va direktor uchun.')
  }
  if (status === 404) return new Error(`${what}: topilmadi.`)
  if (status >= 500) return new Error(`${what}: serverda xatolik (${status}).`)
  return new Error(`${what}: so'rov rad etildi (${status}).`)
}

/* =========================================================================
   1) Holat ma'lumotnomasi
   ========================================================================= */

/** Katalog. `includeInactive` — sozlamalar ekrani uchun (chiqarilganlar bilan). */
export async function getDebtorStatuses(includeInactive = false): Promise<DebtorStatus[]> {
  try {
    const { data } = await api.get<DebtorStatus[]>('/admin/finance/debtor-statuses', {
      params: { includeInactive },
    })
    return data
  } catch (e) {
    throw toUzbekError(e, 'Qarzdor holatlari')
  }
}

export async function createDebtorStatus(request: SaveDebtorStatus): Promise<DebtorStatus> {
  try {
    const { data } = await api.post<DebtorStatus>('/admin/finance/debtor-statuses', request)
    return data
  } catch (e) {
    throw toUzbekError(e, "Holat qo'shish")
  }
}

export async function updateDebtorStatus(
  id: string,
  request: SaveDebtorStatus,
): Promise<DebtorStatus> {
  try {
    const { data } = await api.put<DebtorStatus>(`/admin/finance/debtor-statuses/${id}`, request)
    return data
  } catch (e) {
    throw toUzbekError(e, 'Holatni saqlash')
  }
}

/**
 * Holatni katalogdan CHIQARADI (`is_active = false`) — O'CHIRMAYDI.
 * Server qatorni hech qachon o'chirmaydi: chiqarilgan holat eski amallarda
 * ko'rinib turishi kerak.
 */
export async function retireDebtorStatus(id: string): Promise<DebtorStatus> {
  try {
    const { data } = await api.delete<DebtorStatus>(`/admin/finance/debtor-statuses/${id}`)
    return data
  } catch (e) {
    throw toUzbekError(e, 'Holatni chiqarish')
  }
}

/* =========================================================================
   2) Amallar
   ========================================================================= */

/**
 * Qarzdorlar ro'yxatining yangi ustunlari. Qarz summasi bu javobda YO'Q —
 * u `getDebtors()` dan keladi va ekranda `studentId` bo'yicha birlashtiriladi.
 */
export async function getDebtorWorkflow(className?: string): Promise<DebtorWorkflowRow[]> {
  try {
    const { data } = await api.get<DebtorWorkflowRow[]>('/admin/finance/debtors/workflow', {
      params: className ? { className } : undefined,
    })
    return data
  } catch (e) {
    throw toUzbekError(e, 'Qarzdorlar ish oqimi')
  }
}

/** Bitta o'quvchining amallari tarixi (o'chirilganlarsiz), eng yangisidan. */
export async function getDebtorActions(studentId: string): Promise<DebtorAction[]> {
  try {
    const { data } = await api.get<DebtorAction[]>(
      `/admin/finance/debtors/${encodeURIComponent(studentId)}/actions`,
    )
    return data
  } catch (e) {
    throw toUzbekError(e, 'Amallar tarixi')
  }
}

/** "Amal qo'shish". Izoh majburiy; holat va va'da sanasi ixtiyoriy. */
export async function addDebtorAction(
  studentId: string,
  request: CreateDebtorAction,
): Promise<DebtorAction> {
  try {
    const { data } = await api.post<DebtorAction>(
      `/admin/finance/debtors/${encodeURIComponent(studentId)}/actions`,
      request,
    )
    return data
  } catch (e) {
    throw toUzbekError(e, "Amal qo'shish")
  }
}

/**
 * Amalni YUMSHOQ o'chiradi — qator bazada qoladi (§3.5: nima va'da
 * qilingani yo'qolmasligi kerak). Shu sabab UI'da "butunlay o'chirish"
 * degan tugma yo'q va bo'lmaydi.
 */
export async function deleteDebtorAction(id: string): Promise<DebtorAction> {
  try {
    const { data } = await api.delete<DebtorAction>(`/admin/finance/debtor-actions/${id}`)
    return data
  } catch (e) {
    throw toUzbekError(e, "Amalni o'chirish")
  }
}

/** Buzilgan va'dalar: sana o'tgan, qarz ochiq. */
export async function getBrokenPromises(limit = 200): Promise<BrokenPromise[]> {
  try {
    const { data } = await api.get<BrokenPromise[]>('/admin/finance/debtors/broken-promises', {
      params: { limit },
    })
    return data
  } catch (e) {
    throw toUzbekError(e, "Buzilgan va'dalar")
  }
}
