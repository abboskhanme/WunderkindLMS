import { useEffect, useMemo, useState } from 'react'
import { Users, Star, CalendarCheck } from 'lucide-react'
import type { SchoolClass } from '@/types'
import { getClasses } from '@/api/services/classes'
import { getStudentsRating, type StudentRatingRow } from '@/api/services/classPerformance'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

type Metric = 'grade' | 'attendance'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none focus:border-brand-400'

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
const rankBadge = (i: number) =>
  i === 0
    ? 'bg-amber-100 text-amber-700'
    : i === 1
      ? 'bg-slate-200 text-slate-600'
      : i === 2
        ? 'bg-orange-100 text-orange-700'
        : 'bg-slate-100 text-slate-500'

export function ClassRatingPage() {
  const [rows, setRows] = useState<StudentRatingRow[]>([])
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [loading, setLoading] = useState(true)
  const [gradeFilter, setGradeFilter] = useState<'all' | number>('all')
  const [classFilter, setClassFilter] = useState('all')
  const [metric, setMetric] = useState<Metric>('grade')

  useEffect(() => {
    Promise.all([getStudentsRating(), getClasses()])
      .then(([r, c]) => {
        setRows(r)
        setClasses(c)
      })
      .finally(() => setLoading(false))
  }, [])

  const grades = useMemo(
    () => [...new Set(classes.map((c) => c.grade))].sort((a, b) => a - b),
    [classes],
  )

  // Daraja tanlanganda — faqat shu darajadagi sinflar
  const classOptions = useMemo(
    () =>
      classes
        .filter((c) => gradeFilter === 'all' || c.grade === gradeFilter)
        .map((c) => c.name),
    [classes, gradeFilter],
  )

  const filtered = useMemo(() => {
    const list = rows.filter((r) => {
      if (classFilter !== 'all') return r.className === classFilter
      if (gradeFilter !== 'all') return r.grade === gradeFilter
      return true
    })
    return [...list].sort((a, b) =>
      metric === 'grade' ? b.average - a.average : (b.attendance ?? 0) - (a.attendance ?? 0),
    )
  }, [rows, classFilter, gradeFilter, metric])

  const summary = useMemo(() => {
    const n = filtered.length
    if (!n) return { n: 0, grade: 0, attendance: null as number | null }
    const attVals = filtered.map((r) => r.attendance).filter((a): a is number => a != null)
    return {
      n,
      grade: Math.round((filtered.reduce((a, r) => a + r.average, 0) / n) * 10) / 10,
      attendance: attVals.length
        ? Math.round(attVals.reduce((a, b) => a + b, 0) / attVals.length)
        : null,
    }
  }, [filtered])

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Reyting</h1>
        <p className="text-sm text-slate-400">O'quvchilar baho va davomat reytingi</p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <StatCard label="O'quvchilar" value={summary.n} icon={Users} />
            <StatCard
              label="O'rtacha baho"
              value={summary.grade.toFixed(1)}
              icon={Star}
              iconBg="bg-amber-50"
              iconColor="text-amber-600"
            />
            <StatCard
              label="O'rtacha davomat"
              value={summary.attendance == null ? '—' : `${summary.attendance}%`}
              icon={CalendarCheck}
              iconBg="bg-emerald-50"
              iconColor="text-emerald-600"
            />
          </div>

          <Card>
            <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
              <div className="flex flex-wrap gap-2">
                <select
                  value={gradeFilter}
                  onChange={(e) => {
                    setGradeFilter(e.target.value === 'all' ? 'all' : Number(e.target.value))
                    setClassFilter('all')
                  }}
                  className={control}
                >
                  <option value="all">Barcha darajalar</option>
                  {grades.map((g) => (
                    <option key={g} value={g}>
                      {g}-sinflar
                    </option>
                  ))}
                </select>
                <select
                  value={classFilter}
                  onChange={(e) => setClassFilter(e.target.value)}
                  className={control}
                >
                  <option value="all">Barcha sinflar</option>
                  {classOptions.map((name) => (
                    <option key={name} value={name}>
                      {name}
                    </option>
                  ))}
                </select>
              </div>
              <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1 text-sm">
                <button
                  onClick={() => setMetric('grade')}
                  className={cn(
                    'rounded-md px-3 py-1 font-medium transition-colors',
                    metric === 'grade' ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500',
                  )}
                >
                  O'rtacha baho
                </button>
                <button
                  onClick={() => setMetric('attendance')}
                  className={cn(
                    'rounded-md px-3 py-1 font-medium transition-colors',
                    metric === 'attendance'
                      ? 'bg-white text-brand-700 shadow-sm'
                      : 'text-slate-500',
                  )}
                >
                  Davomat
                </button>
              </div>
            </div>

            <div className="space-y-2">
              {filtered.map((r, i) => {
                const isGrade = metric === 'grade'
                const noData = !isGrade && r.attendance == null
                const numValue = isGrade ? r.average : (r.attendance ?? 0)
                const pct = isGrade ? (numValue / 5) * 100 : numValue
                return (
                  <div
                    key={r.student.id}
                    className="flex items-center gap-3 rounded-xl border border-slate-100 p-3"
                  >
                    <div
                      className={cn(
                        'flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-sm font-bold',
                        rankBadge(i),
                      )}
                    >
                      {i + 1}
                    </div>
                    <div className="min-w-0 flex-1">
                      <p className="truncate font-medium text-slate-800">{r.student.fullName}</p>
                    </div>
                    <span className="hidden shrink-0 rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600 sm:inline">
                      {r.className}
                    </span>
                    <div className="hidden h-2 w-32 overflow-hidden rounded-full bg-slate-100 md:block">
                      <div
                        className={cn(
                          'h-full rounded-full',
                          metric === 'grade' ? 'bg-brand-500' : 'bg-emerald-500',
                        )}
                        style={{ width: `${pct}%` }}
                      />
                    </div>
                    <div
                      className={cn(
                        'w-14 shrink-0 text-right font-semibold',
                        isGrade ? gradeColor(numValue) : noData ? 'text-slate-300' : attColor(numValue),
                      )}
                    >
                      {isGrade ? numValue.toFixed(1) : noData ? '—' : `${numValue}%`}
                    </div>
                  </div>
                )
              })}
              {filtered.length === 0 && (
                <p className="py-8 text-center text-slate-400">O'quvchilar yo'q</p>
              )}
            </div>
          </Card>
        </>
      )}
    </div>
  )
}
