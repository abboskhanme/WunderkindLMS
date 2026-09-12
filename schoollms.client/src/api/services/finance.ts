/**
 * Moliya — eski yassi kassa kitobidan QOLGAN yagona chaqiruv: maosh hisoboti.
 *
 * P1-21 da olib tashlandi (backend endpoint'lari bilan birga):
 *   getTransactions / createTransaction / updateTransaction / deleteTransaction
 *     -> pul endi o'zgarmas: to'lov kassada (`/api/cash/payments`),
 *        chiqim `/api/admin/expenses`, tuzatish faqat storno (SPEC §4.1);
 *   accrueTuition   -> `/api/admin/billing/accrual/run` (toifalar kesimida);
 *   getFinanceSummary -> `/api/admin/finance/pnl`;
 *   getFinanceMonthly -> `/api/admin/finance/cashflow`;
 *   getStudentReport  -> `/api/admin/finance/debtors`.
 *
 * Maosh hisobotining o'rnini bosadigan yangi ekran yo'q — u qoldi, faqat
 * manbasi almashdi: server uni endi `expenses` dan (jurnalga tushgan
 * `salary` chiqimlaridan) hisoblaydi.
 */
import type { SalaryReportRow } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { teachersMock } from '../mock/teachers'

/** "YYYY-MM-DD" oralig'idagi (inklyuziv) kalendar oylar soni */
function monthsInPeriod(from?: string, to?: string): number {
  if (!from || !to) return 1
  const [fy, fm] = from.slice(0, 7).split('-').map(Number)
  const [ty, tm] = to.slice(0, 7).split('-').map(Number)
  const m = (ty - fy) * 12 + (tm - fm) + 1
  return m < 1 ? 1 : m
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
      const expected = t.salary * months
      return {
        teacherId: t.id,
        teacherName: t.fullName,
        salary: t.salary,
        totalPaid: 0,
        paymentsCount: 0,
        months,
        expected,
        remaining: expected,
      }
    })
  }
  const { data } = await api.get<SalaryReportRow[]>('/admin/finance/salary-report', {
    params: { from, to },
  })
  return data
}
