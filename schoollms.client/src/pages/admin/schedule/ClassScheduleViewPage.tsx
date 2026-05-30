import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import type {
  SchoolClass,
  ScheduleTemplate,
  SchoolSettings,
  Subject,
  Teacher,
  WeekAssignment,
} from '@/types'
import { getClasses } from '@/api/services/classes'
import { getTeachers } from '@/api/services/teachers'
import { getSubjects } from '@/api/services/subjects'
import { getSettings } from '@/api/services/settings'
import { getTemplates } from '@/api/services/scheduleTemplates'
import { getWeekAssignments } from '@/api/services/weekAssignments'
import { quarters, weekDays, schedulePeriods } from '@/config/constants'
import { getQuarterWeeks, getCurrentQuarterAndWeek } from '@/lib/weeks'
import { formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * Sinf dars jadvalini KO'RISH (faqat o'qish). Sinf + chorak + hafta tanlanadi va shu haftaga
 * biriktirilgan jadval kun×dars to'rida ko'rsatiladi. Jadval yaratish/biriktirish alohida
 * "Dars jadvali yaratish" bo'limida.
 */
export function ClassScheduleViewPage() {
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [settings, setSettings] = useState<SchoolSettings | null>(null)
  const [loading, setLoading] = useState(true)

  const [classId, setClassId] = useState('')
  const [quarter, setQuarter] = useState(1)
  const [week, setWeek] = useState(1)

  const [templates, setTemplates] = useState<ScheduleTemplate[]>([])
  const [assignments, setAssignments] = useState<WeekAssignment[]>([])
  const [dataLoading, setDataLoading] = useState(false)

  useEffect(() => {
    Promise.all([getClasses(), getSubjects(), getTeachers(), getSettings()])
      .then(([cls, subs, tchs, st]) => {
        setClasses(cls)
        setSubjects(subs)
        setTeachers(tchs)
        setSettings(st)
        setClassId(cls[0]?.id ?? '')
        const { quarter: q, week: w } = getCurrentQuarterAndWeek(st.quarters)
        setQuarter(q)
        setWeek(w)
      })
      .finally(() => setLoading(false))
  }, [])

  // Sinf o'zgarsa — uning jadvallari (templates)
  useEffect(() => {
    if (!classId) return
    let active = true
    getTemplates(classId).then((t) => {
      if (active) setTemplates(t)
    })
    return () => {
      active = false
    }
  }, [classId])

  // Sinf yoki chorak o'zgarsa — hafta-biriktirishlar
  useEffect(() => {
    if (!classId) return
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi so'rovdan oldin holatni belgilash (maqsadli)
    setDataLoading(true)
    getWeekAssignments(classId, quarter)
      .then((a) => {
        if (active) setAssignments(a)
      })
      .finally(() => {
        if (active) setDataLoading(false)
      })
    return () => {
      active = false
    }
  }, [classId, quarter])

  const weeks = useMemo(() => {
    if (!settings) return []
    const q = settings.quarters.find((x) => x.quarter === quarter)
    return q && q.startDate && q.endDate ? getQuarterWeeks(q.startDate, q.endDate) : []
  }, [settings, quarter])

  useEffect(() => {
    if (weeks.length > 0 && !weeks.some((w) => w.week === week)) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- haftani amaldagi diapazonga moslash (maqsadli)
      setWeek(weeks[0].week)
    }
  }, [weeks, week])

  const subjectName = (sid: string) => subjects.find((s) => s.id === sid)?.name ?? ''
  const teacherName = (tid: string) => teachers.find((t) => t.id === tid)?.fullName ?? ''

  const assignedTemplateId = assignments.find((a) => a.week === week)?.templateId ?? null
  const template = templates.find((t) => t.id === assignedTemplateId) ?? null
  const lessons = template?.lessons ?? []
  /** Bir katakdagi BARCHA darslar (butun sinf 1 ta, bo'lingan 2 ta). */
  const lessonsAt = (day: number, period: number) =>
    lessons.filter((l) => l.day === day && l.period === period)

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Sinf dars jadvali</h1>
        <p className="text-sm text-slate-400">
          Sinf, chorak va haftani tanlang — shu hafta jadvali ko'rsatiladi
        </p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : classes.length === 0 ? (
        <Card>
          <p className="py-8 text-center text-sm text-slate-400">Sinflar yo'q</p>
        </Card>
      ) : (
        <>
          {/* Tanlovlar paneli */}
          <Card className="flex flex-wrap items-center gap-3 p-4">
            <select
              value={classId}
              onChange={(e) => setClassId(e.target.value)}
              className={cn(control, 'min-w-[160px]')}
            >
              {classes.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>

            <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
              {quarters.map((q) => (
                <button
                  key={q}
                  type="button"
                  onClick={() => setQuarter(q)}
                  className={cn(
                    'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                    q === quarter
                      ? 'bg-white text-brand-700 shadow-sm'
                      : 'text-slate-500 hover:text-slate-700',
                  )}
                >
                  {q}-chorak
                </button>
              ))}
            </div>

            <select
              value={week}
              onChange={(e) => setWeek(Number(e.target.value))}
              className={control}
              disabled={weeks.length === 0}
            >
              {weeks.map((w) => (
                <option key={w.week} value={w.week}>
                  {w.week}-hafta ({formatDate(w.startISO)} – {formatDate(w.endISO)})
                </option>
              ))}
              {weeks.length === 0 && <option>Hafta yo'q</option>}
            </select>
          </Card>

          {weeks.length === 0 ? (
            <Card>
              <p className="py-8 text-center text-sm text-slate-400">
                Bu chorak uchun sanalar kiritilmagan.{' '}
                <Link to="/admin/settings/quarters" className="text-brand-600 hover:underline">
                  Choraklar sozlamasiga o'ting
                </Link>
              </p>
            </Card>
          ) : (
            <Card className="p-0">
              <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 p-4">
                <h2 className="font-semibold text-slate-800">
                  {classes.find((c) => c.id === classId)?.name} — {week}-hafta
                </h2>
                {template ? (
                  <span className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">
                    {template.name}
                  </span>
                ) : (
                  <span className="text-sm text-slate-400">Jadval biriktirilmagan</span>
                )}
              </div>

              {dataLoading ? (
                <Loader label="Yuklanmoqda..." />
              ) : !template ? (
                <p className="p-8 text-center text-sm text-slate-400">
                  Bu haftaga jadval biriktirilmagan.{' '}
                  <Link
                    to={`/admin/schedule/manage/${classId}`}
                    className="text-brand-600 hover:underline"
                  >
                    Dars jadvali yaratish
                  </Link>
                </p>
              ) : (
                <div className="overflow-x-auto p-4">
                  <table className="w-full border-separate border-spacing-0 text-sm">
                    <thead>
                      <tr className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                        <th className="w-10 px-2 py-2 text-center">№</th>
                        {weekDays.map((d) => (
                          <th key={d} className="min-w-[120px] px-2 py-2 text-left font-medium">
                            {d}
                          </th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {schedulePeriods.map((period) => (
                        <tr key={period}>
                          <td className="px-2 py-1.5 text-center align-top">
                            <div className="mt-1.5 inline-flex h-6 w-6 items-center justify-center rounded-md bg-slate-100 text-xs font-semibold text-slate-500">
                              {period}
                            </div>
                          </td>
                          {weekDays.map((_, day) => {
                            const items = lessonsAt(day, period)
                            const split = items.length > 1 || (items.length === 1 && (items[0].subGroup ?? 0) > 0)
                            return (
                              <td key={day} className="px-1 py-1 align-top">
                                <div
                                  className={
                                    items.length > 0
                                      ? 'min-h-[52px] rounded-lg border border-brand-100 bg-brand-50 p-2'
                                      : 'min-h-[52px] rounded-lg border border-dashed border-slate-200'
                                  }
                                >
                                  {items.length > 0 && (split ? (
                                    <div className="space-y-1">
                                      {items
                                        .slice()
                                        .sort((a, b) => (a.subGroup ?? 0) - (b.subGroup ?? 0))
                                        .map((l, i) => (
                                          <div key={i} className="flex flex-col">
                                            <span
                                              className={cn(
                                                'inline-block w-fit rounded px-1 text-[10px] font-semibold',
                                                l.subGroup === 1
                                                  ? 'bg-sky-200 text-sky-800'
                                                  : 'bg-violet-200 text-violet-800',
                                              )}
                                            >
                                              G{l.subGroup}
                                            </span>
                                            <span className="text-xs font-medium text-slate-800">
                                              {subjectName(l.subjectId) || '—'}
                                            </span>
                                            {l.teacherId && (
                                              <span className="text-[11px] text-slate-500">
                                                {teacherName(l.teacherId)}
                                              </span>
                                            )}
                                          </div>
                                        ))}
                                    </div>
                                  ) : (
                                    <>
                                      <p className="text-sm font-medium text-slate-800">
                                        {subjectName(items[0].subjectId) || '—'}
                                      </p>
                                      {items[0].teacherId && (
                                        <p className="mt-0.5 text-xs text-slate-500">
                                          {teacherName(items[0].teacherId)}
                                        </p>
                                      )}
                                    </>
                                  ))}
                                </div>
                              </td>
                            )
                          })}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          )}
        </>
      )}
    </div>
  )
}
