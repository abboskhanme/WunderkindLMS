import { useEffect, useState } from 'react'
import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Users, UserCheck, UserX, HelpCircle, Clock } from 'lucide-react'
import type { SchoolClass } from '@/types'
import { getClasses } from '@/api/services/classes'
import {
  getAttendanceAnalytics,
  type AttendanceAnalytics,
  type AttendanceTally,
} from '@/api/services/attendanceAnalytics'
import { cn, formatDate } from '@/lib/utils'
import { addDaysISO } from '@/lib/weeks'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none focus:border-brand-400'

const pad = (n: number) => String(n).padStart(2, '0')
function todayISO(): string {
  const d = new Date()
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/** Tez tanlanadigan davrlar — zavuch ertalab shu uchtasidan birini bosadi. */
const ranges = [
  { key: 'today', label: 'Bugun', days: 0 },
  { key: 'week', label: '7 kun', days: 6 },
  { key: 'month', label: '30 kun', days: 29 },
] as const

/** Foizni ko'rsatish: server null bersa — ma'lumot yo'q, nol emas. */
const pct = (v: number | null) => (v == null ? '—' : `${v.toFixed(1).replace('.0', '')}%`)

/**
 * Davomat analitikasi (Analitika #5) — zavuch har kuni ertalab ochadigan roll-up ekrani.
 *
 * Bu sahifa "Davomat" bo'limining yonida turadi, alohida "Analitika" mega-bo'limida emas
 * (docs/modules/existing-module-gaps.md §4.2): o'lchanadigan narsaning yonidagi hisobot
 * ko'riladi, alohida bo'limdagisi esa yo'q.
 *
 * <b>"Tekshirilmagan" alohida ustun</b> va hech qachon "keldi"ga qo'shilmaydi — bu ekranning
 * asosiy ma'nosi: 100% davomat ko'rsatib, aslida jurnal ochilmaganini yashirmaslik.
 * Barcha foizlar SERVERDA hisoblanadi.
 */
export function AttendanceAnalyticsPage() {
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [classId, setClassId] = useState('') // bo'sh = butun maktab
  const [from, setFrom] = useState(addDaysISO(todayISO(), -6))
  const [to, setTo] = useState(todayISO())
  const [day, setDay] = useState(todayISO())
  const [data, setData] = useState<AttendanceAnalytics | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    getClasses().then(setClasses)
  }, [])

  useEffect(() => {
    // Poyga (race) himoyasi: tez almashtirilganda faqat oxirgi javob qabul qilinadi.
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi so'rovdan oldin holatni tozalaymiz (maqsadli)
    setLoading(true)
    getAttendanceAnalytics({ classId, from, to, day })
      .then((d) => {
        if (active) setData(d)
      })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => {
      active = false
    }
  }, [classId, from, to, day])

  const applyRange = (days: number) => {
    const t = todayISO()
    setFrom(addDaysISO(t, -days))
    setTo(t)
    setDay(t)
  }

  const total = data?.total
  const trend = (data?.trend ?? []).map((p) => ({
    date: formatDate(p.date).slice(0, 5), // "dd.MM"
    keldi: p.tally.present,
    kelmadi: p.tally.absent,
    tekshirilmagan: p.tally.unchecked,
  }))

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Davomat analitikasi</h1>
        <p className="text-sm text-slate-400">
          Sinf va davr kesimida keldi / kelmadi / tekshirilmagan
        </p>
      </div>

      {/* Tanlovlar */}
      <div className="flex flex-wrap items-center gap-3">
        <select value={classId} onChange={(e) => setClassId(e.target.value)} className={control}>
          <option value="">Barcha sinflar</option>
          {classes.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}-sinf
            </option>
          ))}
        </select>
        <div className="flex items-center gap-2">
          <input
            type="date"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
            className={control}
          />
          <span className="text-slate-400">—</span>
          <input
            type="date"
            value={to}
            onChange={(e) => setTo(e.target.value)}
            className={control}
          />
        </div>
        <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
          {ranges.map((r) => (
            <button
              key={r.key}
              onClick={() => applyRange(r.days)}
              className="rounded-md px-3 py-1.5 text-sm font-medium text-slate-600 transition-colors hover:bg-white hover:text-slate-800"
            >
              {r.label}
            </button>
          ))}
        </div>
      </div>

      {loading && !data ? (
        <Loader label="Yuklanmoqda..." />
      ) : !data ? null : (
        <>
          {/* Ko'rsatkichlar */}
          <div className="grid grid-cols-2 gap-4 lg:grid-cols-5">
            <StatCard label="O'quvchilar" value={data.studentsTotal} icon={Users} />
            <StatCard
              label="Keldi"
              value={pct(total?.presentPct ?? null)}
              icon={UserCheck}
              iconBg="bg-emerald-50"
              iconColor="text-emerald-600"
              hint={`${total?.present ?? 0} ta belgi`}
            />
            <StatCard
              label="Kelmadi"
              value={pct(total?.absentPct ?? null)}
              icon={UserX}
              iconBg="bg-red-50"
              iconColor="text-red-600"
              hint={`Sababli ${total?.excused ?? 0} · sababsiz ${total?.unexcused ?? 0}`}
            />
            <StatCard
              label="Tekshirilmagan"
              value={pct(total?.uncheckedPct ?? null)}
              icon={HelpCircle}
              iconBg="bg-amber-50"
              iconColor="text-amber-600"
              hint={`${total?.unchecked ?? 0} ta dars belgilanmagan`}
            />
            <StatCard
              label="Kech keldi"
              value={total?.late ?? 0}
              icon={Clock}
              iconBg="bg-sky-50"
              iconColor="text-sky-600"
              hint="Kelganlar ichida"
            />
          </div>

          {/* Trend */}
          <Card>
            <h2 className="mb-3 font-semibold text-slate-800">Davr bo'yicha harakat</h2>
            {trend.length === 0 ? (
              <EmptyNote text="Bu davrda o'tilgan dars yo'q." />
            ) : (
              <ResponsiveContainer width="100%" height={280}>
                <BarChart data={trend} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#eef0f4" />
                  <XAxis
                    dataKey="date"
                    tickLine={false}
                    axisLine={false}
                    tick={{ fontSize: 12, fill: '#94a3b8' }}
                  />
                  <YAxis
                    tickLine={false}
                    axisLine={false}
                    tick={{ fontSize: 12, fill: '#94a3b8' }}
                  />
                  <Tooltip
                    cursor={{ fill: 'rgba(0,0,0,0.03)' }}
                    contentStyle={{ borderRadius: 12, border: '1px solid #e2e8f0', fontSize: 13 }}
                  />
                  <Bar dataKey="keldi" stackId="a" fill="#16a34a" maxBarSize={42} />
                  <Bar dataKey="kelmadi" stackId="a" fill="#dc2626" maxBarSize={42} />
                  <Bar
                    dataKey="tekshirilmagan"
                    stackId="a"
                    fill="#f59e0b"
                    radius={[6, 6, 0, 0]}
                    maxBarSize={42}
                  />
                </BarChart>
              </ResponsiveContainer>
            )}
          </Card>

          {/* Sinflar kesimi */}
          <Card className="p-0">
            <div className="flex items-center justify-between px-5 pt-5">
              <h2 className="font-semibold text-slate-800">Sinflar kesimi</h2>
              <span className="text-xs text-slate-400">
                {formatDate(data.from)} — {formatDate(data.to)}
              </span>
            </div>
            {data.classes.length === 0 ? (
              <EmptyNote text="Sinf topilmadi." />
            ) : (
              <div className="mt-3 overflow-x-auto">
                <table className="w-full min-w-[820px] text-left text-sm">
                  <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-4 py-3">Sinf</th>
                      <th className="px-4 py-3 text-center">O'quvchi</th>
                      <th className="px-4 py-3 text-center">Dars</th>
                      <th className="px-4 py-3 text-center">Keldi</th>
                      <th className="px-4 py-3 text-center">Sababli</th>
                      <th className="px-4 py-3 text-center">Sababsiz</th>
                      <th className="px-4 py-3 text-center">Kech</th>
                      <th className="px-4 py-3 text-center">Tekshirilmagan</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100 tabular-nums">
                    {data.classes.map((row) => (
                      <tr key={row.classId} className="hover:bg-slate-50/60">
                        <td className="px-4 py-3 font-medium text-slate-800">{row.className}</td>
                        <td className="px-4 py-3 text-center text-slate-600">{row.students}</td>
                        <td className="px-4 py-3 text-center text-slate-600">
                          {row.tally.lessons}
                        </td>
                        <td className="px-4 py-3 text-center font-semibold text-emerald-600">
                          {pct(row.tally.presentPct)}
                        </td>
                        <td className="px-4 py-3 text-center text-slate-600">
                          {row.tally.excused || '—'}
                        </td>
                        <td className="px-4 py-3 text-center font-semibold text-red-600">
                          {row.tally.unexcused || '—'}
                        </td>
                        <td className="px-4 py-3 text-center text-slate-600">
                          {row.tally.late || '—'}
                        </td>
                        <td
                          className={cn(
                            'px-4 py-3 text-center font-semibold',
                            row.tally.unchecked > 0 ? 'text-amber-600' : 'text-slate-300',
                          )}
                        >
                          {row.tally.unchecked || '—'}
                        </td>
                      </tr>
                    ))}
                    <TotalRow tally={data.total} students={data.studentsTotal} />
                  </tbody>
                </table>
              </div>
            )}
          </Card>

          {/* Bir kunning dars soatlari kesimi */}
          <Card className="p-0">
            <div className="flex flex-wrap items-center justify-between gap-3 px-5 pt-5">
              <h2 className="font-semibold text-slate-800">Dars soatlari kesimi</h2>
              <div className="flex items-center gap-1">
                <button
                  onClick={() => setDay(addDaysISO(day, -1))}
                  className="rounded-lg border border-slate-200 px-2 py-1.5 text-sm text-slate-500 hover:bg-slate-50"
                >
                  ‹
                </button>
                <input
                  type="date"
                  value={day}
                  min={from}
                  max={to}
                  onChange={(e) => setDay(e.target.value)}
                  className={control}
                />
                <button
                  onClick={() => setDay(addDaysISO(day, 1))}
                  className="rounded-lg border border-slate-200 px-2 py-1.5 text-sm text-slate-500 hover:bg-slate-50"
                >
                  ›
                </button>
              </div>
            </div>
            {data.periods.length === 0 ? (
              <EmptyNote text="Bu kuni o'tilgan dars yo'q." />
            ) : (
              <div className="mt-3 overflow-x-auto">
                <table className="w-full min-w-[720px] text-left text-sm">
                  <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-4 py-3">Dars</th>
                      <th className="px-4 py-3">Vaqti</th>
                      <th className="px-4 py-3 text-center">O'quvchi</th>
                      <th className="px-4 py-3 text-center">Keldi</th>
                      <th className="px-4 py-3 text-center">Kelmadi</th>
                      <th className="px-4 py-3 text-center">Tekshirilmagan</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100 tabular-nums">
                    {data.periods.map((p) => (
                      <tr key={p.period} className="hover:bg-slate-50/60">
                        <td className="px-4 py-3 font-medium text-slate-800">{p.period}-dars</td>
                        <td className="px-4 py-3 text-slate-400">
                          {p.startTime ? `${p.startTime} — ${p.endTime}` : '—'}
                        </td>
                        <td className="px-4 py-3 text-center text-slate-600">
                          {p.tally.opportunities}
                        </td>
                        <td className="px-4 py-3 text-center font-semibold text-emerald-600">
                          {p.tally.present} <span className="text-slate-400">· {pct(p.tally.presentPct)}</span>
                        </td>
                        <td className="px-4 py-3 text-center font-semibold text-red-600">
                          {p.tally.absent || '—'}
                        </td>
                        <td
                          className={cn(
                            'px-4 py-3 text-center font-semibold',
                            p.tally.unchecked > 0 ? 'text-amber-600' : 'text-slate-300',
                          )}
                        >
                          {p.tally.unchecked || '—'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>

          {/* Sabablar */}
          <Card>
            <h2 className="mb-1 font-semibold text-slate-800">Sabablar</h2>
            <p className="mb-3 text-xs text-slate-400">
              Jurnalda qo'yilgan belgilar soni. "Sababsiz" deb sabab nomi yoki unga berilgan
              manfiy intizomiy ball asosida ajratiladi.
            </p>
            {data.reasons.every((r) => r.count === 0) ? (
              <EmptyNote text="Bu davrda davomat belgisi qo'yilmagan." />
            ) : (
              <div className="flex flex-wrap gap-2">
                {data.reasons
                  .filter((r) => r.count > 0)
                  .map((r) => (
                    <span
                      key={r.reasonId}
                      className={cn(
                        'rounded-lg px-3 py-1.5 text-sm font-medium',
                        r.isLate
                          ? 'bg-sky-50 text-sky-700'
                          : r.unexcused
                            ? 'bg-red-50 text-red-700'
                            : 'bg-slate-100 text-slate-600',
                      )}
                    >
                      {r.name}: {r.count}
                    </span>
                  ))}
              </div>
            )}
          </Card>
        </>
      )}
    </div>
  )
}

/** Jadval oxiridagi "Jami" qatori — sinflar yig'indisi umumiy son bilan mos kelishini ko'rsatadi. */
function TotalRow({ tally, students }: { tally: AttendanceTally; students: number }) {
  return (
    <tr className="bg-slate-50/60 font-semibold">
      <td className="px-4 py-3 text-slate-800">Jami</td>
      <td className="px-4 py-3 text-center text-slate-700">{students}</td>
      <td className="px-4 py-3 text-center text-slate-700">{tally.lessons}</td>
      <td className="px-4 py-3 text-center text-emerald-700">{pct(tally.presentPct)}</td>
      <td className="px-4 py-3 text-center text-slate-700">{tally.excused || '—'}</td>
      <td className="px-4 py-3 text-center text-red-700">{tally.unexcused || '—'}</td>
      <td className="px-4 py-3 text-center text-slate-700">{tally.late || '—'}</td>
      <td className="px-4 py-3 text-center text-amber-700">{tally.unchecked || '—'}</td>
    </tr>
  )
}

function EmptyNote({ text }: { text: string }) {
  return <p className="py-10 text-center text-sm text-slate-400">{text}</p>
}
