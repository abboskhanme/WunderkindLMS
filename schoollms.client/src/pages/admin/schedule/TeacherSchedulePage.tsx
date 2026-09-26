import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import type {
  LessonOwnerKind,
  ScheduleTemplate,
  SchoolSettings,
  Subject,
  Teacher,
  WeekAssignment,
} from '@/types'
import { getClasses } from '@/api/services/classes'
import { getLessonOwners } from '@/api/services/lessonOwners'
import { getTeachers } from '@/api/services/teachers'
import { getSubjects } from '@/api/services/subjects'
import { getSettings } from '@/api/services/settings'
import { getTemplates } from '@/api/services/scheduleTemplates'
import { getWeekAssignments } from '@/api/services/weekAssignments'
import { getHolidays } from '@/api/services/holidays'
import { weekDays } from '@/config/constants'
import { getQuarterWeeks, getCurrentQuarterAndWeek, mondayOfISO, addDaysISO } from '@/lib/weeks'
import { formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { periodTimeMap, visiblePeriods, type PeriodTime } from '@/components/schedule/periodTimes'
import { PeriodCell, PeriodHead } from '@/components/schedule/PeriodCell'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/* ==========================================================================
   O'QITUVCHI DARS JADVALI — BARCHA O'QITUVCHILAR, BITTA HAFTA

   Mijoz, 2026-09-19: "o'qituvchi jadvallari ham shunday bo'lsin" — ya'ni
   sinf jadvali bilan bir xil: chorak tugmalari ham, o'qituvchi tanlash ham
   yo'q, faqat HAFTA tanlanadi va hamma o'qituvchining jadvali ketma-ket
   chiqadi.

   CHORAK KO'ZDAN YASHIRINDI, YO'QOLMADI: biriktirish baribir chorak
   bo'yicha saqlanadi (`week_assignments.quarter`), shuning uchun haftalar
   ro'yxati barcha choraklardan yig'iladi va har bir yozuv o'z chorogini
   o'zi bilan olib yuradi.

   MA'LUMOT: jadval sinf (yoki o'quv guruhi) bo'yicha saqlanadi, o'qituvchi
   bo'yicha emas. Shuning uchun avval hamma eganing shabloni o'qiladi, keyin
   darslar o'qituvchi bo'yicha guruhlanadi — bitta o'qituvchining bir kunda
   uch sinfda darsi bo'lishi mumkin.
   ========================================================================== */

/** Bitta katakdagi dars (o'qituvchi nuqtai nazaridan: qaysi sinf, qaysi fan) */
interface CellLesson {
  /** Eganing nomi — sinf nomi yoki o'quv guruhi nomi */
  className: string
  subjectName: string
  subGroup: number
  ownerKind: LessonOwnerKind
}

/** Jadval EGASI — sinf yoki o'quv guruhi (§2.1.4). */
interface Owner {
  id: string
  name: string
  kind: LessonOwnerKind
}

/** Ro'yxatdagi bitta hafta — o'z chorogi bilan. */
interface WeekOption {
  quarter: number
  week: number
  startISO: string
  endISO: string
}

/** Bitta eganing jadval ma'lumoti. */
interface OwnerSchedule {
  templates: ScheduleTemplate[]
  assignments: WeekAssignment[]
}

/** grid[period][day] => shu katakdagi darslar */
type Grid = Record<number, Record<number, CellLesson[]>>

export function TeacherSchedulePage() {
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [owners, setOwners] = useState<Owner[]>([])
  const [settings, setSettings] = useState<SchoolSettings | null>(null)
  const [holidays, setHolidays] = useState<Map<string, string>>(new Map())
  const [loading, setLoading] = useState(true)

  /** Tanlangan hafta — "chorak:hafta" (bitta qiymat, ikkalasini ham saqlaydi). */
  const [weekKey, setWeekKey] = useState('')
  const [data, setData] = useState<Map<string, OwnerSchedule>>(new Map())
  const [dataLoading, setDataLoading] = useState(false)

  useEffect(() => {
    Promise.all([
      getTeachers(),
      getSubjects(),
      getClasses(),
      getSettings(),
      getLessonOwners().catch(() => []),
    ])
      .then(([tchs, subs, cls, st, ownerList]) => {
        setTeachers(tchs)
        setSubjects(subs)
        setSettings(st)

        // O'qituvchi jadvali HAMMA sinfni ko'radi (eski 9–11 sinf jadvali ham — maosh uni
        // hisoblaydi), ustiga guruhlar: yo'nalish guruhlari har doim, oddiylari o'chirgich
        // yoqilganda (server qoidasi).
        const list: Owner[] = cls.map((c) => ({ id: c.id, name: c.name, kind: 'class' as const }))
        list.push(
          ...ownerList
            .filter((o) => o.kind === 'group')
            .map((o) => ({ id: o.id, name: o.name, kind: 'group' as const })),
        )
        setOwners(list)

        const { quarter, week } = getCurrentQuarterAndWeek(st.quarters)
        setWeekKey(`${quarter}:${week}`)
      })
      .finally(() => setLoading(false))
    getHolidays().then((hs) => setHolidays(new Map(hs.map((h) => [h.date, h.name]))))
  }, [])

  /** Butun o'quv yilining haftalari — choraklar ketma-ket qo'shilgan. */
  const weekOptions = useMemo<WeekOption[]>(() => {
    if (!settings) return []
    return settings.quarters
      .filter((q) => q.startDate && q.endDate)
      .sort((a, b) => (a.startDate ?? '').localeCompare(b.startDate ?? ''))
      .flatMap((q) =>
        getQuarterWeeks(q.startDate!, q.endDate!).map((w) => ({
          quarter: q.quarter,
          week: w.week,
          startISO: w.startISO,
          endISO: w.endISO,
        })),
      )
  }, [settings])

  const selected = weekOptions.find((w) => `${w.quarter}:${w.week}` === weekKey) ?? null

  useEffect(() => {
    if (weekOptions.length === 0) return
    if (weekOptions.some((w) => `${w.quarter}:${w.week}` === weekKey)) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- tanlangan hafta ro'yxatdan chiqib qolsa, birinchisiga qaytamiz (maqsadli)
    setWeekKey(`${weekOptions[0].quarter}:${weekOptions[0].week}`)
  }, [weekOptions, weekKey])

  /** Har bir eganing shabloni va shu chorakdagi biriktirishlari. */
  useEffect(() => {
    if (owners.length === 0 || !selected) return
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi so'rovdan oldin yuklanish holatini belgilash (maqsadli)
    setDataLoading(true)

    Promise.all(
      owners.map(async (o) => {
        const [templates, assignments] = await Promise.all([
          getTemplates(o.id).catch(() => [] as ScheduleTemplate[]),
          getWeekAssignments(o.id, selected.quarter).catch(() => [] as WeekAssignment[]),
        ])
        return [o.id, { templates, assignments }] as const
      }),
    )
      .then((pairs) => {
        if (active) setData(new Map(pairs))
      })
      .finally(() => {
        if (active) setDataLoading(false)
      })

    return () => {
      active = false
    }
  }, [owners, selected?.quarter, selected])

  /** Dars raqami -> soati ("Sozlamalar → Dars vaqtlari"). */
  const periodTimes = useMemo(() => periodTimeMap(settings), [settings])

  /** Tanlangan haftaning kuni: sana, chorakdan tashqarimi, bayrammi. */
  const monday = selected ? mondayOfISO(selected.startISO) : null
  const dayMeta = (day: number) => {
    if (!selected || !monday) return { date: '', out: true, holiday: null as string | null }
    const date = addDaysISO(monday, day)
    const out = date < selected.startISO || date > selected.endISO
    const holiday = holidays.has(date) ? holidays.get(date) || 'Bayram' : null
    return { date, out, holiday }
  }

  /**
   * O'qituvchi bo'yicha to'rlar. Hamma eganing shu haftaga biriktirilgan
   * shabloni ochiladi va har bir dars o'z o'qituvchisining to'riga tushadi.
   */
  const gridByTeacher = useMemo(() => {
    const result = new Map<string, Grid>()
    if (!selected) return result
    const subjectName = (sid: string) => subjects.find((s) => s.id === sid)?.name ?? ''

    for (const o of owners) {
      const mine = data.get(o.id)
      if (!mine) continue
      const tid = mine.assignments.find((a) => a.week === selected.week)?.templateId
      if (!tid) continue
      const tpl = mine.templates.find((t) => t.id === tid)
      if (!tpl) continue

      for (const l of tpl.lessons ?? []) {
        if (!l.teacherId) continue
        const g = result.get(l.teacherId) ?? {}
        ;(g[l.period] ??= {})
        ;(g[l.period][l.day] ??= []).push({
          className: o.name,
          subjectName: subjectName(l.subjectId),
          subGroup: l.subGroup ?? 0,
          ownerKind: o.kind,
        })
        result.set(l.teacherId, g)
      }
    }
    return result
  }, [owners, data, selected, subjects])

  const countOf = (grid: Grid | undefined) =>
    grid
      ? Object.values(grid).reduce(
          (acc, row) => acc + Object.values(row).reduce((a, arr) => a + arr.length, 0),
          0,
        )
      : 0

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">O'qituvchi dars jadvali</h1>
        <p className="text-sm text-slate-400">
          Haftani tanlang — barcha o'qituvchilarning jadvali ketma-ket ko'rsatiladi
        </p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : teachers.length === 0 ? (
        <Card>
          <p className="py-8 text-center text-sm text-slate-400">O'qituvchilar yo'q</p>
        </Card>
      ) : weekOptions.length === 0 ? (
        <Card>
          <p className="py-8 text-center text-sm text-slate-400">
            Chorak sanalari kiritilmagan.{' '}
            <Link to="/admin/settings/quarters" className="text-brand-600 hover:underline">
              Choraklar sozlamasiga o'ting
            </Link>
          </p>
        </Card>
      ) : (
        <>
          {/* Yagona tanlov — HAFTA. O'qituvchi ham, chorak ham tanlanmaydi. */}
          <Card className="flex flex-wrap items-center gap-3 p-3">
            <span className="text-sm font-medium text-slate-600">Hafta</span>
            <select
              value={weekKey}
              onChange={(e) => setWeekKey(e.target.value)}
              className={cn(control, 'min-w-[260px]')}
            >
              {weekOptions.map((w) => (
                <option key={`${w.quarter}:${w.week}`} value={`${w.quarter}:${w.week}`}>
                  {formatDate(w.startISO)} – {formatDate(w.endISO)}
                </option>
              ))}
            </select>
            <span className="text-sm text-slate-400">
              {teachers.length} ta o'qituvchi · {dataLoading ? 'yuklanmoqda...' : 'tayyor'}
            </span>
          </Card>

          {teachers.map((t) => {
            const grid = gridByTeacher.get(t.id)
            const count = countOf(grid)
            return (
              <Card key={t.id} className="p-0">
                <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 p-4">
                  <h2 className="font-semibold text-slate-800">{t.fullName}</h2>
                  <span className="text-sm text-slate-400">{count} ta dars</span>
                </div>

                {dataLoading && !grid ? (
                  <Loader label="Yuklanmoqda..." />
                ) : count === 0 ? (
                  <p className="p-6 text-center text-sm text-slate-400">
                    Bu hafta uchun dars topilmadi
                  </p>
                ) : (
                  <TeacherWeekGrid grid={grid!} dayMeta={dayMeta} periodTimes={periodTimes} />
                )}
              </Card>
            )
          })}
        </>
      )}
    </div>
  )
}

