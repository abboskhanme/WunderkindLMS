/**
 * Bonus / jarima (F11.01, F11.02) — `PayrollAdjustmentsController.cs` ning ko'zgusi.
 *
 *   GET  /admin/hr/adjustments?kind&employeeKind&employeeId&periodYear&periodMonth
 *   POST /admin/hr/adjustments                       { employeeKind, employeeId, kind, reasonId, amount, periodYear, periodMonth, comment?, imageUrl? }
 *   POST /admin/hr/adjustments/{id}/reverse           { reason }
 *   GET  /admin/hr/adjustment-reasons?kind
 *   POST /admin/hr/adjustment-reasons                 { kind, name, position }
 *   PUT  /admin/hr/adjustment-reasons/{id}             { name, isActive, position }
 *
 * NIMA UCHUN KERAK
 * ----------------
 * EduSchool'dagi "Bonus" va "Jarima" — xodimga (o'qituvchi yoki boshqa xodim)
 * bir martalik qo'lda kiritiladigan to'lov. HR qoidalar dvigateli
 * (`hr_rules`, avtomatik) BILAN ARALASHTIRMANG — bu qo'lda yoziladigan yozuv.
 *
 * SPEC §4.4 — `createdBy` HECH QAYERDA yuborilmaydi: server JWT'dan oladi.
 * Tanada uchrasa 400 `identity_in_body` qaytaradi.
 *
 * O'CHIRISH VA TAHRIRLASH YO'Q (registr uchun) — va bo'lmaydi: jadval
 * bazada faqat qo'shiladi (`app_rw` da UPDATE/DELETE yo'q). Xato yozuv
 * `reverse` bilan tuzatiladi. Sabab katalogi esa moliyaviy emas — `PUT`
 * bilan tahrirlanadi (nomi, tartibi, faolligi; turi — `kind` — muzlaydi).
 */
import { api } from '../client'

export type AdjustmentKind = 'bonus' | 'penalty'
export type EmployeeKind = 'teacher' | 'staff'

/** Sabab katalogi qatori — server javobining AYNAN ko'zgusi (camelCase). */
export interface AdjustmentReason {
  id: string
  kind: AdjustmentKind
  name: string
  isActive: boolean
  position: number
}

export interface AdjustmentReasonInput {
  kind: AdjustmentKind
  name: string
  position: number
}

/** `kind` YO'Q — u yaratilgandan keyin o'zgarmaydi (server ham shunday talab qiladi). */
export interface UpdateAdjustmentReasonInput {
  name: string
  isActive: boolean
  position: number
}

/** Bonus/jarima yozuvi — server javobining AYNAN ko'zgusi. */
export interface PayrollAdjustment {
  id: string
  employeeKind: EmployeeKind
  employeeId: string
  employeeName: string
  kind: AdjustmentKind
  reasonId: string
  reasonName: string
  amount: number
  periodYear: number
  periodMonth: number
  comment: string | null
  imageUrl: string | null
  createdBy: string
  createdByName: string
  createdAt: string
  /** Storno bo'lsa — qaysi yozuvni bekor qilyapti. */
  reversalOf: string | null
  /** Shu yozuv keyinchalik storno qilinganmi. */
  reversed: boolean
}

export interface CreatePayrollAdjustmentInput {
  employeeKind: EmployeeKind
  employeeId: string
  kind: AdjustmentKind
  reasonId: string
  amount: number
  periodYear: number
  periodMonth: number
  comment?: string
  imageUrl?: string
}

export interface PayrollAdjustmentFilters {
  kind?: AdjustmentKind
  employeeKind?: EmployeeKind
  employeeId?: string
  periodYear?: number
  periodMonth?: number
}

/** O'zbekcha yorliq — ekranlarda bitta manbadan. */
export const adjustmentKindLabels: Record<AdjustmentKind, string> = {
  bonus: 'Bonus',
  penalty: 'Jarima',
}

// -----------------------------------------------------------------
//  Registr (F11.01)
// -----------------------------------------------------------------

export async function getPayrollAdjustments(
  filters: PayrollAdjustmentFilters = {},
): Promise<PayrollAdjustment[]> {
  const { data } = await api.get<PayrollAdjustment[]>('/admin/hr/adjustments', { params: filters })
  return data
}

export async function createPayrollAdjustment(
  input: CreatePayrollAdjustmentInput,
): Promise<PayrollAdjustment> {
  const { data } = await api.post<PayrollAdjustment>('/admin/hr/adjustments', input)
  return data
}

/** STORNO — yozuvni tuzatishning YAGONA yo'li. Sabab majburiy. */
export async function reversePayrollAdjustment(
  id: string,
  reason: string,
): Promise<PayrollAdjustment> {
  const { data } = await api.post<PayrollAdjustment>(`/admin/hr/adjustments/${id}/reverse`, { reason })
  return data
}

// -----------------------------------------------------------------
//  Sabab katalogi (F11.02)
// -----------------------------------------------------------------

export async function getAdjustmentReasons(kind?: AdjustmentKind): Promise<AdjustmentReason[]> {
  const { data } = await api.get<AdjustmentReason[]>('/admin/hr/adjustment-reasons', {
    params: kind ? { kind } : undefined,
  })
  return data
}

export async function createAdjustmentReason(
  input: AdjustmentReasonInput,
): Promise<AdjustmentReason> {
  const { data } = await api.post<AdjustmentReason>('/admin/hr/adjustment-reasons', input)
  return data
}

export async function updateAdjustmentReason(
  id: string,
  input: UpdateAdjustmentReasonInput,
): Promise<AdjustmentReason> {
  const { data } = await api.put<AdjustmentReason>(`/admin/hr/adjustment-reasons/${id}`, input)
  return data
}
