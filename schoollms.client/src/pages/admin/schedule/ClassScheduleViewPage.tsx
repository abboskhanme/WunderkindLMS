import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import type {
  LessonOwnerKind,
  ScheduleLesson,
  ScheduleTemplate,
  SchoolSettings,
  Subject,
  Teacher,
  WeekAssignment,
} from '@/types'
import { getClasses } from '@/api/services/classes'
import { getGroups } from '@/api/services/groups'
import { getGroupLessonsSwitch } from '@/api/services/groupLessons'
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
   DARS JADVALI — BARCHA SINFLAR, BITTA HAFTA

   Mijoz, 2026-09-19: "dars jadvali choraklar uchun kerakmas, sinf bo'yicha
   ham tanlanmasin yani barcha sinflarniki oydana tagma tag bo'lib chiqsin."

   Shuning uchun ekranda BITTA tanlov qoldi — HAFTA. Sinf ro'yxati va chorak
   tugmalari olib tashlandi: sahifa ochilganda joriy hafta tanlanadi va
   maktabning hamma sinfi ketma-ket, har biri o'z jadval to'ri bilan chiqadi.

   CHORAK YO'QOLMADI, KO'ZDAN YASHIRINDI: hafta baribir chorak ichida
   yashaydi (jadval haftaga chorak bo'yicha biriktiriladi —
   `week_assignments.quarter`). Shuning uchun haftalar ro'yxati BARCHA
   choraklardan yig'iladi va har bir yozuv o'z chorogini o'zi bilan olib
   yuradi; foydalanuvchi esa faqat sanalarni ko'radi.

   MA'LUMOT: shablonlar sinf bo'yicha olinadi, shuning uchun har bir sinf
   uchun bittadan so'rov ketadi (`Promise.all`). Maktabda o'nlab sinf bor,
   yuzlab emas — bitta umumiy endpoint ochishdan ko'ra, mavjudini
   ishlatgan ma'qul (yangi endpoint = yangi kontrakt = yangi test).
   ========================================================================== */

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

/** Bitta sinfning jadval ma'lumoti. */
interface OwnerSchedule {
  templates: ScheduleTemplate[]
  assignments: WeekAssignment[]
}

export function ClassScheduleViewPage() {
  const [owners, setOwners] = useState<Owner[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [settings, setSettings] = useState<SchoolSettings | null>(null)
  const [holidays, setHolidays] = useState<Map<string, string>>(new Map())
  const [loading, setLoading] = useState(true)

  /** Tanlangan hafta — "chorak:hafta" (bitta qiymat, ikkalasini ham saqlaydi). */
  const [weekKey, setWeekKey] = useState('')
  const [data, setData] = useState<Map<string, OwnerSchedule>>(new Map())
  const [dataLoading, setDataLoading] = useState(false)

  useEffect(() => {
    Promise.all([
      getClasses(),
      getSubjects(),
      getTeachers(),
      getSettings(),
      getGroupLessonsSwitch().catch(() => null),
    ])
      .then(async ([cls, subs, tchs, st, flag]) => {
        setSubjects(subs)
        setTeachers(tchs)
        setSettings(st)

        const list: Owner[] = cls.map((c) => ({ id: c.id, name: c.name, kind: 'class' as const }))
        if (flag?.enabled) {
          const groups = await getGroups().catch(() => [])
          list.push(...groups.map((g) => ({ id: g.id, name: g.name, kind: 'group' as const })))
        }
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

  /**
   * Har bir sinfning shabloni va shu chorakdagi biriktirishlari.
   * Chorak o'zgarsa qayta o'qiladi — biriktirish chorak bo'yicha saqlanadi.
   */
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

  const subjectName = (sid: string) => subjects.find((s) => s.id === sid)?.name ?? ''
  const teacherName = (tid: string) => teachers.find((t) => t.id === tid)?.fullName ?? ''

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

  const templateFor = (ownerId: string): ScheduleTemplate | null => {
    if (!selected) return null
    const mine = data.get(ownerId)
    if (!mine) return null
    const id = mine.assignments.find((a) => a.week === selected.week)?.templateId ?? null
    return mine.templates.find((t) => t.id === id) ?? null
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Dars jadvali</h1>
        <p className="text-sm text-slate-400">
          Haftani tanlang — barcha sinflarning jadvali ketma-ket ko'rsatiladi
        </p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : owners.length === 0 ? (
        <Card>
          <p className="py-8 text-center text-sm text-slate-400">Sinflar yo'q</p>
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
          {/* Yagona tanlov — HAFTA. Sinf ham, chorak ham tanlanmaydi. */}
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
              {owners.length} ta sinf · {dataLoading ? 'yuklanmoqda...' : 'tayyor'}
            </span>
          </Card>

          {owners.map((owner) => {
            const template = templateFor(owner.id)
            return (
              <Card key={owner.id} className="p-0">
                <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 p-4">
                  <h2 className="font-semibold text-slate-800">
                    {owner.kind === 'group' ? `Guruh: ${owner.name}` : owner.name}
                  </h2>
                  {template ? (
                    <span className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">
                      {template.name}
                    </span>
                  ) : (
                    <span className="text-sm text-slate-400">Jadval biriktirilmagan</span>
                  )}
                </div>

                {dataLoading && !template ? (
                  <Loader label="Yuklanmoqda..." />
                ) : !template ? (
                  <p className="p-6 text-center text-sm text-slate-400">
                    Bu haftaga jadval biriktirilmagan.{' '}
                    <Link
                      to={`/admin/schedule/manage/${owner.id}`}
                      className="text-brand-600 hover:underline"
                    >
                      Dars jadvali yaratish
                    </Link>
                  </p>
                ) : (
                  <WeekGrid
                    lessons={template.lessons ?? []}
                    dayMeta={dayMeta}
                    subjectName={subjectName}
                    teacherName={teacherName}
                    periodTimes={periodTimes}
                  />
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
   Bitta sinfning hafta to'ri — kun × dars
   ========================================================================== */

interface WeekGridProps {
  lessons: ScheduleLesson[]
  dayMeta: (day: number) => { date: string; out: boolean; holiday: string | null }
  subjectName: (id: string) => string
  teacherName: (id: string) => string
  periodTimes: Map<number, PeriodTime>
}

function WeekGrid({ lessons, dayMeta, subjectName, teacherName, periodTimes }: WeekGridProps) {
  /** Bir katakdagi BARCHA darslar (butun sinf 1 ta, bo'lingan 2 ta). */
  const lessonsAt = (day: number, period: number) =>
    lessons.filter((l) => l.day === day && l.period === period)

  const periods = visiblePeriods(periodTimes, lessons.map((l) => l.period))

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
                      {items.length > 0 &&
                        (split ? (
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
  )
}
