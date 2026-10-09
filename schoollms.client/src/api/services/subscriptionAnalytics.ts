/**
 * Abonementlar analitikasi — sinflar × abonement turlari va sinf ichida kim qaysi abonementda
 * (mijoz, 2026-10-09).
 *
 * Backend: `GET /api/admin/finance/subscription-analytics?from&to` va
 * `GET /api/admin/finance/subscription-analytics/class/{classId}?from&to`
 * (`SchoolLms.Application/Billing/SubscriptionAnalyticsQuery.cs` — davr qoidalari o'sha yerda).
 * Hamma summa serverdan tayyor keladi; ekran pulni qayta hisoblamaydi.
 */
import { api } from '../client'
import { billingErrorMessage } from './billingError'

/** Abonement ko'rsatilmagan hisob-fakturalar ustuni (`Guid.Empty`). */
export const NO_PLAN = '00000000-0000-0000-0000-000000000000'
/** Sinfi yo'q o'quvchilar qatori. */
export const NO_CLASS = 'none'
/** Maktabdan ketgan, lekin davrda abonementi yoki fakturasi bor o'quvchilar qatori. */
export const ARCHIVED = 'archived'

export interface SubscriptionPlanColumn {
  planId: string
  name: string
  categoryCode: string
  categoryName: string
  monthlyAmount: number
  isActive: boolean
}

export interface SubscriptionPlanCell {
  planId: string
  /** Davrda shu abonementda bo'lgan o'quvchilar. */
  students: number
  /** Davr oylariga hisoblangan (summa − chegirma). */
  billed: number
  paid: number
}

export interface SubscriptionClassRow {
  classId: string
  className: string
  grade: number
  students: number
  /** Davrda o'qish abonementi bo'lmagan o'quvchilar. */
  withoutTuition: number
  plans: SubscriptionPlanCell[]
  billed: number
  paid: number
  debt: number
}

export interface SubscriptionPlanTotal {
  planId: string
  students: number
  billed: number
  paid: number
  debt: number
}

export interface SubscriptionAnalytics {
  from: string
  to: string
  plans: SubscriptionPlanColumn[]
  planTotals: SubscriptionPlanTotal[]
  classes: SubscriptionClassRow[]
  /** Maktabdagi (arxivlanmagan) o'quvchilar. */
  students: number
  withSubscription: number
  withoutTuition: number
  billed: number
  paid: number
  debt: number
}

export interface SubscriptionStudentPlan {
  subscriptionId: string
  planId: string
  planName: string
  categoryCode: string
  categoryName: string
  monthlyAmount: number
  startsOn: string
  endsOn: string | null
  detail: string | null
  billed: number
  paid: number
}

export interface SubscriptionStudentRow {
  studentId: string
  fullName: string
  subscriptions: SubscriptionStudentPlan[]
  billed: number
  paid: number
  debt: number
}

export interface SubscriptionClassDetail {
  from: string
  to: string
  classId: string
  className: string
  plans: SubscriptionPlanColumn[]
  students: SubscriptionStudentRow[]
  billed: number
  paid: number
  debt: number
}

export async function getSubscriptionAnalytics(from: string, to: string): Promise<SubscriptionAnalytics> {
  try {
    const { data } = await api.get<SubscriptionAnalytics>('/admin/finance/subscription-analytics', {
      params: { from, to },
    })
    return data
  } catch (e) {
    throw new Error(billingErrorMessage(e, "Abonementlar analitikasini yuklab bo'lmadi"), { cause: e })
  }
}

export async function getSubscriptionClass(
  classId: string,
  from: string,
  to: string,
): Promise<SubscriptionClassDetail> {
  try {
    const { data } = await api.get<SubscriptionClassDetail>(
      `/admin/finance/subscription-analytics/class/${encodeURIComponent(classId)}`,
      { params: { from, to } },
    )
    return data
  } catch (e) {
    throw new Error(billingErrorMessage(e, "Sinf abonementlarini yuklab bo'lmadi"), { cause: e })
  }
}
