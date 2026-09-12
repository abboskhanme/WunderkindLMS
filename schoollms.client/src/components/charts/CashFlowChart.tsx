/**
 * Pul oqimi grafigi (P1-18): oylar bo'yicha kirim/chiqim ustunlari va
 * ularning ustidan oy oxiridagi QOLDIQ chizig'i.
 *
 * Nega bitta grafikda: kirim va chiqim alohida qaralganda "shu oyda pul
 * qanchaga kamaydi" degan savol javobsiz qoladi. Qoldiq chizig'i esa aynan
 * shuni ko'rsatadi va manfiy tomonga o'tsa darrov ko'zga tashlanadi.
 *
 * Uslub `FinanceMonthlyChart.tsx` dan olingan — loyihada grafiklar bir xil
 * ko'rinishda bo'lishi kerak.
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
import type { CashFlowMonth } from '@/api/services/financeReports'
import { formatMoney } from '@/lib/utils'
import { chartColors, formatMonthLabel, shortAmount } from '@/pages/admin/finance/reportLabels'

interface Props {
  months: CashFlowMonth[]
}

export function CashFlowChart({ months }: Props) {
  const data = months.map((m) => ({
    name: formatMonthLabel(m.month),
    Kirim: m.inflow,
    Chiqim: m.outflow,
    Qoldiq: m.closing,
  }))

  return (
    <ResponsiveContainer width="100%" height={320}>
      <ComposedChart data={data} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={chartColors.grid} />
        <XAxis
          dataKey="name"
          tickLine={false}
          axisLine={false}
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
        <Bar dataKey="Kirim" fill={chartColors.inflow} radius={[6, 6, 0, 0]} maxBarSize={28} />
        <Bar dataKey="Chiqim" fill={chartColors.outflow} radius={[6, 6, 0, 0]} maxBarSize={28} />
        <Line
          type="monotone"
          dataKey="Qoldiq"
          stroke={chartColors.balance}
          strokeWidth={2}
          dot={{ r: 3 }}
        />
      </ComposedChart>
    </ResponsiveContainer>
  )
}
