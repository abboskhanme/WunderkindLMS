import { api } from '../client'

// "Abonement oyini butunlay o'chirish" (client, 2026-10-07) — even a paid month. Its receipts stay in
// the archive as storno and leave the accounts; the rest of a receipt is re-issued onto the months it
// paid. Admin and director only (two-person rule: not one's own receipt or accrual).
// 2026-10-26 (billing-plans.md §10.5): the paid share may instead go back to the pupil's balance, and the
// month may be billed again from the abonement — plus other abonements whose "Hisob" start skipped it.

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
  /** Blocks only the storno path (own receipt); the balance path reverses no receipt. */
  blockedReason: string | null
  /** What the abonement bills for the month if it is billed again; null = nothing covers it. */
  rebill: number | null
}

export interface MonthRemovalOther {
  subscriptionId: string
  categoryName: string
  planName: string | null
  monthlyAmount: number
  startsOn: string
  endsOn: string | null
  accrueFrom: string
  /** Months its "Hisob" start skipped and nobody billed — "yyyy-MM" with the amount. */
  months: { month: string; amount: number }[]
}

export interface MonthRemovalPreview {
  subscriptionId: string
  categoryName: string
  monthlyAmount: number
  startsOn: string
  endsOn: string | null
  months: MonthRemovalLine[]
  removedMonths: string[]
  others: MonthRemovalOther[] | null
}

/** Where the paid share of a removed month goes. */
export type MonthRemovalPaidTo = 'balance' | 'storno'

export interface MonthRemovalResult {
  months: number
  removedPayable: number
  reversedPaid: number
  paymentsReversed: number
  paymentsReissued: number
  subscriptionDeleted: boolean
  released: number
  rebilled: number
  alsoBilled: number
}

const base = (id: string) => `/admin/billing/subscriptions/${id}/remove-months`

export async function getMonthRemovalPreview(subscriptionId: string): Promise<MonthRemovalPreview> {
  const { data } = await api.get<MonthRemovalPreview>(base(subscriptionId))
  return data
}

export async function removeSubscriptionMonths(
  subscriptionId: string,
  body: {
    invoiceIds: string[]
    reason: string
    deleteSubscription: boolean
    paidTo: MonthRemovalPaidTo
    rebill: boolean
    alsoBill: string[]
  },
): Promise<MonthRemovalResult> {
  const { data } = await api.post<MonthRemovalResult>(base(subscriptionId), body)
  return data
}
