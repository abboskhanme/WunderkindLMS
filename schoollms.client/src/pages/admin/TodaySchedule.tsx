import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import type {
  SchoolClass,
  ScheduleTemplate,
  Subject,
  Teacher,
  WeekAssignment,
} from '@/types'
import { getClasses } from '@/api/services/classes'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import { getSettings } from '@/api/services/settings'
import { getTemplates } from '@/api/services/scheduleTemplates'
import { getWeekAssignments } from '@/api/services/weekAssignments'
import { getConductedLessons } from '@/api/services/journal'
import { weekDays } from '@/config/constants'
import { getQuarterWeeks } from '@/lib/weeks'
import { formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'

const todayISO = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
// App hafta kuni: 0=Dushanba ... 5=Shanba, 6=Yakshanba (dars yo'q)
const todayWeekday = () => (new Date().getDay() + 6) % 7

interface Lesson {
  period: number
  subjectName: string
  teacherName: string
  /** Dars o'tildimi (yashil) yoki yo'q (qizil) */
  conducted: boolean
  /** Bo'linish: 0 = butun sinf, 1/2 = guruh */
  subGroup: number
}

/** Bosh sahifada bugungi (joriy hafta kuni) barcha sinflar dars jadvali. */
export function TodaySchedule() {
  const [loading, setLoading] = useState(true)
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [quarter, setQuarter] = useState<number | null>(null)
  const [week, setWeek] = useState<number | null>(null)
  const [templatesByClass, setTemplatesByClass] = useState<Record<string, ScheduleTemplate[]>>({})
  const [assignmentsByClass, setAssignmentsByClass] = useState<Record<string, WeekAssignment[]>>({})
  const [conductedSet, setConductedSet] = useState<Set<string>>(new Set())

  const iso = todayISO()
  const weekday = todayWeekday()

  useEffect(() => {
    let active = true
    Promise.all([
      getClasses(),
      getSubjects(),
      getTeachers(),
      getSettings(),
      getConductedLessons(iso).catch(() => []),
    ])
      .then(async ([cls, subs, tchs, settings, conducted]) => {
        if (!active) return
        setClasses(cls)
        setSubjects(subs)
        setTeachers(tchs)
        setConductedSet(new Set(conducted.map((c) => `${c.classId}|${c.subjectId}|${c.period}|${c.subGroup ?? 0}`)))

        const q = settings.quarters.find(
          (x) => x.startDate && x.endDate && x.startDate <= iso && iso <= x.endDate,
        )
        if (!q) return
        setQuarter(q.quarter)
        const w = getQuarterWeeks(q.startDate, q.endDate).find(
          (x) => x.startISO <= iso && iso <= x.endISO,
        )
        if (!w) return
        setWeek(w.week)

        const [tpls, asg] = await Promise.all([
          Promise.all(cls.map((c) => getTemplates(c.id))),
          Promise.all(cls.map((c) => getWeekAssignments(c.id, q.quarter))),
        ])
        if (!active) return
        const tmap: Record<string, ScheduleTemplate[]> = {}
        const amap: Record<string, WeekAssignment[]> = {}
        cls.forEach((c, i) => {
          tmap[c.id] = tpls[i]
          amap[c.id] = asg[i]
        })
        setTemplatesByClass(tmap)
        setAssignmentsByClass(amap)
      })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => {
      active = false
    }
  }, [iso])

  const subjectName = (id: string) => subjects.find((s) => s.id === id)?.name ?? ''
  const teacherName = (id: string) => teachers.find((t) => t.id === id)?.fullName ?? ''

  const lessonsFor = (classId: string): { assigned: boolean; lessons: Lesson[] } => {
    if (week == null) return { assigned: false, lessons: [] }
    const tid = assignmentsByClass[classId]?.find((a) => a.week === week)?.templateId
    if (!tid) return { assigned: false, lessons: [] }
    const tpl = templatesByClass[classId]?.find((t) => t.id === tid)
    if (!tpl) return { assigned: false, lessons: [] }
    return {
      assigned: true,
      lessons: tpl.lessons
        .filter((l) => l.day === weekday)
        .sort((a, b) => a.period - b.period || (a.subGroup ?? 0) - (b.subGroup ?? 0))
        .map((l) => ({
          period: l.period,
          subjectName: subjectName(l.subjectId),
          teacherName: teacherName(l.teacherId),
          conducted: conductedSet.has(`${classId}|${l.subjectId}|${l.period}|${l.subGroup ?? 0}`),
          subGroup: l.subGroup ?? 0,
        })),
    }
  }

  const dayName = weekday <= 5 ? weekDays[weekday] : 'Yakshanba'

  return (
    <Card>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="font-semibold text-slate-800">Bugungi dars jadvali</h2>
          <p className="text-sm text-slate-400">
            {dayName}, {formatDate(iso)}
          </p>
        </div>
        <Link to="/admin/schedule" className="text-sm font-medium text-brand-600 hover:text-brand-700">
          Barcha jadvallar →
        </Link>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : weekday > 5 ? (
        <p className="py-8 text-center text-sm text-slate-400">Bugun dam olish kuni — dars yo'q</p>
      ) : quarter == null ? (
        <p className="py-8 text-center text-sm text-slate-400">
          Bugun uchun o'quv chorak sanasi topilmadi.{' '}
          <Link to="/admin/settings/quarters" className="text-brand-600 hover:underline">
            Choraklar sozlamasi
          </Link>
        </p>
      ) : classes.length === 0 ? (
        <p className="py-8 text-center text-sm text-slate-400">Sinflar yo'q</p>
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {classes.map((c) => {
            const { assigned, lessons } = lessonsFor(c.id)
            return (
              <div key={c.id} className="rounded-xl border border-slate-200 p-3">
                <div className="mb-2 flex items-center justify-between">
                  <span className="font-semibold text-slate-800">{c.name}</span>
                  <span className="text-xs text-slate-400">{lessons.length} dars</span>
                </div>
                {!assigned ? (
                  <p className="py-2 text-xs text-slate-400">Jadval biriktirilmagan</p>
                ) : lessons.length === 0 ? (
                  <p className="py-2 text-xs text-slate-400">Bugun dars yo'q</p>
                ) : (
                  <ul className="space-y-1">
                    {lessons.map((l, i) => (
                      <li
                        key={i}
                        className={cn(
                          'flex items-center gap-2 rounded-md border px-2 py-1 text-sm',
                          l.conducted
                            ? 'border-emerald-200 bg-emerald-50'
                            : 'border-red-200 bg-red-50',
                        )}
                        title={l.conducted ? "Dars o'tildi" : "Dars o'tilmadi"}
                      >
                        <span
                          className={cn(
                            'flex h-5 w-5 shrink-0 items-center justify-center rounded text-xs font-semibold',
                            l.conducted ? 'bg-emerald-200 text-emerald-800' : 'bg-red-200 text-red-800',
                          )}
                        >
                          {l.period}
                        </span>
                        <span
                          className={cn(
                            'font-medium',
                            l.conducted ? 'text-emerald-800' : 'text-red-800',
                          )}
                        >
                          {l.subjectName || '—'}
                        </span>
                        {l.subGroup > 0 && (
                          <span
                            className={cn(
                              'rounded px-1 text-[10px] font-semibold',
                              l.subGroup === 1
                                ? 'bg-sky-200 text-sky-800'
                                : 'bg-violet-200 text-violet-800',
                            )}
                          >
                            G{l.subGroup}
                          </span>
                        )}
                        {l.teacherName && (
                          <span className="text-xs text-slate-500">· {l.teacherName}</span>
                        )}
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            )
          })}
        </div>
      )}
    </Card>
  )
}
