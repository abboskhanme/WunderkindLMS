import type {
  CategoryAmount,
  FinanceDirection,
  FinanceMonthly,
  FinanceSummary,
  FinanceTransaction,
  SalaryReportRow,
  StudentFinanceRow,
} from '@/types'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { financeMock } from '../mock/finance'
import { studentsMock } from '../mock/students'
import { classesMock } from '../mock/classes'
import { teachersMock } from '../mock/teachers'

/** "YYYY-MM-DD" oralig'idagi (inklyuziv) kalendar oylar soni */
function monthsInPeriod(from?: string, to?: string): number {
  if (!from || !to) return 1
  const [fy, fm] = from.slice(0, 7).split('-').map(Number)
  const [ty, tm] = to.slice(0, 7).split('-').map(Number)
  const m = (ty - fy) * 12 + (tm - fm) + 1
  return m < 1 ? 1 : m
}

export interface FinanceTransactionPayload {
  date: string
  direction: FinanceDirection
  category: string
  amount: number
  note?: string
  studentId?: string
  /** Chiqim "salary" bo'lsa — qaysi o'qituvchiga (oylik maosh) */
  teacherId?: string
  /** Kirim "tuition" (o'quvchi to'lovi) bo'lsa — qaysi oy uchun ("YYYY-MM") */
  month?: string
}

export interface TransactionFilters {
  from?: string
  to?: string
  direction?: FinanceDirection
  category?: string
}

/* ---------- Mock yordamchilari ---------- */

function inRange(t: FinanceTransaction, from?: string, to?: string): boolean {
  if (from && t.date < from) return false
  if (to && t.date > to) return false
  return true
}

function sumBy(items: FinanceTransaction[], cat: (t: FinanceTransaction) => boolean): number {
  return items.filter(cat).reduce((acc, t) => acc + t.amount, 0)
}

function byCategory(items: FinanceTransaction[]): CategoryAmount[] {
  const map = new Map<string, number>()
  items.forEach((t) => map.set(t.category, (map.get(t.category) ?? 0) + t.amount))
  return [...map.entries()]
    .map(([category, amount]) => ({ category, amount }))
    .sort((a, b) => b.amount - a.amount)
}

/* ---------- API ---------- */

export async function getTransactions(filters: TransactionFilters = {}): Promise<FinanceTransaction[]> {
  if (USE_MOCK) {
    await delay()
    return financeMock
      .filter(
        (t) =>
          inRange(t, filters.from, filters.to) &&
          (!filters.direction || t.direction === filters.direction) &&
          (!filters.category || t.category === filters.category),
      )
      .sort((a, b) => (a.date < b.date ? 1 : -1))
  }
  const { data } = await api.get<FinanceTransaction[]>('/admin/finance/transactions', {
    params: filters,
  })
  return data
}

export async function createTransaction(
  payload: FinanceTransactionPayload,
): Promise<FinanceTransaction> {
  if (USE_MOCK) {
    await delay(250)
    const studentName = payload.studentId
      ? studentsMock.find((s) => s.id === payload.studentId)?.fullName
      : undefined
    const tx: FinanceTransaction = { id: uid(), ...payload, studentName }
    financeMock.unshift(tx)
    return tx
  }
  const { data } = await api.post<FinanceTransaction>('/admin/finance/transactions', payload)
  return data
}

export async function updateTransaction(
  id: string,
  payload: FinanceTransactionPayload,
): Promise<FinanceTransaction> {
  if (USE_MOCK) {
    await delay(250)
    const i = financeMock.findIndex((t) => t.id === id)
    const studentName = payload.studentId
      ? studentsMock.find((s) => s.id === payload.studentId)?.fullName
      : undefined
    const tx: FinanceTransaction = { id, ...payload, studentName }
    if (i >= 0) financeMock[i] = tx
    return tx
  }
  const { data } = await api.put<FinanceTransaction>(`/admin/finance/transactions/${id}`, payload)
  return data
}

export async function deleteTransaction(id: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    const i = financeMock.findIndex((t) => t.id === id)
    if (i >= 0) financeMock.splice(i, 1)
    return
  }
  await api.delete(`/admin/finance/transactions/${id}`)
}

