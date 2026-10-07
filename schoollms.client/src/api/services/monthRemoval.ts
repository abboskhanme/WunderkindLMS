import { api } from '../client'

// "Abonement oyini butunlay o'chirish" (client, 2026-10-07) — even a paid month. Its receipts stay in
// the archive as storno and leave the accounts; the rest of a receipt is re-issued onto the months it
// paid. Admin and director only (two-person rule: not one's own receipt or accrual).

export interface MonthRemovalPayment {
  paymentId: string
  receiptNo: number
  receivedAt: string
  amount: number
  share: number
  own: boolean
}

export interface MonthRemovalLine {
  invoiceId: string
  month: string
  amount: number
  discount: number
  payable: number
  paid: number
  status: string
  payments: MonthRemovalPayment[]
  blockedReason: string | null
}

export interface MonthRemovalPreview {
  subscriptionId: string
  categoryName: string
  monthlyAmount: number
  startsOn: string
  endsOn: string | null
  months: MonthRemovalLine[]
  removedMonths: string[]
}

export interface MonthRemovalResult {
  months: number
  removedPayable: number
  reversedPaid: number
  paymentsReversed: number
  paymentsReissued: number
  subscriptionDeleted: boolean
}

const base = (id: string) => `/admin/billing/subscriptions/${id}/remove-months`

export async function getMonthRemovalPreview(subscriptionId: string): Promise<MonthRemovalPreview> {
  const { data } = await api.get<MonthRemovalPreview>(base(subscriptionId))
  return data
}

export async function removeSubscriptionMonths(
  subscriptionId: string,
  body: { invoiceIds: string[]; reason: string; deleteSubscription: boolean },
): Promise<MonthRemovalResult> {
  const { data } = await api.post<MonthRemovalResult>(base(subscriptionId), body)
  return data
}
