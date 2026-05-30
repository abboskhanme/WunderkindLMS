import { useEffect, useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import { ArrowLeft, Users, Star, CalendarCheck } from 'lucide-react'
import type { SchoolClass } from '@/types'
import { getClasses } from '@/api/services/classes'
import {
  getClassPerformance,
  type ClassPerformanceData,
} from '@/api/services/classPerformance'
import { languageLabels } from '@/config/constants'
import { formatMoney, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'

function gradeColor(g: number): string {
  if (g >= 4.5) return 'text-emerald-600'
  if (g >= 4) return 'text-brand-600'
  if (g >= 3.5) return 'text-amber-600'
  return 'text-red-600'
}

function attColor(a: number): string {
  if (a >= 95) return 'text-emerald-600'
  if (a >= 90) return 'text-amber-600'
  return 'text-red-600'
}

export function ClassDetailPage() {
  const { id = '' } = useParams()
  const [cls, setCls] = useState<SchoolClass | null>(null)
  const [data, setData] = useState<ClassPerformanceData | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    Promise.all([getClasses(), getClassPerformance(id)])
      .then(([cl, perf]) => {
        setCls(cl.find((c) => c.id === id) ?? null)
        setData(perf)
      })
      .finally(() => setLoading(false))
  }, [id])

  const rows = data?.rows ?? []
  const subjects = data?.subjects ?? []
  const studentsCount = rows.length
  const classAverage = studentsCount
    ? Math.round((rows.reduce((a, r) => a + r.average, 0) / studentsCount) * 10) / 10
    : 0
  const attVals = rows.map((r) => r.attendance).filter((a): a is number => a != null)
  const avgAttendance = attVals.length
    ? Math.round(attVals.reduce((a, b) => a + b, 0) / attVals.length)
    : null

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <Link
          to="/admin/classes"
          className="rounded-lg p-2 text-slate-500 transition-colors hover:bg-slate-100"
        >
          <ArrowLeft className="h-5 w-5" />
        </Link>
        <div>
          <h1 className="text-xl font-semibold text-slate-800">
            {cls ? `${cls.name}-sinf` : 'Sinf'}
          </h1>
          {cls && (
            <p className="text-sm text-slate-400">
              {languageLabels[cls.language]} sinfi
              {cls.room ? ` · ${cls.room}-xona` : ''} · {formatMoney(cls.monthlyFee)}
            </p>
          )}
        </div>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          {/* Umumiy ko'rsatkichlar */}
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <StatCard label="O'quvchilar" value={studentsCount} icon={Users} />
            <StatCard
              label="O'rtacha baho"
              value={classAverage.toFixed(1)}
              icon={Star}
              iconBg="bg-amber-50"
              iconColor="text-amber-600"
            />
            <StatCard
              label="O'rtacha davomat"
              value={avgAttendance == null ? '—' : `${avgAttendance}%`}
              icon={CalendarCheck}
              iconBg="bg-emerald-50"
              iconColor="text-emerald-600"
            />
          </div>

          {/* O'quvchilar va baholar */}
          <Card className="p-0">
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="w-10 px-4 py-3">#</th>
                    <th className="px-4 py-3">F.I.SH</th>
                    {subjects.map((s) => (
                      <th key={s.id} className="px-3 py-3 text-center font-medium">
                        {s.name}
                      </th>
                    ))}
                    <th className="px-3 py-3 text-center">O'rtacha</th>
                    <th className="px-3 py-3 text-center">Davomat</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {rows.map((r, i) => (
                    <tr key={r.student.id} className="hover:bg-slate-50/60">
                      <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                      <td className="px-4 py-3 font-medium text-slate-800">
                        {r.student.fullName}
                      </td>
                      {subjects.map((s) => (
                        <td
                          key={s.id}
                          className={cn('px-3 py-3 text-center font-medium', gradeColor(r.grades[s.id]))}
                        >
                          {r.grades[s.id]?.toFixed(1) ?? '—'}
                        </td>
                      ))}
                      <td className={cn('px-3 py-3 text-center font-semibold', gradeColor(r.average))}>
                        {r.average.toFixed(1)}
                      </td>
                      <td
                        className={cn(
                          'px-3 py-3 text-center font-medium',
                          r.attendance == null ? 'text-slate-300' : attColor(r.attendance),
                        )}
                      >
                        {r.attendance == null ? '—' : `${r.attendance}%`}
                      </td>
                    </tr>
                  ))}
                  {rows.length === 0 && (
                    <tr>
                      <td
                        colSpan={subjects.length + 4}
                        className="px-4 py-12 text-center text-slate-400"
                      >
                        Bu sinfda o'quvchilar yo'q
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </Card>
        </>
      )}
    </div>
  )
}