/* ==========================================================================
   Bitta o'qituvchining hafta to'ri — kun × dars
   ========================================================================== */

interface TeacherWeekGridProps {
  grid: Grid
  dayMeta: (day: number) => { date: string; out: boolean; holiday: string | null }
  periodTimes: Map<number, PeriodTime>
}

function TeacherWeekGrid({ grid, dayMeta, periodTimes }: TeacherWeekGridProps) {
  const periods = visiblePeriods(periodTimes, Object.keys(grid).map(Number))

  return (
    <div className="overflow-x-auto p-4">
      <table className="w-full border-separate border-spacing-0 text-sm">
        <thead>
          <tr className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
            <PeriodHead />
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
                  {m.date && (
                    <div className="text-[10px] font-normal normal-case text-slate-400">
                      {m.date.slice(8)}.{m.date.slice(5, 7)}
                    </div>
                  )}
                </th>
              )
            })}
          </tr>
        </thead>
        <tbody>
          {periods.map((period) => (
            <tr key={period}>
              <PeriodCell period={period} time={periodTimes.get(period)} />
              {weekDays.map((_, day) => {
                const m = dayMeta(day)
                // Chorakdan tashqari (oxirgi hafta) yoki bayram — dars ko'rsatilmaydi.
                if (m.out || m.holiday) {
                  return (
                    <td key={day} className="px-1 py-1 align-top">
                      <div
                        className={cn(
                          'min-h-[52px] rounded-lg border border-dashed',
                          m.holiday
                            ? 'border-red-100 bg-red-50/60'
                            : 'border-slate-100 bg-slate-50/50',
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
                          <p
                            className={cn(
                              'mt-0.5 text-xs',
                              s.ownerKind === 'group' ? 'text-violet-700' : 'text-brand-700',
                            )}
                          >
                            {s.ownerKind === 'group' ? `Guruh: ${s.className}` : s.className}
                          </p>
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
    </div>
  )
}
