import { useCallback, useEffect, useMemo, useState } from 'react'
import { CalendarDays, ChevronLeft, ChevronRight } from 'lucide-react'
import type { StudentTimetable } from '@/api/services/studentProfile'
import { getStudentTimetable } from '@/api/services/studentProfile'
import { quarters, weekDays } from '@/config/constants'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDate } from '@/lib/utils'
import { ProfileEmpty, ProfileError, ProfileSection } from './ProfileUi'

/**
 * Kartochkaning "Jadval" tab'i — docs/modules/students-parity.md §2.3
 * (S-10: *Jadval* — hafta, `PupilTimetable` dan).
 *
 * Ro'yxatda SINF darslari va o'quvchi a'zo bo'lgan FAOL GURUH darslari birga
 * keladi; guruh darsi yonida guruh nomi yoziladi (§2.3.1 — "group name when
 * class.type === group"). O'chirgich o'chiq bo'lsa guruh darsi umuman
 * bo'lmaydi va jadval bugungi ko'rinishning aynan o'zi bo'lib qoladi.
 *
 * XONA USTUNI YO'Q: dars jadvali qatorida xona bog'lanmagan (`rooms`
 * katalogi bor, lekin darsga ulanmagan) — yo'q ma'lumotni ko'rsatmaymiz.
 */
export function TimetableTab({ studentId }: { studentId: string }) {
  const [data, setData] = useState<StudentTimetable | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  /** null = server joriy chorak/haftani o'zi tanlaydi. */
  const [pick, setPick] = useState<{ quarter: number; week: number } | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    getStudentTimetable(studentId, pick?.quarter, pick?.week)
      .then(setData)
      .catch((e) =>
        setError(
          (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
            "Jadvalni olib bo'lmadi",
        ),
      )
      .finally(() => setLoading(false))
  }, [studentId, pick])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- tab ochilganda va hafta almashganda jadval yuklanadi (maqsadli)
    load()
  }, [load])

  /** Dars raqamlari — faqat darsi BOR qatorlar (bo'sh 10 qator emas). */
  const periods = useMemo(() => {
    const set = new Set<number>(data?.lessons.map((l) => l.period) ?? [])
    return [...set].sort((a, b) => a - b)
  }, [data])

  const step = (delta: number) => {
    if (!data) return
    const next = data.week + delta
    if (next < 1 || (data.weekCount > 0 && next > data.weekCount)) return
    setPick({ quarter: data.quarter, week: next })
  }

  const nav = data && (
    <div className="flex flex-wrap items-center gap-2">
      <select
        value={data.quarter}
        onChange={(e) => setPick({ quarter: Number(e.target.value), week: 1 })}
        className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400"
      >
        {quarters.map((q) => (
          <option key={q} value={q}>
            {q}-chorak
          </option>
        ))}
      </select>
      <div className="flex items-center gap-1">
        <button
          type="button"
          title="Oldingi hafta"
          onClick={() => step(-1)}
          disabled={data.week <= 1}
          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:opacity-40"
        >
          <ChevronLeft className="h-4 w-4" />
        </button>
        <span className="min-w-[5.5rem] text-center text-sm font-medium text-slate-600">
          {data.week}-hafta
        </span>
        <button
          type="button"
          title="Keyingi hafta"
          onClick={() => step(1)}
          disabled={data.weekCount > 0 && data.week >= data.weekCount}
          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:opacity-40"
        >
          <ChevronRight className="h-4 w-4" />
        </button>
      </div>
    </div>
  )

  return (
    <ProfileSection title="Dars jadvali" icon={CalendarDays} action={nav}>
      {error ? (
        <ProfileError message={error} />
      ) : loading && !data ? (
        <Loader label="Yuklanmoqda..." />
      ) : !data ? null : (
        <>
          {data.weekStart && (
            <p className="mb-3 text-xs text-slate-400">
              {formatDate(data.weekStart)} — {data.weekEnd ? formatDate(data.weekEnd) : ''}
            </p>
          )}

          {periods.length === 0 ? (
            <ProfileEmpty>Bu haftaga jadval biriktirilmagan</ProfileEmpty>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[720px] text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="w-16 px-3 py-2 text-center">Dars</th>
                    {weekDays.map((d) => (
                      <th key={d} className="px-3 py-2">{d}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {periods.map((period) => {
                    const time = data.lessons.find((l) => l.period === period)
                    return (
                      <tr key={period} className="align-top">
                        <td className="px-3 py-2 text-center">
                          <span className="font-semibold text-slate-700">{period}</span>
                          {time?.startTime && (
                            <span className="block text-[11px] text-slate-400">
                              {time.startTime}
                            </span>
                          )}
                        </td>
                        {weekDays.map((day, index) => {
                          const cell = data.lessons.filter(
                            (l) => l.day === index && l.period === period,
                          )
                          return (
                            <td key={day} className="px-3 py-2">
                              {cell.length === 0 ? (
                                <span className="text-slate-300">—</span>
                              ) : (
                                cell.map((l) => (
                                  <div
                                    key={`${l.subjectId}-${l.ownerKind}-${l.ownerName ?? ''}`}
                                    className={cn(
                                      'rounded-lg px-2 py-1',
                                      l.ownerKind === 'group' ? 'bg-brand-50' : 'bg-slate-50',
                                    )}
                                  >
                                    <p className="font-medium text-slate-700">{l.subjectName || '—'}</p>
                                    {l.teacherName && (
                                      <p className="text-[11px] text-slate-400">{l.teacherName}</p>
                                    )}
                                    {l.ownerName && (
                                      <p className="text-[11px] font-medium text-brand-600">
                                        {l.ownerName}
                                      </p>
                                    )}
                                    {l.ownerKind !== 'group' && l.subGroup > 0 && (
                                      <p className="text-[11px] text-slate-400">
                                        {l.subGroup}-guruh
                                      </p>
                                    )}
                                  </div>
                                ))
                              )}
                            </td>
                          )
                        })}
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </ProfileSection>
  )
}