export async function getFinanceSummary(from?: string, to?: string): Promise<FinanceSummary> {
  if (USE_MOCK) {
    await delay()
    const items = financeMock.filter((t) => inRange(t, from, to))
    const income = items.filter((t) => t.direction === 'income')
    const expense = items.filter((t) => t.direction === 'expense')
    const totalIncome = income.reduce((a, t) => a + t.amount, 0)
    const totalExpense = expense.reduce((a, t) => a + t.amount, 0)
    const tuitionIncome = sumBy(income, (t) => t.category === 'tuition')
    const balances = studentsMock.map((s) => s.balance)
    return {
      totalIncome,
      totalExpense,
      net: totalIncome - totalExpense,
      tuitionIncome,
      otherIncome: totalIncome - tuitionIncome,
      incomeByCategory: byCategory(income),
      expenseByCategory: byCategory(expense),
      studentDebt: balances.filter((b) => b < 0).reduce((a, b) => a - b, 0),
      studentAdvance: balances.filter((b) => b > 0).reduce((a, b) => a + b, 0),
      transactionsCount: items.length,
    }
  }
  const { data } = await api.get<FinanceSummary>('/admin/finance/summary', { params: { from, to } })
  return data
}

/** O'qituvchilar maoshi hisoboti (davr bo'yicha): oylik, kerakli, berilgan, qoldiq */
export async function getSalaryReport(from?: string, to?: string): Promise<SalaryReportRow[]> {
  if (USE_MOCK) {
    await delay()
    const periodFrom = (from ?? `${new Date().getFullYear()}-01-01`).slice(0, 7)
    return teachersMock.map((t) => {
      // Oylik o'qituvchi boshlagan oydan hisoblanadi (avvalgi oylar uchun qarz yozilmaydi).
      const startMonth =
        t.salaryStartMonth && t.salaryStartMonth > periodFrom ? t.salaryStartMonth : periodFrom
      const months = monthsInPeriod(`${startMonth}-01`, to)
      const paid = financeMock.filter(
        (x) =>
          x.teacherId === t.id &&
          x.category === 'salary' &&
          x.date.slice(0, 7) >= startMonth &&
          inRange(x, undefined, to),
      )
      const totalPaid = paid.reduce((a, p) => a + p.amount, 0)
      const expected = t.salary * months
      return {
        teacherId: t.id,
        teacherName: t.fullName,
        salary: t.salary,
        totalPaid,
        paymentsCount: paid.length,
        months,
        expected,
        remaining: expected - totalPaid,
      }
    })
  }
  const { data } = await api.get<SalaryReportRow[]>('/admin/finance/salary-report', {
    params: { from, to },
  })
  return data
}

/** O'quvchilar bo'yicha moliya hisoboti (joriy holat): hisoblangan, to'langan, qoldiq, avans */
export async function getStudentReport(): Promise<StudentFinanceRow[]> {
  if (USE_MOCK) {
    await delay()
    return studentsMock
      .map((s) => {
        const fee = classesMock.find((c) => c.name === s.className)?.monthlyFee ?? 0
        const charged = fee * 5 // mock: 5 oy hisoblangan
        const debt = s.balance < 0 ? -s.balance : 0
        const advance = s.balance > 0 ? s.balance : 0
        const paid = Math.max(0, charged - debt)
        const monthDiscount = Math.max(
          0,
          Math.min(fee, (fee * s.discountPct) / 100 + s.discountAmount),
        )
        const discount = monthDiscount * 5
        return {
          studentId: s.id,
          fullName: s.fullName,
          className: s.className,
          charged,
          discount,
          paid,
          debt,
          advance,
          discountPct: s.discountPct,
          discountAmount: s.discountAmount,
        }
      })
      .sort((a, b) => b.debt - a.debt || a.fullName.localeCompare(b.fullName))
  }
  const { data } = await api.get<StudentFinanceRow[]>('/admin/finance/student-report')
  return data
}

export interface AccrueResult {
  months: string[]
  count: number
  total: number
}

/** Oylik to'lovni hisoblash. month berilmasa — hisoblanmagan barcha oylar. */
export async function accrueTuition(month?: string): Promise<AccrueResult> {
  if (USE_MOCK) {
    await delay(300)
    return { months: month ? [month] : [], count: 0, total: 0 }
  }
  const { data } = await api.post<AccrueResult>('/admin/finance/accrue', null, {
    params: month ? { month } : {},
  })
  return data
}

export async function getFinanceMonthly(year: number): Promise<FinanceMonthly[]> {
  if (USE_MOCK) {
    await delay()
    const result: FinanceMonthly[] = []
    for (let m = 1; m <= 12; m++) {
      const month = `${year}-${String(m).padStart(2, '0')}`
      const items = financeMock.filter((t) => t.date.startsWith(month))
      result.push({
        month,
        income: sumBy(items, (t) => t.direction === 'income'),
        expense: sumBy(items, (t) => t.direction === 'expense'),
      })
    }
    return result
  }
  const { data } = await api.get<FinanceMonthly[]>('/admin/finance/monthly', { params: { year } })
  return data
}
