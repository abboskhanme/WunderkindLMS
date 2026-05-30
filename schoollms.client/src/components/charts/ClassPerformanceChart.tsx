import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { ClassPerformance } from '@/types'

export type Metric = 'grade' | 'attendance'

interface Props {
  data: ClassPerformance[]
  metric: Metric
}

export function ClassPerformanceChart({ data, metric }: Props) {
  const isGrade = metric === 'grade'
  const dataKey = isGrade ? 'averageGrade' : 'attendanceRate'
  const color = isGrade ? '#1f47f5' : '#16a34a'
  const domain: [number, number] = isGrade ? [0, 5] : [0, 100]
  const unit = isGrade ? '' : '%'
  const label = isGrade ? "O'rtacha baho" : 'Davomat'

  return (
    <ResponsiveContainer width="100%" height={300}>
      <BarChart data={data} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#eef0f4" />
        <XAxis
          dataKey="className"
          tickLine={false}
          axisLine={false}
          tick={{ fontSize: 12, fill: '#94a3b8' }}
        />
        <YAxis
          domain={domain}
          tickLine={false}
          axisLine={false}
          tick={{ fontSize: 12, fill: '#94a3b8' }}
        />
        <Tooltip
          cursor={{ fill: 'rgba(0,0,0,0.03)' }}
          contentStyle={{ borderRadius: 12, border: '1px solid #e2e8f0', fontSize: 13 }}
          formatter={(value) => [`${value}${unit}`, label]}
        />
        <Bar dataKey={dataKey} fill={color} radius={[6, 6, 0, 0]} maxBarSize={42} />
      </BarChart>
    </ResponsiveContainer>
  )
}
