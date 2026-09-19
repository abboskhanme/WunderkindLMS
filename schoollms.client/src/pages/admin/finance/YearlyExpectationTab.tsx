/**
 * P&L 2.0 — F6.05 yillik reja/fakt jadvali, eng yaxshi/yomon oy bilan.
 *
 * Manba: `GET /api/admin/finance/pnl/expectation/yearly` — har oy AYNAN
 * `RevenueExpectationAsync` ning o'zi (ikkinchi arifmetika yo'q, to'liq
 * qoida `FinanceReportQueries.RevenueExpectationYearly.cs` da).
 */
import { useState } from 'react'
import { Award, Download, TrendingDown, Wallet } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import { getYearlyExpectation } from '@/api/services/financeReports'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { StatCard } from '@/components/ui/StatCard'
import { cn, exportToCsv, formatMoney } from '@/lib/utils'
import { formatMonthLabel, formatSignedMoney, pctText, signClass } from './reportLabels'
import { ReportState } from './ReportState'

function currentYear(): number {
  return new Date().getFullYear()
}

export function YearlyExpectationTab() {
  const [year, setYear] = useState(currentYear)
  const { data, loading, error, refetch } = useAsync(() => getYearlyExpectation(year), [year])

  const handleExport = () => {
    if (!data) return
    exportToCsv(
      `pnl-2.0-yillik_${year}.csv`,
      ['Oy', 'Reja yalpi', 'Reja chegirma', 'Reja sof', 'Fakt daromad', 'Fakt chiqim', 'Fakt natija', 'Marja %'],
      data.months.map((m) => [
        formatMonthLabel(m.month),
        String(m.grossPlan),
        String(m.discountAmount),
        String(m.netPlan),
        String(m.revenueActual),
        String(m.expenseActual),
        String(m.profitActual),
        m.margin === null ? '' : String(m.margin),
      ]),
    )
  }

  return (
    <div className="space-y-5">
      <Card className="flex flex-wrap items-center gap-3 p-4">
        <span className="text-sm font-medium text-slate-600">Yil:</span>
        <input
          type="number"
          value={year}
          min={2000}
          max={2100}
          onChange={(e) => setYear(Number(e.target.value) || currentYear())}
          aria-label="Yil"
          className="w-28 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
        />
      </Card>

      <ReportState
        loading={loading}
        error={error}
        isEmpty={!!data && data.months.every((m) => m.revenueActual === 0 && m.expenseActual === 0 && m.netPlan === 0)}
        emptyTitle="Bu yilda moliyaviy harakat yo'q"
        emptyHint="Boshqa yilni tanlang."
        onRetry={refetch}
      >
        {data && (
          <div className="space-y-5">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <StatCard
                label="Yillik sof natija (fakt)"
                value={formatSignedMoney(data.profitActualTotal)}
                icon={Wallet}
                iconBg={data.profitActualTotal >= 0 ? 'bg-emerald-50' : 'bg-red-50'}
                iconColor={data.profitActualTotal >= 0 ? 'text-emerald-600' : 'text-red-600'}
              />
              <StatCard
                label="Yillik fakt daromad"
                value={formatMoney(data.revenueActualTotal)}
                icon={TrendingDown}
                iconBg="bg-emerald-50"
                iconColor="text-emerald-600"
              />
              <StatCard
                label="Eng yaxshi oy"
                value={data.bestMonth ? formatMonthLabel(data.bestMonth) : '—'}
                icon={Award}
                iconBg="bg-emerald-50"
                iconColor="text-emerald-600"
              />
              <StatCard
                label="Eng yomon oy"
                value={data.worstMonth ? formatMonthLabel(data.worstMonth) : '—'}
                icon={Award}
                iconBg="bg-red-50"
                iconColor="text-red-600"
              />
            </div>

            <div className="flex justify-end">
              <Button variant="secondary" onClick={handleExport}>
                <Download className="h-4 w-4" /> CSV
              </Button>
            </div>

            <Card className="p-0">
              <div className="overflow-x-auto">
                <table className="w-full min-w-[52rem] text-left text-sm">
                  <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-4 py-3">Oy</th>
                      <th className="px-4 py-3 text-right">Reja (sof)</th>
                      <th className="px-4 py-3 text-right">Fakt daromad</th>
                      <th className="px-4 py-3 text-right">Fakt chiqim</th>
                      <th className="px-4 py-3 text-right">Fakt natija</th>
                      <th className="px-4 py-3 text-right">Marja</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {data.months.map((m) => {
                      const isBest = data.bestMonth === m.month
                      const isWorst = data.worstMonth === m.month
                      return (
                        <tr
                          key={m.month}
                          className={cn(
                            'hover:bg-slate-50/60',
                            isBest && 'bg-emerald-50/50',
                            isWorst && !isBest && 'bg-red-50/40',
                          )}
                        >
                          <td className="px-4 py-2.5 font-medium text-slate-700">
                            {formatMonthLabel(m.month)}
                            {isBest && (
                              <span className="ml-2 rounded-full bg-emerald-100 px-2 py-0.5 text-[10px] font-semibold text-emerald-700">
                                ENG YAXSHI
                              </span>
                            )}
                            {isWorst && !isBest && (
                              <span className="ml-2 rounded-full bg-red-100 px-2 py-0.5 text-[10px] font-semibold text-red-700">
                                ENG YOMON
                              </span>
                            )}
                          </td>
                          <td className="px-4 py-2.5 text-right text-slate-600">{formatMoney(m.netPlan)}</td>
                          <td className="px-4 py-2.5 text-right text-slate-700">{formatMoney(m.revenueActual)}</td>
                          <td className="px-4 py-2.5 text-right text-slate-700">{formatMoney(m.expenseActual)}</td>
                          <td className={cn('px-4 py-2.5 text-right font-medium', signClass(m.profitActual))}>
                            {formatSignedMoney(m.profitActual)}
                          </td>
                          <td className="px-4 py-2.5 text-right text-slate-500">{pctText(m.margin)}</td>
                        </tr>
                      )
                    })}
                  </tbody>
                  <tfoot className="bg-slate-50 font-medium text-slate-700">
                    <tr>
                      <td className="px-4 py-2.5">Jami</td>
                      <td className="px-4 py-2.5 text-right">{formatMoney(data.netPlanTotal)}</td>
                      <td className="px-4 py-2.5 text-right">{formatMoney(data.revenueActualTotal)}</td>
                      <td className="px-4 py-2.5 text-right">{formatMoney(data.expenseActualTotal)}</td>
                      <td className={cn('px-4 py-2.5 text-right', signClass(data.profitActualTotal))}>
                        {formatSignedMoney(data.profitActualTotal)}
                      </td>
                      <td className="px-4 py-2.5 text-right text-slate-400">—</td>
                    </tr>
                  </tfoot>
                </table>
              </div>
            </Card>
          </div>
        )}
      </ReportState>
    </div>
  )
}
