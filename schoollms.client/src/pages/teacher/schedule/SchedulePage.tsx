import { useEffect, useMemo, useState } from 'react'
import type { PortalMeta, TeacherLesson } from '@/types'
import { getTeacherMeta, getTeacherSchedule, getTeacherHolidays } from '@/api/services/teacher'
import { quarters, weekDays } from '@/config/constants'
import { getQuarterWeeks, mondayOfISO, addDaysISO } from '@/lib/weeks'
import { formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

export function TeacherSchedulePage() {
  const [meta, setMeta] = useState<PortalMeta | null>(null)
  const [loading, setLoading] = useState(true)
  const [quarter, setQuarter] = useState(1)
  const [week, setWeek] = useState(1)
  const [lessons, setLessons] = useState<TeacherLesson[]>([])
  const [holidays, setHolidays] = useState<Map<string, string>>(new Map())
  const [dataLoading, setDataLoading] = useState(false)

  useEffect(() => {
    getTeacherMeta()
      .then((m) => {
        setMeta(m)
        if (m) {
          setQuarter(m.currentQuarter || 1)
          setWeek(m.currentWeek || 1)
        }
      })
      .finally(() => setLoading(false))
    getTeacherHolidays().then((hs) => setHolidays(new Map(hs.map((h) => [h.date, h.name]))))
  }, [])

  const weeks = useMemo(() => {
    if (!meta) return []
    const q = meta.quarters.find((x) => x.quarter === quarter)
    return q && q.startDate && q.endDate ? getQuarterWeeks(q.startDate, q.endDate) : []
  }, [meta, quarter])

  useEffect(() => {
    if (weeks.length > 0 && !weeks.some((w) => w.week === week)) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- haftani amaldagi diapazonga moslash (maqsadli)
      setWeek(weeks[0].week)
    }
  }, [weeks, week])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- chorak/hafta o'zgarganda jadvalni qayta yuklash (maqsadli)
    setDataLoading(true)
    getTeacherSchedule(quarter, week)
      .then(setLessons)
      .finally(() => setDataLoading(false))
  }, [quarter, week])

  // Dars raqamlari (dars vaqtlaridan; bo'lmasa darslardan).
  const periods = useMemo(() => {
    const set = new Set<number>()
    meta?.lessonTimes.forEach((t) => set.add(t.period))
    lessons.forEach((l) => set.add(l.period))
    return [...set].sort((a, b) => a - b)
  }, [meta, lessons])

  const timeFor = (period: number) => meta?.lessonTimes.find((t) => t.period === period)

  // Tanlangan haftaning har kuni: sana, chorakdan tashqarimi (clamp), bayrammi.
  const selectedWeek = weeks.find((w) => w.week === week) ?? null
  const monday = selectedWeek ? mondayOfISO(selectedWeek.startISO) : null
  const dayMeta = (day: number) => {
    if (!selectedWeek || !monday) return { date: '', out: true, holiday: null as string | null }
    const date = addDaysISO(monday, day)
    const out = date < selectedWeek.startISO || date > selectedWeek.endISO
    const holiday = holidays.has(date) ? holidays.get(date) || 'Bayram' : null
    return { date, out, holiday }
  }

  // grid[period][day] => darslar
  const grid = useMemo(() => {
    const g: Record<number, Record<number, TeacherLesson[]>> = {}
    for (const l of lessons) {
      ;(g[l.period] ??= {})
      ;(g[l.period][l.day] ??= []).push(l)
    }
    return g
  }, [lessons])

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Dars jadvali</h1>
        <p className="text-sm text-slate-400">Chorak va haftani tanlang — darslaringiz ko'rinadi</p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          <Card className="flex flex-wrap items-center gap-3">
            <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
              {quarters.map((q) => (
                <button
                  key={q}
                  type="button"
                  onClick={() => setQuarter(q)}
                  className={cn(
                    'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                    q === quarter ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
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
                Bu chorak uchun sanalar kiritilmagan.
              </p>
            </Card>
          ) : dataLoading ? (
            <Loader label="Yuklanmoqda..." />
          ) : (
            <Card className="p-0">
              <div className="overflow-x-auto p-4">
                <table className="w-full border-separate border-spacing-0 text-sm">
                  <thead>
                    <tr className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                      <th className="w-16 px-2 py-2 text-center">Dars</th>
                      {weekDays.map((d, day) => {
                        const m = dayMeta(day)
                        return (
                          <th
                            key={d}
                            className={cn('min-w-[120px] px-2 py-2 text-left font-medium', m.out && 'opacity-40')}
                          >
                            <div className="flex items-center gap-1.5">
                              <span>{d}</span>
                              {m.holiday && (
                                <span
                                  title={m.holiday}
                                  className="rounded bg-red-100 px-1 text-[9px] font-semibold normal-case text-red-600"
                                >
                                  Bayram
                                </span>
                              )}
                            </div>
                          </th>
                        )
                      })}
                    </tr>
                  </thead>
                  <tbody>
                    {periods.map((period) => {
                      const t = timeFor(period)
                      return (
                        <tr key={period}>
                          <td className="px-2 py-1.5 text-center align-top">
                            <div className="mt-1.5 inline-flex h-6 w-6 items-center justify-center rounded-md bg-slate-100 text-xs font-semibold text-slate-500">
                              {period}
                            </div>
                            {t && (
                              <div className="mt-1 text-[10px] text-slate-400">
                                {t.startTime}
                                <br />
                                {t.endTime}
                              </div>
                            )}
                          </td>
                          {weekDays.map((_, day) => {
                            const m = dayMeta(day)
                            if (m.out || m.holiday) {
                              return (
                                <td key={day} className="px-1 py-1 align-top">
                                  <div
                                    className={cn(
                                      'min-h-[52px] rounded-lg border border-dashed',
                                      m.holiday ? 'border-red-100 bg-red-50/60' : 'border-slate-100 bg-slate-50/50',
                                    )}
                                  />
                                </td>
                              )
                            }
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
                                  {cell.map((l, i) => (
                                    <div key={i}>
                                      <p className="text-sm font-medium text-slate-800">
                                        {l.subjectName || '—'}
                                      </p>
                                      <p className="mt-0.5 text-xs text-brand-700">{l.className}</p>
                                    </div>
                                  ))}
                                </div>
                              </td>
                            )
                          })}
                        </tr>
                      )
                    })}
                    {periods.length === 0 && (
                      <tr>
                        <td colSpan={weekDays.length + 1} className="px-4 py-10 text-center text-slate-400">
                          Bu hafta uchun dars topilmadi
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            </Card>
          )}
        </>
      )}
    </div>
  )
}
