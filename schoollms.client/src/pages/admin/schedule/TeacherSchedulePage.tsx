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

/** Bitta katakdagi dars (o'qituvchi nuqtai nazaridan: qaysi sinf, qaysi fan) */
interface CellLesson {
  className: string
  subjectName: string
  subGroup: number
}

/**
 * O'qituvchining dars jadvali: o'qituvchi + chorak + hafta tanlanadi va shu hafta uchun
 * barcha sinflarning biriktirilgan jadvalidan o'qituvchining darslari yig'ib ko'rsatiladi.
 */
export function TeacherSchedulePage() {
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [settings, setSettings] = useState<SchoolSettings | null>(null)
  const [templatesByClass, setTemplatesByClass] = useState<Record<string, ScheduleTemplate[]>>({})
  const [loading, setLoading] = useState(true)

  const [teacherId, setTeacherId] = useState('')
  const [quarter, setQuarter] = useState(1)
  const [week, setWeek] = useState(1)

  const [assignmentsByClass, setAssignmentsByClass] = useState<Record<string, WeekAssignment[]>>({})
  const [assignLoading, setAssignLoading] = useState(false)

  // Boshlang'ich yuklash: o'qituvchilar, fanlar, sinflar, sozlamalar + har sinf jadvallari
  useEffect(() => {
    Promise.all([getTeachers(), getSubjects(), getClasses(), getSettings()])
      .then(async ([tchs, subs, cls, st]) => {
        setTeachers(tchs)
        setSubjects(subs)
        setClasses(cls)
        setSettings(st)
        setTeacherId(tchs[0]?.id ?? '')
        const { quarter: q, week: w } = getCurrentQuarterAndWeek(st.quarters)
        setQuarter(q)
        setWeek(w)
        const tpls = await Promise.all(cls.map((c) => getTemplates(c.id)))
        const map: Record<string, ScheduleTemplate[]> = {}
        cls.forEach((c, i) => {
          map[c.id] = tpls[i]
        })
        setTemplatesByClass(map)
      })
      .finally(() => setLoading(false))
  }, [])

  // Chorak o'zgarsa — barcha sinflar uchun shu chorak hafta-biriktirishlarini yuklaymiz
  useEffect(() => {
    if (classes.length === 0) return
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi so'rovdan oldin holatni belgilash (maqsadli)
    setAssignLoading(true)
    Promise.all(classes.map((c) => getWeekAssignments(c.id, quarter)))
      .then((res) => {
        if (!active) return
        const map: Record<string, WeekAssignment[]> = {}
        classes.forEach((c, i) => {
          map[c.id] = res[i]
        })
        setAssignmentsByClass(map)
      })
      .finally(() => {
        if (active) setAssignLoading(false)
      })
    return () => {
      active = false
    }
  }, [classes, quarter])

  const weeks = useMemo(() => {
    if (!settings) return []
    const q = settings.quarters.find((x) => x.quarter === quarter)
    return q && q.startDate && q.endDate ? getQuarterWeeks(q.startDate, q.endDate) : []
  }, [settings, quarter])

  // Tanlangan hafta shu chorakda mavjud bo'lmasa — birinchisiga o'tamiz
  useEffect(() => {
    if (weeks.length > 0 && !weeks.some((w) => w.week === week)) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- haftani amaldagi diapazonga moslash (maqsadli)
      setWeek(weeks[0].week)
    }
  }, [weeks, week])

  const subjectName = (sid: string) => subjects.find((s) => s.id === sid)?.name ?? ''

  // grid[period][day] => o'qituvchining shu katakdagi darslari
  const grid = useMemo(() => {
    const g: Record<number, Record<number, CellLesson[]>> = {}
    if (!teacherId) return g
    for (const c of classes) {
      const tid = assignmentsByClass[c.id]?.find((a) => a.week === week)?.templateId
      if (!tid) continue
      const tpl = templatesByClass[c.id]?.find((t) => t.id === tid)
      if (!tpl) continue
      for (const l of tpl.lessons) {
        if (l.teacherId !== teacherId) continue
        ;(g[l.period] ??= {})
        ;(g[l.period][l.day] ??= []).push({
          className: c.name,
          subjectName: subjectName(l.subjectId),
          subGroup: l.subGroup ?? 0,
        })
      }
    }
    return g
    // eslint-disable-next-line react-hooks/exhaustive-deps -- subjectName subjects'ga bog'liq
  }, [teacherId, classes, assignmentsByClass, templatesByClass, week, subjects])

  const lessonCount = useMemo(
    () =>
      Object.values(grid).reduce(
        (acc, row) => acc + Object.values(row).reduce((a, arr) => a + arr.length, 0),
        0,
      ),
    [grid],
  )

  const selectedWeek = weeks.find((w) => w.week === week)

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">O'qituvchi dars jadvali</h1>
        <p className="text-sm text-slate-400">
          O'qituvchi, chorak va haftani tanlang — shu hafta darslari ko'rsatiladi
        </p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : teachers.length === 0 ? (
        <Card>
          <p className="py-8 text-center text-sm text-slate-400">O'qituvchilar yo'q</p>
        </Card>
      ) : (
        <>
          {/* Tanlovlar paneli */}
          <Card className="flex flex-wrap items-center gap-3 p-4">
            <select
              value={teacherId}
              onChange={(e) => setTeacherId(e.target.value)}
              className={cn(control, 'min-w-[200px]')}
            >
              {teachers.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.fullName}
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
                  {teachers.find((t) => t.id === teacherId)?.fullName}
                  {selectedWeek ? ` — ${selectedWeek.week}-hafta` : ''}
                </h2>
                <span className="text-sm text-slate-400">{lessonCount} ta dars</span>
              </div>

              {assignLoading ? (
                <Loader label="Yuklanmoqda..." />
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
                            const cell = grid[period]?.[day] ?? []
                            return (
                              <td key={day} className="px-1 py-1 align-top">
                                <div
                                  className={
                                    cell.length > 0
                                      ? 'min-h-[52px] space-y-1 rounded-lg border border-brand-100 bg-brand-50 p-2'
                                      : 'min-h-[52px] rounded-lg border border-dashed border-slate-200'
                                  }
                                >
                                  {cell.map((s, i) => (
                                    <div key={i}>
                                      <div className="flex items-center gap-1">
                                        <p className="text-sm font-medium text-slate-800">
                                          {s.subjectName || '—'}
                                        </p>
                                        {s.subGroup > 0 && (
                                          <span
                                            className={cn(
                                              'rounded px-1 text-[10px] font-semibold',
                                              s.subGroup === 1
                                                ? 'bg-sky-200 text-sky-800'
                                                : 'bg-violet-200 text-violet-800',
                                            )}
                                          >
                                            G{s.subGroup}
                                          </span>
                                        )}
                                      </div>
                                      <p className="mt-0.5 text-xs text-brand-700">{s.className}</p>
                                    </div>
                                  ))}
                                </div>
                              </td>
                            )
                          })}
                        </tr>
                      ))}
                    </tbody>
                  </table>

                  {lessonCount === 0 && (
                    <p className="pt-4 text-center text-sm text-slate-400">
                      Bu hafta uchun dars topilmadi (sinflarga jadval biriktirilganini tekshiring)
                    </p>
                  )}
                </div>
              )}
            </Card>
          )}
        </>
      )}
    </div>
  )
}
