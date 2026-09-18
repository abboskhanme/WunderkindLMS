/**
 * P&L 2.0 kunlik dinamikasi (§2.6, F6.04): har kunning daromad/chiqim ustuni
 * va oy boshidan yig'ilgan sof natija chizig'i.
 *
 * Uslub `CashFlowChart.tsx`/`DailyCashChart.tsx` dan olingan — loyihada
 * grafiklar bir xil ko'rinishda bo'lishi kerak. RAQAM SERVERDAN: bu fayl
 * birorta summani qo'shmaydi yoki qayta hisoblamaydi.
 *
 * "Bugun" chizig'i — <c>today</c> berilsa, o'sha kunga vertikal chiziq.
 * Bashorat chizig'i YO'Q (EduSchool'da bor) — sabab
 * `FinanceReportQueries.RevenueExpectationDaily.cs` fayl boshidagi izohda:
 * bashorat qurish modellashtirish qarori talab qiladi, bu vazifada yozilmagan.
 */
import {
  Bar,
  CartesianGrid,
  ComposedChart,
  Legend,
  Line,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { DailyDynamicsDay } from '@/api/services/financeReports'
import { formatMoney } from '@/lib/utils'
import { chartColors, shortAmount } from '@/pages/admin/finance/reportLabels'

interface Props {
  days: DailyDynamicsDay[]
  today: string | null
}

/** "2026-09-17" → "17.09" (o'q tor, yil takrorlanmasin). */
function dayLabel(iso: string): string {
  const [, month, day] = iso.split('-')
  return `${day}.${month}`
}

export function PnlDailyChart({ days, today }: Props) {
  const data = days.map((d) => ({
    name: dayLabel(d.date),
    Daromad: d.revenue,
    Chiqim: d.expense,
    "Yig'ilgan sof": d.cumulativeNet,
  }))

  const todayLabel = today ? dayLabel(today) : null

  return (
    <ResponsiveContainer width="100%" height={300}>
      <ComposedChart data={data} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={chartColors.grid} />
        <XAxis
          dataKey="name"
          tickLine={false}
          axisLine={false}
          interval="preserveStartEnd"
          minTickGap={16}
          tick={{ fontSize: 12, fill: chartColors.neutral }}
        />
        <YAxis
          tickLine={false}
          axisLine={false}
          tick={{ fontSize: 12, fill: chartColors.neutral }}
          tickFormatter={shortAmount}
        />
        <Tooltip
          cursor={{ fill: 'rgba(0,0,0,0.03)' }}
          contentStyle={{ borderRadius: 12, border: '1px solid #e2e8f0', fontSize: 13 }}
          formatter={(value) => formatMoney(Number(value))}
        />
        <Legend wrapperStyle={{ fontSize: 13 }} />
        <ReferenceLine y={0} stroke="#cbd5e1" />
        {todayLabel && (
          <ReferenceLine x={todayLabel} stroke={chartColors.neutral} strokeDasharray="4 4" />
        )}
        <Bar dataKey="Daromad" fill={chartColors.inflow} radius={[6, 6, 0, 0]} maxBarSize={18} />
        <Bar dataKey="Chiqim" fill={chartColors.outflow} radius={[6, 6, 0, 0]} maxBarSize={18} />
        <Line
          type="monotone"
          dataKey="Yig'ilgan sof"
          stroke={chartColors.balance}
          strokeWidth={2}
          dot={false}
        />
      </ComposedChart>
    </ResponsiveContainer>
  )
}
