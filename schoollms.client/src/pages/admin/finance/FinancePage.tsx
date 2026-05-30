import { useCallback, useEffect, useState } from 'react'
import { Plus, Pencil, Trash2, Download, TrendingUp, TrendingDown, Wallet, AlertCircle, Calculator, History } from 'lucide-react'
import type {
  FinanceDirection,
  FinanceMonthly,
  FinanceSummary,
  FinanceTransaction,
  SalaryReportRow,
  StudentFinanceRow,
} from '@/types'
import {
  getFinanceSummary,
  getFinanceMonthly,
  getTransactions,
  createTransaction,
  updateTransaction,
  deleteTransaction,
  accrueTuition,
  getSalaryReport,
  getStudentReport,
  type FinanceTransactionPayload,
} from '@/api/services/finance'
import { financeCategoryLabel, financeDirectionLabels } from '@/config/constants'
import { formatDate, formatMoney, exportToCsv, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { StatCard } from '@/components/ui/StatCard'
import { FinanceMonthlyChart } from '@/components/charts/FinanceMonthlyChart'
import { AuditHistoryModal } from '@/components/audit/AuditHistoryModal'
import type { AuditFilters } from '@/api/services/audit'
import { TransactionFormModal } from './TransactionFormModal'
import { TeacherSalaryDetailModal } from './TeacherSalaryDetailModal'

const todayStr = new Date().toISOString().slice(0, 10)
const yearOf = (d: string) => Number(d.slice(0, 4))

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

type DirFilter = 'all' | FinanceDirection
type Tab = 'overview' | 'teachers' | 'students'

const tabs: { value: Tab; label: string }[] = [
  { value: 'overview', label: 'Umumiy' },
  { value: 'teachers', label: "O'qituvchilar" },
  { value: 'students', label: "O'quvchilar" },
]

/** Qoldiq/qarz summasini belgisiga qarab ranglash */
function balanceClass(v: number): string {
  return v > 0 ? 'text-red-600' : v < 0 ? 'text-emerald-600' : 'text-slate-400'
}

/** Chegirma — foiz + summa qisqacha ko'rinishi (masalan "20% + 50 000" yoki "—"). */
function formatDiscount(pct: number, amount: number): string {
  if (pct <= 0 && amount <= 0) return '—'
  const parts: string[] = []
  if (pct > 0) parts.push(`${pct}%`)
  if (amount > 0) parts.push(formatMoney(amount))
  return parts.join(' + ')
}

export function FinancePage() {
  const [tab, setTab] = useState<Tab>('overview')
  const [from, setFrom] = useState(`${yearOf(todayStr)}-01-01`)
  const [to, setTo] = useState(todayStr)
  const [dirFilter, setDirFilter] = useState<DirFilter>('all')

  const [summary, setSummary] = useState<FinanceSummary | null>(null)
  const [monthly, setMonthly] = useState<FinanceMonthly[]>([])
  const [transactions, setTransactions] = useState<FinanceTransaction[]>([])
  const [salaryReport, setSalaryReport] = useState<SalaryReportRow[]>([])
  const [studentReport, setStudentReport] = useState<StudentFinanceRow[]>([])
  const [loading, setLoading] = useState(true)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<FinanceTransaction | null>(null)
  const [audit, setAudit] = useState<{ filters: AuditFilters; title: string } | null>(null)
  const [detailTeacher, setDetailTeacher] = useState<SalaryReportRow | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    Promise.all([
      getFinanceSummary(from, to),
      getFinanceMonthly(yearOf(to)),
      getTransactions({ from, to, direction: dirFilter === 'all' ? undefined : dirFilter }),
      getSalaryReport(from, to),
      getStudentReport(),
    ])
      .then(([s, m, t, sr, st]) => {
        setSummary(s)
        setMonthly(m)
        setTransactions(t)
        setSalaryReport(sr)
        setStudentReport(st)
      })
      .finally(() => setLoading(false))
  }, [from, to, dirFilter])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda ma'lumotni qayta yuklash (maqsadli, useAsync bilan bir xil naqsh)
  useEffect(() => load(), [load])

  const handleSubmit = async (values: FinanceTransactionPayload) => {
    if (editing) await updateTransaction(editing.id, values)
    else await createTransaction(values)
    setFormOpen(false)
    setEditing(null)
    load()
  }

  const handleDelete = async (t: FinanceTransaction) => {
    if (!confirm("Ushbu moliyaviy amalni o'chirishni tasdiqlaysizmi?")) return
    await deleteTransaction(t.id)
    load()
  }

  const handleAccrue = async () => {
    if (!confirm("Hisoblanmagan oylik to'lovlar barcha o'quvchilarga hisoblanadimi?")) return
    const res = await accrueTuition()
    alert(
      res.count > 0
        ? `${res.months.join(', ')} uchun ${res.count} ta o'quvchiga jami ${formatMoney(res.total)} hisoblandi.`
        : "Yangi hisoblanadigan oy yo'q — hammasi hisoblangan.",
    )
    load()
  }

  const handleExport = () => {
    exportToCsv(
      'moliya.csv',
      ['Sana', "Yo'nalish", 'Toifa', 'Izoh', 'Summa'],
      transactions.map((t) => [
        formatDate(t.date),
        financeDirectionLabels[t.direction],
        financeCategoryLabel(t.category),
        t.note ?? '',
        String(t.amount),
      ]),
    )
  }

  const handleExportTeachers = () => {
    exportToCsv(
      'oqituvchilar-maoshi.csv',
      ["O'qituvchi", 'Oylik', 'Hisoblangan', 'Berilgan', 'Qoldiq'],
      salaryReport.map((r) => [
        r.teacherName,
        String(r.salary),
        String(r.expected),
        String(r.totalPaid),
        String(r.remaining),
      ]),
    )
  }

  const handleExportStudents = () => {
    exportToCsv(
      'oquvchilar-tolov.csv',
      ["O'quvchi", 'Sinf', 'Hisoblangan', 'Chegirma', "To'langan", 'Qarz', 'Avans'],
      studentReport.map((r) => [
        r.fullName,
        r.className,
        String(r.charged),
        String(r.discount),
        String(r.paid),
        String(r.debt),
        String(r.advance),
      ]),
    )
  }

  // Tanlangan davrning kalendar oylari (har o'qituvchining hisoblangan oyi boshlanish oyiga
  // qarab farq qilishi mumkin — bu faqat davr uzunligini ko'rsatadi).
  const periodMonths = (() => {
    const [fy, fm] = from.slice(0, 7).split('-').map(Number)
    const [ty, tm] = to.slice(0, 7).split('-').map(Number)
    return Math.max(1, (ty - fy) * 12 + (tm - fm) + 1)
  })()

  const teacherTotals = {
    expected: salaryReport.reduce((a, r) => a + r.expected, 0),
    paid: salaryReport.reduce((a, r) => a + r.totalPaid, 0),
    remaining: salaryReport.reduce((a, r) => a + Math.max(0, r.remaining), 0),
  }
  const studentTotals = {
    charged: studentReport.reduce((a, r) => a + r.charged, 0),
    paid: studentReport.reduce((a, r) => a + r.paid, 0),
    debt: studentReport.reduce((a, r) => a + r.debt, 0),
    advance: studentReport.reduce((a, r) => a + r.advance, 0),
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Moliya</h1>
          <p className="text-sm text-slate-400">Maktab kirim-chiqimlari va hisobotlar</p>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant="secondary"
            onClick={() => setAudit({ filters: {}, title: "Moliya o'zgarishlar tarixi" })}
          >
            <History className="h-4 w-4" /> Tarix
          </Button>
          <Button variant="secondary" onClick={handleAccrue}>
            <Calculator className="h-4 w-4" /> Oylik to'lovni hisoblash
          </Button>
          <Button
            onClick={() => {
              setEditing(null)
              setFormOpen(true)
            }}
          >
            <Plus className="h-4 w-4" /> Yangi amal
          </Button>
        </div>
      </div>

      {/* Bo'limlar (tablar) */}
      <div className="flex flex-wrap items-center gap-2">
        {tabs.map((t) => (
          <button
            key={t.value}
            onClick={() => setTab(t.value)}
            className={cn(
              'rounded-lg px-4 py-2 text-sm font-medium transition-colors',
              tab === t.value
                ? 'bg-brand-600 text-white'
                : 'bg-white text-slate-600 hover:bg-slate-100',
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

      {/* Davr tanlash (umumiy va o'qituvchilar bo'limi uchun) */}
      {tab !== 'students' && (
        <Card className="flex flex-wrap items-center gap-3 p-4">
          <span className="text-sm font-medium text-slate-600">Davr:</span>
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className={control} />
          <span className="text-slate-400">—</span>
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} className={control} />
        </Card>
      )}

      {loading || !summary ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          {/* ============ UMUMIY ============ */}
          {tab === 'overview' && (
            <>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
                <StatCard
                  label="Umumiy kirim"
                  value={formatMoney(summary.totalIncome)}
                  icon={TrendingUp}
                  iconBg="bg-emerald-50"
                  iconColor="text-emerald-600"
                  hint={`O'quvchi to'lovi: ${formatMoney(summary.tuitionIncome)}`}
                />
                <StatCard
                  label="Umumiy chiqim"
                  value={formatMoney(summary.totalExpense)}
                  icon={TrendingDown}
                  iconBg="bg-red-50"
                  iconColor="text-red-600"
                />
                <StatCard
                  label="Sof balans"
                  value={formatMoney(summary.net)}
                  icon={Wallet}
                  iconBg={summary.net >= 0 ? 'bg-emerald-50' : 'bg-red-50'}
                  iconColor={summary.net >= 0 ? 'text-emerald-600' : 'text-red-600'}
                  hint="Kirim − Chiqim"
                />
                <StatCard
                  label="O'quvchilar qarzi"
                  value={formatMoney(summary.studentDebt)}
                  icon={AlertCircle}
                  iconBg="bg-amber-50"
                  iconColor="text-amber-600"
                  hint={`Avans: ${formatMoney(summary.studentAdvance)}`}
                />
              </div>

              <div className="grid grid-cols-1 gap-6 xl:grid-cols-3">
                <Card className="xl:col-span-2">
                  <h2 className="mb-4 font-semibold text-slate-800">
                    Oylik kirim/chiqim ({yearOf(to)})
                  </h2>
                  <FinanceMonthlyChart data={monthly} />
                </Card>

                <Card>
                  <h2 className="mb-3 font-semibold text-slate-800">Toifalar bo'yicha</h2>
                  <CategoryList title="Kirim" items={summary.incomeByCategory} positive />
                  <div className="my-3 border-t border-slate-100" />
                  <CategoryList title="Chiqim" items={summary.expenseByCategory} positive={false} />
                </Card>
              </div>

              {/* Amallar jadvali */}
              <Card className="p-0">
                <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 p-4">
                  <div className="flex items-center gap-2">
                    {(['all', 'income', 'expense'] as DirFilter[]).map((d) => (
                      <button
                        key={d}
                        onClick={() => setDirFilter(d)}
                        className={cn(
                          'rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
                          dirFilter === d
                            ? 'bg-brand-50 text-brand-700'
                            : 'text-slate-500 hover:bg-slate-100',
                        )}
                      >
                        {d === 'all' ? 'Barchasi' : financeDirectionLabels[d]}
                      </button>
                    ))}
                  </div>
                  <Button variant="secondary" onClick={handleExport} disabled={transactions.length === 0}>
                    <Download className="h-4 w-4" /> CSV
                  </Button>
                </div>

                <div className="overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                      <tr>
                        <th className="px-4 py-3">Sana</th>
                        <th className="px-4 py-3">Yo'nalish</th>
                        <th className="px-4 py-3">Toifa</th>
                        <th className="px-4 py-3">Izoh</th>
                        <th className="px-4 py-3 text-right">Summa</th>
                        <th className="px-4 py-3 text-right">Amallar</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {transactions.map((t) => (
                        <tr key={t.id} className="hover:bg-slate-50/60">
                          <td className="px-4 py-3 text-slate-600">{formatDate(t.date)}</td>
                          <td className="px-4 py-3">
                            <span
                              className={cn(
                                'rounded-md px-2 py-0.5 text-xs font-medium',
                                t.direction === 'income'
                                  ? 'bg-emerald-50 text-emerald-700'
                                  : 'bg-red-50 text-red-700',
                              )}
                            >
                              {financeDirectionLabels[t.direction]}
                            </span>
                          </td>
                          <td className="px-4 py-3 text-slate-600">{financeCategoryLabel(t.category)}</td>
                          <td className="px-4 py-3 text-slate-500">{t.note ?? '—'}</td>
                          <td
                            className={cn(
                              'px-4 py-3 text-right font-medium',
                              t.direction === 'income' ? 'text-emerald-600' : 'text-red-600',
                            )}
                          >
                            {t.direction === 'income' ? '+' : '−'}
                            {formatMoney(t.amount)}
                          </td>
                          <td className="px-4 py-3">
                            <div className="flex items-center justify-end gap-0.5">
                              <button
                                type="button"
                                title="O'zgarishlar tarixi"
                                onClick={() =>
                                  setAudit({
                                    filters: { entityType: 'FinanceTransaction', entityId: t.id },
                                    title: 'Amal tarixi',
                                  })
                                }
                                className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                              >
                                <History className="h-4 w-4" />
                              </button>
                              <button
                                type="button"
                                title="Tahrirlash"
                                onClick={() => {
                                  setEditing(t)
                                  setFormOpen(true)
                                }}
                                className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                              >
                                <Pencil className="h-4 w-4" />
                              </button>
                              <button
                                type="button"
                                title="O'chirish"
                                onClick={() => handleDelete(t)}
                                className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                              >
                                <Trash2 className="h-4 w-4" />
                              </button>
                            </div>
                          </td>
                        </tr>
                      ))}
                      {transactions.length === 0 && (
                        <tr>
                          <td colSpan={6} className="px-4 py-12 text-center text-slate-400">
                            Bu davrda amallar yo'q
                          </td>
                        </tr>
                      )}
                    </tbody>
                  </table>
                </div>
              </Card>
            </>
          )}

          {/* ============ O'QITUVCHILAR ============ */}
          {tab === 'teachers' && (
            <>
              {salaryReport.length > 0 && (
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                  <SummaryCard label="Jami hisoblangan" value={formatMoney(teacherTotals.expected)} />
                  <SummaryCard
                    label="Jami berilgan"
                    value={formatMoney(teacherTotals.paid)}
                    valueClass="text-emerald-600"
                  />
                  <SummaryCard
                    label="Jami qoldiq"
                    value={formatMoney(teacherTotals.remaining)}
                    valueClass="text-red-600"
                  />
                </div>
              )}
              <Card className="p-0">
                <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 p-4">
                  <div>
                    <h2 className="font-semibold text-slate-800">O'qituvchilar maoshi</h2>
                    <p className="text-sm text-slate-400">
                      Davr bo'yicha — {periodMonths} oy · batafsil uchun o'qituvchini bosing
                    </p>
                  </div>
                  <Button variant="secondary" onClick={handleExportTeachers} disabled={salaryReport.length === 0}>
                    <Download className="h-4 w-4" /> CSV
                  </Button>
                </div>
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                      <tr>
                        <th className="px-4 py-3">O'qituvchi</th>
                        <th className="px-4 py-3 text-right">Oylik</th>
                        <th className="px-4 py-3 text-right">Hisoblangan</th>
                        <th className="px-4 py-3 text-right">Berilgan</th>
                        <th className="px-4 py-3 text-right">Qoldiq</th>
                        <th className="px-4 py-3 text-right">Tarix</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {salaryReport.map((r) => (
                      <tr
                        key={r.teacherId}
                        onClick={() => setDetailTeacher(r)}
                        className="cursor-pointer hover:bg-slate-50/60"
                      >
                        <td className="px-4 py-3 font-medium text-brand-700">{r.teacherName}</td>
                        <td className="px-4 py-3 text-right text-slate-600">{formatMoney(r.salary)}</td>
                        <td className="px-4 py-3 text-right text-slate-600">{formatMoney(r.expected)}</td>
                        <td className="px-4 py-3 text-right font-medium text-emerald-600">
                          {formatMoney(r.totalPaid)}
                        </td>
                        <td className={cn('px-4 py-3 text-right font-medium', balanceClass(r.remaining))}>
                          {r.remaining < 0 ? `+${formatMoney(-r.remaining)}` : formatMoney(r.remaining)}
                        </td>
                        <td className="px-4 py-3 text-right">
                          <button
                            type="button"
                            title="O'zgarishlar tarixi"
                            onClick={(e) => {
                              e.stopPropagation()
                              setAudit({ filters: { teacherId: r.teacherId }, title: `Tarix — ${r.teacherName}` })
                            }}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                          >
                            <History className="h-4 w-4" />
                          </button>
                        </td>
                      </tr>
                    ))}
                      {salaryReport.length === 0 && (
                        <tr>
                          <td colSpan={6} className="px-4 py-10 text-center text-slate-400">
                            Ma'lumot yo'q
                          </td>
                        </tr>
                      )}
                    </tbody>
                  </table>
                </div>
              </Card>
            </>
          )}

          {/* ============ O'QUVCHILAR ============ */}
          {tab === 'students' && (
            <>
              {studentReport.length > 0 && (
                <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
                  <SummaryCard label="Jami hisoblangan" value={formatMoney(studentTotals.charged)} />
                  <SummaryCard
                    label="Jami to'langan"
                    value={formatMoney(studentTotals.paid)}
                    valueClass="text-emerald-600"
                  />
                  <SummaryCard
                    label="Jami qarz"
                    value={formatMoney(studentTotals.debt)}
                    valueClass="text-red-600"
                  />
                  <SummaryCard
                    label="Jami avans"
                    value={formatMoney(studentTotals.advance)}
                    valueClass="text-emerald-600"
                  />
                </div>
              )}
              <Card className="p-0">
                <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 p-4">
                  <div>
                    <h2 className="font-semibold text-slate-800">O'quvchilar to'lovi</h2>
                    <p className="text-sm text-slate-400">Joriy holat — eng katta qarzdorlar yuqorida</p>
                  </div>
                  <Button variant="secondary" onClick={handleExportStudents} disabled={studentReport.length === 0}>
                    <Download className="h-4 w-4" /> CSV
                  </Button>
                </div>
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                      <tr>
                        <th className="px-4 py-3">O'quvchi</th>
                        <th className="px-4 py-3">Sinf</th>
                        <th className="px-4 py-3 text-right">Hisoblangan</th>
                        <th className="px-4 py-3 text-right">Chegirma</th>
                        <th className="px-4 py-3 text-right">To'langan</th>
                        <th className="px-4 py-3 text-right">Qarz</th>
                        <th className="px-4 py-3 text-right">Avans</th>
                        <th className="px-4 py-3 text-right">Tarix</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {studentReport.map((r) => (
                        <tr key={r.studentId} className="hover:bg-slate-50/60">
                        <td className="px-4 py-3 font-medium text-slate-800">{r.fullName}</td>
                        <td className="px-4 py-3">
                          <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                            {r.className}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-right text-slate-600">{formatMoney(r.charged)}</td>
                        <td className="px-4 py-3 text-right">
                          {r.discount > 0 ? (
                            <div>
                              <div className="font-medium text-amber-700">−{formatMoney(r.discount)}</div>
                              {(r.discountPct > 0 || r.discountAmount > 0) && (
                                <div className="text-xs text-amber-600/70">
                                  {formatDiscount(r.discountPct, r.discountAmount)}
                                </div>
                              )}
                            </div>
                          ) : (
                            <span className="text-slate-300">—</span>
                          )}
                        </td>
                        <td className="px-4 py-3 text-right font-medium text-emerald-600">
                          {formatMoney(r.paid)}
                        </td>
                        <td className={cn('px-4 py-3 text-right font-medium', r.debt > 0 ? 'text-red-600' : 'text-slate-400')}>
                          {formatMoney(r.debt)}
                        </td>
                        <td className={cn('px-4 py-3 text-right', r.advance > 0 ? 'text-emerald-600' : 'text-slate-400')}>
                          {r.advance > 0 ? `+${formatMoney(r.advance)}` : formatMoney(0)}
                        </td>
                        <td className="px-4 py-3 text-right">
                          <button
                            type="button"
                            title="O'zgarishlar tarixi"
                            onClick={() =>
                              setAudit({ filters: { studentId: r.studentId }, title: `Tarix — ${r.fullName}` })
                            }
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                          >
                            <History className="h-4 w-4" />
                          </button>
                        </td>
                      </tr>
                    ))}
                      {studentReport.length === 0 && (
                        <tr>
                          <td colSpan={8} className="px-4 py-10 text-center text-slate-400">
                            Ma'lumot yo'q
                          </td>
                        </tr>
                      )}
                    </tbody>
                  </table>
                </div>
              </Card>
            </>
          )}
        </>
      )}

      <TransactionFormModal
        open={formOpen}
        onClose={() => {
          setFormOpen(false)
          setEditing(null)
        }}
        onSubmit={handleSubmit}
        initial={editing}
      />

      <AuditHistoryModal
        open={!!audit}
        onClose={() => setAudit(null)}
        title={audit?.title}
        filters={audit?.filters ?? {}}
      />

      <TeacherSalaryDetailModal
        teacher={detailTeacher}
        from={from}
        to={to}
        onClose={() => setDetailTeacher(null)}
      />
    </div>
  )
}

function SummaryCard({
  label,
  value,
  valueClass = 'text-slate-800',
}: {
  label: string
  value: string
  valueClass?: string
}) {
  return (
    <Card className="p-4">
      <p className="text-xs font-medium uppercase tracking-wide text-slate-400">{label}</p>
      <p className={cn('mt-1 text-lg font-semibold', valueClass)}>{value}</p>
    </Card>
  )
}

function CategoryList({
  title,
  items,
  positive,
}: {
  title: string
  items: { category: string; amount: number }[]
  positive: boolean
}) {
  const total = items.reduce((a, c) => a + c.amount, 0)
  return (
    <div>
      <p className="mb-2 text-xs font-medium uppercase tracking-wide text-slate-400">{title}</p>
      {items.length === 0 ? (
        <p className="text-sm text-slate-400">Ma'lumot yo'q</p>
      ) : (
        <ul className="space-y-2">
          {items.map((c) => (
            <li key={c.category} className="flex items-center justify-between gap-2 text-sm">
              <span className="text-slate-600">{financeCategoryLabel(c.category)}</span>
              <span className={cn('font-medium', positive ? 'text-emerald-600' : 'text-red-600')}>
                {formatMoney(c.amount)}
              </span>
            </li>
          ))}
          <li className="flex items-center justify-between gap-2 border-t border-slate-100 pt-2 text-sm font-semibold">
            <span className="text-slate-700">Jami</span>
            <span className={positive ? 'text-emerald-700' : 'text-red-700'}>{formatMoney(total)}</span>
          </li>
        </ul>
      )}
    </div>
  )
}
