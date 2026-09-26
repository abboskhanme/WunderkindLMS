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

/**
 * Xodimlar maoshi hisoboti (davr bo'yicha): oylik, kerakli, berilgan, qoldiq.
 * Avval o'qituvchilar, keyin boshqa xodimlar (`kind === 'staff'`) keladi.
 */
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
        kind: 'teacher' as const,
        position: "O'qituvchi",
      }
    })
  }
  const { data } = await api.get<SalaryReportRow[]>('/admin/finance/salary-report', {
    params: { from, to },
  })
  return data
}

/**
 * Maosh hisoboti — .xlsx (mijoz, 2026-09-19: "yuklab olish csv emas excel
 * fayl uchun bo'lsin"). Fayl serverda yig'iladi, ya'ni ekrandagi raqamning
 * o'zi tushadi va yakun qatori ham bor.
 */
export async function downloadSalaryReport(from: string, to: string): Promise<void> {
  const res = await api.get('/admin/finance/salary-report/export', {
    params: { from, to },
    responseType: 'blob',
  })

  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^"]+)"?/)
  a.download = m?.[1] ?? 'xodimlar-maoshi.xlsx'
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
