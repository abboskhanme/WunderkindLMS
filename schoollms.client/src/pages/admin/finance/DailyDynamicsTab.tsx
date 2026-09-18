/**
 * P&L 2.0 — F6.04 kunlik dinamika.
 *
 * Manba: `GET /api/admin/finance/pnl/expectation/daily` — to'liq qoida
 * `FinanceReportQueries.RevenueExpectationDaily.cs` da. Bashorat chizig'i
 * ATAYLAB yo'q — pastdagi eslatmaga qarang.
 */
import { TrendingDown, TrendingUp, Wallet } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import { getDailyDynamics } from '@/api/services/financeReports'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { PnlDailyChart } from '@/components/charts/PnlDailyChart'
import { formatMoney } from '@/lib/utils'
import { formatSignedMoney } from './reportLabels'
import { ReportState } from './ReportState'

interface Props {
  /** "YYYY-MM" */
  month: string
}

export function DailyDynamicsTab({ month }: Props) {
  const { data, loading, error, refetch } = useAsync(() => getDailyDynamics(month), [month])

  const totals = data
    ? {
        revenue: data.days.reduce((s, d) => s + d.revenue, 0),
        expense: data.days.reduce((s, d) => s + d.expense, 0),
        net: data.days.reduce((s, d) => s + d.net, 0),
      }
    : null

  return (
    <div className="space-y-5">
      <ReportState
        loading={loading}
        error={error}
        isEmpty={!!data && totals !== null && totals.revenue === 0 && totals.expense === 0}
        emptyTitle="Bu oyda kunlik harakat yo'q"
        emptyHint="Boshqa oyni tanlang."
        onRetry={refetch}
      >
        {data && totals && (
          <div className="space-y-5">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              <StatCard
                label="Oy bo'yicha daromad"
                value={formatMoney(totals.revenue)}
                icon={TrendingUp}
                iconBg="bg-emerald-50"
                iconColor="text-emerald-600"
              />
              <StatCard
                label="Oy bo'yicha chiqim"
                value={formatMoney(totals.expense)}
                icon={TrendingDown}
                iconBg="bg-red-50"
                iconColor="text-red-600"
              />
              <StatCard
                label="Sof natija"
                value={formatSignedMoney(totals.net)}
                icon={Wallet}
                iconBg={totals.net >= 0 ? 'bg-emerald-50' : 'bg-red-50'}
                iconColor={totals.net >= 0 ? 'text-emerald-600' : 'text-red-600'}
                hint={data.today ? `bugun: ${data.today}` : undefined}
              />
            </div>

            <Card>
              <PnlDailyChart days={data.days} today={data.today} />
            </Card>

            <Card className="bg-slate-50/60 text-xs text-slate-500">
              Bu grafik faqat FAKT (jurnaldagi haqiqiy harakat) — bashorat chizig'i yo'q: uni chizish
              uchun modellashtirish qarori kerak (masalan, o'tgan oylar o'rtachasi), bu hali qabul
              qilinmagan.
            </Card>
          </div>
        )}
      </ReportState>
    </div>
  )
}
