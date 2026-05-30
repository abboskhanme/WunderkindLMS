import { useState } from 'react'
import { Users, GraduationCap, Star, CalendarCheck } from 'lucide-react'
import { getAdminDashboard } from '@/api/services/dashboard'
import { useAsync } from '@/hooks/useAsync'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import {
  ClassPerformanceChart,
  type Metric,
} from '@/components/charts/ClassPerformanceChart'
import { cn } from '@/lib/utils'
import { TodaySchedule } from './TodaySchedule'

export function AdminDashboard() {
  const { data, loading, error } = useAsync(getAdminDashboard, [])
  const [metric, setMetric] = useState<Metric>('grade')

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (error) return <p className="text-red-600">Xatolik: {error}</p>
  if (!data) return null

  const { stats, classPerformance, topClasses } = data

  // O'rtacha baho bo'yicha eng yuqori 5 ta sinf
  const ranked = [...topClasses]
    .sort((a, b) => b.averageGrade - a.averageGrade)
    .slice(0, 5)

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Bosh sahifa</h1>
        <p className="text-sm text-slate-400">Maktab bo'yicha umumiy ko'rsatkichlar</p>
      </div>

      {/* Statistik kartalar */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          label="O'quvchilar soni"
          value={stats.studentsCount.toLocaleString()}
          icon={Users}
        />
        <StatCard
          label="O'qituvchilar soni"
          value={stats.teachersCount}
          icon={GraduationCap}
          iconBg="bg-violet-50"
          iconColor="text-violet-600"
        />
        <StatCard
          label="O'rtacha baho"
          value={stats.averageGrade.toFixed(1)}
          icon={Star}
          iconBg="bg-amber-50"
          iconColor="text-amber-600"
          hint="5 ballik tizim"
        />
        <StatCard
          label="Umumiy davomat"
          value={stats.attendanceRate == null ? '—' : `${stats.attendanceRate}%`}
          icon={CalendarCheck}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
        />
      </div>

      {/* Bugungi dars jadvali (barcha sinflar) */}
      <TodaySchedule />

      {/* Statistika grafigi + reyting */}
      <div className="grid grid-cols-1 gap-6 xl:grid-cols-3">
        {/* Grafik (baho / davomat tanlash) */}
        <Card className="xl:col-span-2">
          <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
            <h2 className="font-semibold text-slate-800">Sinflar bo'yicha statistika</h2>
            <div className="flex rounded-lg bg-slate-100 p-1 text-sm">
              <button onClick={() => setMetric('grade')} className={toggleBtn(metric === 'grade')}>
                O'rtacha baho
              </button>
              <button
                onClick={() => setMetric('attendance')}
                className={toggleBtn(metric === 'attendance')}
              >
                Davomat
              </button>
            </div>
          </div>
          <ClassPerformanceChart data={classPerformance} metric={metric} />
        </Card>

        {/* Eng yuqori o'rtacha baholi sinflar (Top 5) */}
        <Card>
          <h2 className="mb-1 font-semibold text-slate-800">Eng yuqori bahoga ega sinflar</h2>
          <p className="mb-4 text-xs text-slate-400">O'rtacha baho bo'yicha Top 5</p>
          <ul className="space-y-2">
            {ranked.map((c, i) => (
              <li
                key={c.id}
                className="flex items-center gap-3 rounded-xl border border-slate-100 p-3"
              >
                <div
                  className={cn(
                    'flex h-9 w-9 shrink-0 items-center justify-center rounded-lg text-sm font-bold',
                    i === 0
                      ? 'bg-amber-100 text-amber-700'
                      : 'bg-slate-100 text-slate-500',
                  )}
                >
                  {i + 1}
                </div>
                <div className="min-w-0 flex-1">
                  <p className="font-medium text-slate-800">{c.name}</p>
                  <p className="text-xs text-slate-400">{c.studentsCount} o'quvchi</p>
                </div>
                <div className="flex items-center gap-1 text-amber-600">
                  <Star className="h-4 w-4 fill-amber-400 text-amber-400" />
                  <span className="font-semibold">{c.averageGrade.toFixed(1)}</span>
                </div>
              </li>
            ))}
          </ul>
        </Card>
      </div>
    </div>
  )
}

function toggleBtn(active: boolean): string {
  return cn(
    'rounded-md px-3 py-1 font-medium transition-colors',
    active ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
  )
}
