import { useCallback, useEffect, useMemo, useState } from 'react'
import { CalendarCheck } from 'lucide-react'
import type { StudentAttendanceRange } from '@/api/services/studentProfile'
import { getStudentAttendanceRange } from '@/api/services/studentProfile'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDate } from '@/lib/utils'
import { ProfileEmpty, ProfileError, ProfileSection } from './ProfileUi'

/**
 * Kartochkaning "Davomat" tab'i — docs/modules/students-parity.md §2.3
 * (S-10: *Davomat* — oy kalendari + oraliq hisoboti, fan kesimida).
 *
 * FOIZNING TA'RIFI SERVERDA. Maxraj — jurnalda "o'tildi" belgisi qo'yilgan
 * darslar; "kech keldi" yo'qlik sifatida sanalmaydi. Shu ta'rif shaxsiy
 * daftardagi bilan bir xil (`StudentProfileBuilder`), ya'ni ikki ekran bir xil
 * raqamni ko'rsatadi.
 *
 * "SABABLI / SABABSIZ" USTUNI YO'Q: `absence_reasons` jadvalida bunday ustun
 * yo'q, shuning uchun yo'qliklar SABAB kesimida beriladi.
 */

const uzMonths = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

const dayNames = ['Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh', 'Ya']

/** Oyning birinchi va oxirgi kuni ("yyyy-MM-dd"). */
function monthRange(month: string): { from: string; to: string } {
  const [y, m] = month.split('-').map(Number)
  const last = new Date(Date.UTC(y, m, 0)).getUTCDate()
  return { from: `${month}-01`, to: `${month}-${String(last).padStart(2, '0')}` }
}

function thisMonth(): string {
  return new Date().toISOString().slice(0, 7)
}

