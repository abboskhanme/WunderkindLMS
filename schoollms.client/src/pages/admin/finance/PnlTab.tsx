/**
 * Foyda va zarar (P&L) — P1-18.
 *
 * Manba: `GET /api/admin/finance/pnl` — `ledger_entries` ni akkaunt
 * prefiksi bo'yicha yig'adi. Bu yerda hech narsa qayta hisoblanmaydi:
 * `revenueTotal`, `expenseTotal`, `net` — serverning raqamlari. JS'da
 * pul `float64`, shuning uchun yig'indini qayta hisoblash tiyin xatosini
 * keltirib chiqarardi (BillingDtos.cs, 2-qoida).
 *
 * Ulush foizi (%) — YAGONA hisob, u ham faqat ko'rsatish uchun: qaysi
 * toifa xarajatning yarmini yeyayotganini ko'rsatadi.
 */
import { Download, TrendingDown, TrendingUp, Wallet } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import { getProfitLoss, type ProfitLossLine } from '@/api/services/financeReports'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { StatCard } from '@/components/ui/StatCard'
import { cn, exportToCsv, formatDate, formatMoney } from '@/lib/utils'
import { ReportState } from './ReportState'
import { accountLabel, formatSignedMoney, signClass } from './reportLabels'

interface Props {
  /** "YYYY-MM-DD" */
  from: string
  to: string
}

export function PnlTab({ from, to }: Props) {
  const { data, loading, error, refetch } = useAsync(() => getProfitLoss(from, to), [from, to])

  const isEmpty = !!data && data.revenueTotal === 0 && data.expenseTotal === 0

  const handleExport = () => {
    if (!data) return
    exportToCsv(
      `foyda-zarar_${data.from}_${data.to}.csv`,
      ["Yo'nalish", 'Toifa', 'Summa'],
      [
        ...data.revenue.map((l) => ['Daromad', accountLabel(l.account), String(l.amount)]),
        ...data.expense.map((l) => ['Xarajat', accountLabel(l.account), String(l.amount)]),
        ['Jami', 'Daromad', String(data.revenueTotal)],
        ['Jami', 'Xarajat', String(data.expenseTotal)],
        ['Jami', 'Sof natija', String(data.net)],
      ],
    )
  }

  return (
    <ReportState
      loading={loading}
      error={error}
      isEmpty={isEmpty}
      emptyTitle="Bu davrda moliyaviy harakat yo'q"
      emptyHint="Boshqa davrni tanlang yoki oylik hisoblashni ishga tushiring."
      onRetry={refetch}
    >
      {data && (
        <div className="space-y-6">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <StatCard
              label="Daromad"
              value={formatMoney(data.revenueTotal)}
              icon={TrendingUp}
              iconBg="bg-emerald-50"
              iconColor="text-emerald-600"
              hint={`${formatDate(data.from)} — ${formatDate(data.to)}`}
            />
            <StatCard
              label="Xarajat"
              value={formatMoney(data.expenseTotal)}
              icon={TrendingDown}
              iconBg="bg-red-50"
              iconColor="text-red-600"
              hint={`${data.expense.filter((l) => l.amount !== 0).length} ta toifa`}
            />
            <StatCard
              label="Sof natija"
              value={formatSignedMoney(data.net)}
              icon={Wallet}
              iconBg={data.net >= 0 ? 'bg-emerald-50' : 'bg-red-50'}
              iconColor={data.net >= 0 ? 'text-emerald-600' : 'text-red-600'}
              hint="Daromad − Xarajat"
            />
          </div>

          <div className="flex justify-end">
            <Button variant="secondary" onClick={handleExport}>
              <Download className="h-4 w-4" /> CSV
            </Button>
          </div>

          <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
            <LinesTable
              title="Daromad — toifalar kesimida"
              lines={data.revenue}
              total={data.revenueTotal}
              positive
            />
            <LinesTable
              title="Xarajat — toifalar kesimida"
              lines={data.expense}
              total={data.expenseTotal}
              positive={false}
            />
          </div>

          <Card className={cn(data.net >= 0 ? 'border-emerald-200' : 'border-red-200')}>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <p className="text-sm font-medium text-slate-600">Davr yakuni</p>
                <p className="text-xs text-slate-400">
                  Storno qilingan yozuvlar avtomatik chiqarib tashlangan.
                </p>
              </div>
              <p className={cn('text-2xl font-semibold', signClass(data.net))}>
                {formatSignedMoney(data.net)}
              </p>
            </div>
          </Card>
        </div>
      )}
    </ReportState>
  )
}

function LinesTable({
  title,
  lines,
  total,
  positive,
}: {
  title: string
  lines: ProfitLossLine[]
  total: number
  positive: boolean
}) {
  // Harakati bo'lmagan hisoblarni yashirmaymiz — ular "0" bo'lib turishi
  // kerak, aks holda ustunlar oydan oyga o'zgarib ketardi va "qayerga
  // ketdi?" degan savol tug'ilardi (FinanceReportQueries.Lines izohi).
  const share = (amount: number) => (total === 0 ? 0 : Math.round((amount / total) * 100))

  return (
    <Card className="p-0">
      <div className="border-b border-slate-100 p-4">
        <h2 className="font-semibold text-slate-800">{title}</h2>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
            <tr>
              <th className="px-4 py-3">Toifa</th>
              <th className="px-4 py-3 text-right">Summa</th>
              <th className="px-4 py-3 text-right">Ulush</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {lines.map((l) => (
              <tr key={l.account} className="hover:bg-slate-50/60">
                <td className="px-4 py-3 text-slate-600">{accountLabel(l.account)}</td>
                <td
                  className={cn(
                    'px-4 py-3 text-right font-medium',
                    l.amount === 0
                      ? 'text-slate-300'
                      : positive
                        ? 'text-emerald-600'
                        : 'text-red-600',
                  )}
                >
                  {formatMoney(l.amount)}
                </td>
                <td className="px-4 py-3 text-right text-slate-400">{share(l.amount)}%</td>
              </tr>
            ))}
            {lines.length === 0 && (
              <tr>
                <td colSpan={3} className="px-4 py-10 text-center text-slate-400">
                  Bu davrda yozuv yo'q
                </td>
              </tr>
            )}
          </tbody>
          <tfoot>
            <tr className="border-t border-slate-200 bg-slate-50/60 text-sm font-semibold">
              <td className="px-4 py-3 text-slate-700">Jami</td>
              <td
                className={cn(
                  'px-4 py-3 text-right',
                  positive ? 'text-emerald-700' : 'text-red-700',
                )}
              >
                {formatMoney(total)}
              </td>
              <td className="px-4 py-3 text-right text-slate-400">100%</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </Card>
  )
}
