import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { FinanceMonthly } from '@/types'
import { monthShortNames } from '@/config/constants'
import { formatMoney } from '@/lib/utils'

interface Props {
  data: FinanceMonthly[]
}

export function FinanceMonthlyChart({ data }: Props) {
  const chartData = data.map((d) => ({
    name: monthShortNames[Number(d.month.slice(5, 7)) - 1] ?? d.month,
    Kirim: d.income,
    Chiqim: d.expense,
  }))

  return (
    <ResponsiveContainer width="100%" height={300}>
      <BarChart data={chartData} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#eef0f4" />
        <XAxis dataKey="name" tickLine={false} axisLine={false} tick={{ fontSize: 12, fill: '#94a3b8' }} />
        <YAxis
          tickLine={false}
          axisLine={false}
          tick={{ fontSize: 12, fill: '#94a3b8' }}
          tickFormatter={(v: number) => (v >= 1_000_000 ? `${v / 1_000_000}M` : `${v / 1000}k`)}
        />
        <Tooltip
          cursor={{ fill: 'rgba(0,0,0,0.03)' }}
          contentStyle={{ borderRadius: 12, border: '1px solid #e2e8f0', fontSize: 13 }}
          formatter={(value) => formatMoney(Number(value))}
        />
        <Legend wrapperStyle={{ fontSize: 13 }} />
        <Bar dataKey="Kirim" fill="#16a34a" radius={[6, 6, 0, 0]} maxBarSize={28} />
        <Bar dataKey="Chiqim" fill="#dc2626" radius={[6, 6, 0, 0]} maxBarSize={28} />
      </BarChart>
    </ResponsiveContainer>
  )
}
