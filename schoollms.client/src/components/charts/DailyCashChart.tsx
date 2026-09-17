/**
 * KUNLIK KIRIM/CHIQIM GRAFIGI (docs/modules/finance-parity.md §2.4 F4.01).
 *
 * Ustun yoki chiziq — bitta tugma bilan almashadi: uzun davrda (bir necha
 * oy) ustunlar qisilib ketadi, chiziq esa tendensiyani ko'rsatadi; qisqa
 * davrda esa aksincha, har kunning ustuni aniqroq.
 *
 * Uslub `CashFlowChart.tsx` dan olingan — loyihada grafiklar bir xil
 * ko'rinishda bo'lishi kerak (ranglar `reportLabels.chartColors` dan).
 * RAQAM SERVERDAN: bu fayl birorta summani qo'shmaydi.
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
import type { FinanceDay } from '@/api/services/financeStatements'
import { formatMoney } from '@/lib/utils'
import { chartColors, shortAmount } from '@/pages/admin/finance/reportLabels'

interface Props {
  days: FinanceDay[]
  /** `bar` — har kun ustun, `line` — tendensiya chizig'i. */
  mode: 'bar' | 'line'
}

/** "2026-09-17" → "17.09" (o'q tor, yil takrorlanmasin). */
function dayLabel(iso: string): string {
  const [, month, day] = iso.split('-')
  return `${day}.${month}`
}

export function DailyCashChart({ days, mode }: Props) {
  const data = days.map((d) => ({
    name: dayLabel(d.date),
    Kirim: d.inflow,
    Chiqim: d.outflow,
    Sof: d.net,
  }))

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

        {/*
          Recharts bolalarni TIPI bo'yicha o'qiydi, shuning uchun bu yerda
          Fragment ishlatilmaydi — har bir seriya alohida shart bilan
          chiziladi (false bola e'tiborsiz qoldiriladi).
        */}
        {mode === 'bar' && (
          <Bar dataKey="Kirim" fill={chartColors.inflow} radius={[6, 6, 0, 0]} maxBarSize={22} />
        )}
        {mode === 'bar' && (
          <Bar dataKey="Chiqim" fill={chartColors.outflow} radius={[6, 6, 0, 0]} maxBarSize={22} />
        )}
        {mode === 'line' && (
          <Line type="monotone" dataKey="Kirim" stroke={chartColors.inflow} strokeWidth={2} dot={false} />
        )}
        {mode === 'line' && (
          <Line type="monotone" dataKey="Chiqim" stroke={chartColors.outflow} strokeWidth={2} dot={false} />
        )}
        {mode === 'line' && (
          <Line type="monotone" dataKey="Sof" stroke={chartColors.balance} strokeWidth={2} dot={false} />
        )}
      </ComposedChart>
    </ResponsiveContainer>
  )
}