export function AttendanceTab({ studentId }: { studentId: string }) {
  const [mode, setMode] = useState<'month' | 'range'>('month')
  const [month, setMonth] = useState(thisMonth)
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [data, setData] = useState<StudentAttendanceRange | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(() => {
    const window = mode === 'month' ? monthRange(month) : { from, to }
    setLoading(true)
    setError(null)
    getStudentAttendanceRange(studentId, window.from || undefined, window.to || undefined)
      .then(setData)
      .catch((e) =>
        setError(
          (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
            "Davomat hisobotini olib bo'lmadi",
        ),
      )
      .finally(() => setLoading(false))
  }, [studentId, mode, month, from, to])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- tab ochilganda va oraliq o'zgarganda hisobot yuklanadi (maqsadli)
    load()
  }, [load])

  /** Kun → holat. Kalendar faqat "oy" rejimida chiziladi. */
  const byDate = useMemo(() => {
    const map = new Map<string, { planned: number; absent: number; late: number }>()
    data?.days.forEach((d) => map.set(d.date, d))
    return map
  }, [data])

  /** Oyning kunlari, birinchi hafta dushanbadan boshlanadigan qilib to'ldirilgan. */
  const cells = useMemo(() => {
    const [y, m] = month.split('-').map(Number)
    if (!y || !m) return []
    const first = new Date(Date.UTC(y, m - 1, 1))
    const lead = (first.getUTCDay() + 6) % 7 // Dushanba = 0
    const total = new Date(Date.UTC(y, m, 0)).getUTCDate()
    const list: (string | null)[] = Array.from({ length: lead }, () => null)
    for (let d = 1; d <= total; d += 1)
      list.push(`${month}-${String(d).padStart(2, '0')}`)
    return list
  }, [month])

  const picker = (
    <div className="flex flex-wrap items-center gap-2">
      <div className="flex gap-1 rounded-lg bg-slate-100 p-1">
        {(['month', 'range'] as const).map((value) => (
          <button
            key={value}
            type="button"
            onClick={() => setMode(value)}
            className={cn(
              'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
              mode === value ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
            )}
          >
            {value === 'month' ? 'Oy' : 'Oraliq'}
          </button>
        ))}
      </div>
      {mode === 'month' ? (
        <input
          type="month"
          value={month}
          onChange={(e) => setMonth(e.target.value)}
          className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400"
        />
      ) : (
        <>
          <input
            type="date"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
            className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400"
          />
          <span className="text-sm text-slate-400">—</span>
          <input
            type="date"
            value={to}
            onChange={(e) => setTo(e.target.value)}
            className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400"
          />
        </>
      )}
    </div>
  )

  return (
    <div className="space-y-6">
      <ProfileSection title="Davomat" icon={CalendarCheck} action={picker}>
        {error ? (
          <ProfileError message={error} />
        ) : loading && !data ? (
          <Loader label="Yuklanmoqda..." />
        ) : !data ? null : (
          <>
            <p className="mb-4 text-xs text-slate-400">
              {formatDate(data.from)} — {formatDate(data.to)}
            </p>

            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
              <Tile label="O'tilgan dars" value={data.planned} />
              <Tile label="Qatnashgan" value={data.attended} tone="emerald" />
              <Tile label="Qoldirgan" value={data.absent} tone="red" />
              <Tile label="Kech kelgan" value={data.late} tone="amber" />
              <Tile label="Davomat" value={`${data.pct}%`} tone="brand" />
            </div>

            {mode === 'month' && (
              <div className="mt-6">
                <p className="mb-2 text-sm font-medium text-slate-600">
                  {uzMonths[Number(month.slice(5, 7)) - 1] ?? month} {month.slice(0, 4)}
                </p>
                <div className="grid grid-cols-7 gap-1 text-center">
                  {dayNames.map((d) => (
                    <div key={d} className="py-1 text-[11px] font-medium text-slate-400">
                      {d}
                    </div>
                  ))}
                  {cells.map((date, index) => {
                    if (!date) return <div key={`pad-${index}`} />
                    const day = byDate.get(date)
                    const tone = !day
                      ? 'bg-slate-50 text-slate-300'
                      : day.absent > 0
                        ? 'bg-red-50 text-red-600'
                        : day.late > 0
                          ? 'bg-amber-50 text-amber-700'
                          : 'bg-emerald-50 text-emerald-700'
                    return (
                      <div
                        key={date}
                        title={
                          day
                            ? `${day.planned} dars · ${day.absent} qoldirdi · ${day.late} kech keldi`
                            : 'Dars yo‘q'
                        }
                        className={cn('rounded-lg py-2 text-sm font-medium', tone)}
                      >
                        {Number(date.slice(8, 10))}
                      </div>
                    )
                  })}
                </div>
              </div>
            )}
          </>
        )}
      </ProfileSection>

      {data && !error && (
        <ProfileSection title="Fan kesimida" icon={CalendarCheck}>
          {data.subjects.length === 0 ? (
            <ProfileEmpty>Bu oraliqda o'tilgan dars yo'q</ProfileEmpty>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-3 py-2">Fan</th>
                    <th className="px-3 py-2 text-center">O'tilgan</th>
                    <th className="px-3 py-2 text-center">Qatnashgan</th>
                    <th className="px-3 py-2 text-center">Qoldirgan</th>
                    <th className="px-3 py-2 text-center">Kech</th>
                    <th className="px-3 py-2 text-center">Davomat</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {data.subjects.map((s) => (
                    <tr key={s.subjectId} className="hover:bg-slate-50/60">
                      <td className="px-3 py-2 font-medium text-slate-700">{s.subjectName}</td>
                      <td className="px-3 py-2 text-center text-slate-600">{s.planned}</td>
                      <td className="px-3 py-2 text-center text-emerald-600">{s.attended}</td>
                      <td className="px-3 py-2 text-center text-red-600">{s.absent}</td>
                      <td className="px-3 py-2 text-center text-amber-600">{s.late}</td>
                      <td className="px-3 py-2 text-center font-semibold text-slate-800">{s.pct}%</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {data.reasons.length > 0 && (
            <div className="mt-4 flex flex-wrap gap-2 border-t border-slate-100 pt-4">
              {data.reasons.map((r) => (
                <span
                  key={r.reasonId}
                  className={cn(
                    'inline-flex items-center gap-1 rounded-md px-2 py-1 text-sm font-medium',
                    r.isLate ? 'bg-amber-50 text-amber-700' : 'bg-red-50 text-red-600',
                  )}
                >
                  {r.name} <span className="font-semibold">×{r.count}</span>
                </span>
              ))}
            </div>
          )}
        </ProfileSection>
      )}
    </div>
  )
}

const tones: Record<string, string> = {
  slate: 'text-slate-800',
  emerald: 'text-emerald-600',
  red: 'text-red-600',
  amber: 'text-amber-600',
  brand: 'text-brand-600',
}

function Tile({
  label,
  value,
  tone = 'slate',
}: {
  label: string
  value: string | number
  tone?: keyof typeof tones
}) {
  return (
    <div className="rounded-xl border border-slate-100 bg-slate-50/40 px-4 py-3">
      <p className="text-xs text-slate-400">{label}</p>
      <p className={cn('text-xl font-semibold', tones[tone])}>{value}</p>
    </div>
  )
}
