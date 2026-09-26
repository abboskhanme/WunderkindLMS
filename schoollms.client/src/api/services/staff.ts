import type { Staff, Credentials, SalaryHistory, SalaryLedger } from '@/types'
import { api, USE_MOCK } from '../client'
import type { ExpenseRecord } from './expenses'
import type { SalaryPaymentInput } from './teachers'

export interface StaffPayload {
  fullName: string
  position: string
  newPassword?: string
  /** undefined — o'zgarmaydi; '' — olib tashlanadi; '/uploads/…' — yangi rasm */
  avatarUrl?: string
  /** undefined — o'zgarmaydi (yaratishda bo'sh). `+998 97 666 66 66` */
  phone?: string
  /** Oylik maosh, so'm. undefined — o'zgarmaydi (yaratishda 0). Manfiy — 400. */
  salary?: number
  /** Maosh qaysi kundan hisoblanadi ("YYYY-MM-DD"). undefined — o'zgarmaydi. */
  salaryStartDate?: string
}

export async function getStaff(): Promise<Staff[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Staff[]>('/admin/staff')
  return data
}

export async function createStaff(payload: StaffPayload): Promise<Staff> {
  const { data } = await api.post<Staff>('/admin/staff', payload)
  return data
}

export async function updateStaff(id: string, payload: StaffPayload): Promise<Staff> {
  const { data } = await api.put<Staff>(`/admin/staff/${id}`, payload)
  return data
}

export async function deleteStaff(id: string): Promise<void> {
  await api.delete(`/admin/staff/${id}`)
}

export async function getStaffCredentials(id: string): Promise<Credentials> {
  const { data } = await api.get<Credentials>(`/admin/staff/${id}/credentials`)
  return data
}

/** Xodimga yangi tasodifiy parol generatsiya qiladi — parol bir marta qaytadi. */
export async function resetStaffPassword(id: string): Promise<Credentials> {
  const { data } = await api.post<Credentials>(`/admin/staff/${id}/reset-password`)
  return data
}

/** Xodimga rol biriktirish (faqat superadmin); null — roldan chiqarish */
export async function setStaffRole(id: string, accessRoleId: string | null): Promise<Staff> {
  const { data } = await api.put<Staff>(`/admin/staff/${id}/role`, { accessRoleId })
  return data
}

/** Xodim bo'lim ruxsatlarini saqlash (faqat superadmin) */
export async function setStaffPermissions(id: string, permissions: string[]): Promise<Staff> {
  const { data } = await api.put<Staff>(`/admin/staff/${id}/permissions`, { permissions })
  return data
}

/* ------------------------------------------------------------------
   Xodim maoshi — o'qituvchinikining AYNAN ko'zgusi (`StaffSalaryController`).
   Javob shakllari bir xil: `teacherId` maydonida xodimning (users) id'si keladi.
   ------------------------------------------------------------------ */

/** Xodimga maosh berish — `salary` chiqimi `employee_user_id` bilan yoziladi. */
export async function payStaffSalary(id: string, input: SalaryPaymentInput): Promise<ExpenseRecord> {
  const { data } = await api.post<ExpenseRecord>(`/admin/staff/${id}/salary-payments`, input)
  return data
}

/** Xodimga berilgan maoshlar tarixi (jurnalga tushgan, storno qilinmagan). */
export async function getStaffSalaryHistory(id: string): Promise<SalaryHistory> {
  const { data } = await api.get<SalaryHistory>(`/admin/staff/${id}/salary-history`)
  return data
}

/** Xodim maoshi bo'yicha batafsil hisob (davr bo'yicha): oyma-oy taqsimot. */
export async function getStaffSalaryLedger(id: string, from?: string, to?: string): Promise<SalaryLedger> {
  const { data } = await api.get<SalaryLedger>(`/admin/staff/${id}/salary-ledger`, {
    params: { from, to },
  })
  return data
}
