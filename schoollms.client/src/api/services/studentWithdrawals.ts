import { api } from '../client'

/**
 * "Chiqib ketdi — abonementni bekor qilish" (`StudentWithdrawalController`). O'qilgan dars kunlari qoladi,
 * qolgani va keyingi oylar hisobdan chiqadi, ortiqcha to'lov avansga qaytadi. Kassaga tegmaydi.
 */

export interface WithdrawalRequest {
  /** "YYYY-MM-DD" — oxirgi o'qigan kun (shu kun o'qilgan hisoblanadi). */
  lastDay: string
  /** Bekor qilinadigan abonementlar; berilmasa — shu kundan keyin davom etadiganlarning hammasi. */
  subscriptionIds?: string[]
  note?: string
}

export interface WithdrawalSubscription {
  id: string
  categoryName: string
  planName: string | null
  monthlyAmount: number
  startsOn: string
  endsOn: string | null
  selected: boolean
}

export interface WithdrawalLine {
  invoiceId: string
  /** "YYYY-MM" */
  month: string
  categoryName: string
  payable: number
  paid: number
  /** Faqat ketgan oyda: oyning dars kunlari va o'qilgani. */
  lessonDays: number
  studiedDays: number
  keep: number
  writeOff: number
  released: number
  /** Ketgan oyning kasallik chegirmasi — qoladigan summadan to'liq ayiriladi. */
  sickDeduction: number
}

export interface WithdrawalPreview {
  lastDay: string
  subscriptions: WithdrawalSubscription[]
  lines: WithdrawalLine[]
  totalWriteOff: number
  totalReleased: number
  balanceBefore: number
  balanceAfter: number
  withdrawalId: string | null
}

export interface WithdrawalEntry {
  id: string
  lastDay: string
  writtenOff: number
  released: number
  note: string | null
  createdAt: string
  createdByName: string | null
  cancelledAt: string | null
  canCancel: boolean
}

const base = (studentId: string) => `/admin/billing/students/${studentId}/withdrawals`

export async function listWithdrawals(studentId: string): Promise<WithdrawalEntry[]> {
  const { data } = await api.get<WithdrawalEntry[]>(base(studentId))
  return data
}

export async function previewWithdrawal(studentId: string, body: WithdrawalRequest, signal?: AbortSignal): Promise<WithdrawalPreview> {
  const { data } = await api.post<WithdrawalPreview>(`${base(studentId)}/preview`, body, { signal })
  return data
}

export async function applyWithdrawal(studentId: string, body: WithdrawalRequest): Promise<WithdrawalPreview> {
  const { data } = await api.post<WithdrawalPreview>(base(studentId), body)
  return data
}

export async function cancelWithdrawal(studentId: string, withdrawalId: string): Promise<WithdrawalEntry> {
  const { data } = await api.post<WithdrawalEntry>(`${base(studentId)}/${withdrawalId}/cancel`)
  return data
}
