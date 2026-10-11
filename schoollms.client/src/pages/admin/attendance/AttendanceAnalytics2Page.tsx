/**
 * Davomat analitikasi 2.0 (mijoz, 2026-10-11: "Davomat analitikasi 2.0 qilish uni ichida kechki va
 * yotoqxona statistikasi bo'ladi").
 *
 * Tepada — uchta davomat bir qarashda: kunduzgi (dars-ma-dars jurnal, "Davomat analitikasi" bilan bir
 * xil raqam), kechki dars va yotoqxona. Pastda — tanlangan kechki/yotoqxona sessiyasining batafsili:
 * kunlar grafigi, guruhlar, sabablar, o'quvchilar va "kim, qaysi kuni, nima sababdan". Birlik —
 * o'quvchi-kun; "belgilanmagan" faqat yo'qlama olingan kunlarda sanaladi (server izohi).
 */
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { BedDouble, CalendarDays, FileSpreadsheet, HelpCircle, Moon, Sun, UserCheck, Users, UserX } from 'lucide-react'
import { getAttendanceAnalytics, type AttendanceAnalytics } from '@/api/services/attendanceAnalytics'
import {
  downloadBoardingAnalytics,
  getBoardingAnalytics,
  type BoardingAnalytics,
  type BoardingSession,
} from '@/api/services/boardingAttendance'
import { useAuth } from '@/context/auth-context'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { DatePicker } from '@/components/ui/DatePicker'
import { Loader } from '@/components/ui/Loader'
import { StatCard } from '@/components/ui/StatCard'
import { addDaysISO } from '@/lib/weeks'
import { cn, formatDate } from '@/lib/utils'

const pad = (n: number) => String(n).padStart(2, '0')
const todayISO = () => {
  const d = new Date()
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}
const pct = (v: number | null | undefined) => (v == null ? '—' : `${v.toFixed(1).replace('.0', '')}%`)

const RANGES = [
  { key: 'today', label: 'Bugun', days: 0 },
  { key: 'week', label: '7 kun', days: 6 },
  { key: 'month', label: '30 kun', days: 29 },
  { key: 'thisMonth', label: 'Shu oy', days: -1 },
] as const

const SESSIONS: { key: BoardingSession; label: string; perm: string; icon: typeof Moon }[] = [
  { key: 'evening', label: 'Kechki dars', perm: 'attendanceEvening', icon: Moon },
  { key: 'dorm', label: 'Yotoqxona', perm: 'attendanceDorm', icon: BedDouble },
]

const STATUS_LABEL: Record<string, { label: string; tone: string }> = {
  absent: { label: 'Kelmadi', tone: 'bg-red-50 text-red-700' },
  excused: { label: 'Sababli', tone: 'bg-slate-100 text-slate-600' },
  late: { label: 'Kech keldi', tone: 'bg-sky-50 text-sky-700' },
  unchecked: { label: 'Belgilanmagan', tone: 'bg-amber-50 text-amber-700' },
}

export function AttendanceAnalytics2Page() {
  const { user } = useAuth()
  const can = (perm: string) => !user?.permissions || user.permissions.includes(perm) || user.permissions.includes(`${perm}:view`)
  const sessions = SESSIONS.filter((s) => can(s.perm))
  const canDay = can('attendance')

  const [from, setFrom] = useState(addDaysISO(todayISO(), -6))
  const [to, setTo] = useState(todayISO())
  const [tab, setTab] = useState<BoardingSession>(sessions[0]?.key ?? 'evening')
  const [day, setDay] = useState<AttendanceAnalytics | null>(null)
  const [boarding, setBoarding] = useState<Partial<Record<BoardingSession, BoardingAnalytics>>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [exporting, setExporting] = useState(false)

  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi davr: holatni tozalab qayta o'qiymiz (maqsadli)
    setLoading(true)
    setError(null)
    Promise.all([
      canDay ? getAttendanceAnalytics({ from, to }).catch(() => null) : Promise.resolve(null),
      ...sessions.map((s) => getBoardingAnalytics(s.key, from, to).then((d) => [s.key, d] as const)),
    ])
      .then(([d, ...rest]) => {
        if (!active) return
        setDay(d as AttendanceAnalytics | null)
        setBoarding(Object.fromEntries(rest as (readonly [BoardingSession, BoardingAnalytics])[]))
      })
      .catch(() => active && setError("Statistikani yuklab bo'lmadi"))
      .finally(() => active && setLoading(false))
    return () => {
      active = false
    }
    // sessions/canDay depend only on the user — stable for the page's life
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [from, to])

  const applyRange = (days: number) => {
    const t = todayISO()
    setFrom(days < 0 ? `${t.slice(0, 8)}01` : addDaysISO(t, -days))
    setTo(t)
  }

  const current = boarding[tab]

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Davomat analitikasi 2.0</h1>
          <p className="text-sm text-slate-400">Kunduzgi, kechki dars va yotoqxona davomati — bir joyda</p>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <div className="flex items-center gap-2">
          <DatePicker value={from} max={to} onChange={setFrom} className="w-40" ariaLabel="Davr boshi" />
          <span className="text-slate-400">—</span>
          <DatePicker value={to} min={from} max={todayISO()} onChange={setTo} className="w-40" ariaLabel="Davr oxiri" />
        </div>
        <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
          {RANGES.map((r) => (
            <button
              key={r.key}
              type="button"
              onClick={() => applyRange(r.days)}
              className="rounded-md px-3 py-1.5 text-sm font-medium text-slate-600 transition-colors hover:bg-white hover:text-slate-800"
            >
              {r.label}
            </button>
          ))}
        </div>
      </div>

      {error && <p className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{error}</p>}

      {loading && Object.keys(boarding).length === 0 ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          {/* Uchta davomat bir qarashda */}
          <div className={cn('grid gap-4', canDay ? 'md:grid-cols-3' : 'md:grid-cols-2')}>
            {canDay && (
              <OverviewCard
                icon={Sun}
                title="Kunduzgi davomat"
                present={day?.total.presentPct ?? null}
                lines={[
                  `Kelmadi: ${day?.total.absent ?? 0} o'quvchi-kun (sababsiz ${day?.total.unexcused ?? 0})`,
                  `Tekshirilmagan: ${day?.total.unchecked ?? 0}`,
                ]}
                footer={
                  <Link to="/admin/attendance/analytics" className="text-xs font-medium text-brand-600 hover:underline">
                    Batafsil — Davomat analitikasi →
                  </Link>
                }
              />
            )}
            {sessions.map((s) => {
              const d = boarding[s.key]
              return (
                <OverviewCard
                  key={s.key}
                  icon={s.icon}
                  title={s.label}
                  present={d?.total.presentPct ?? null}
                  active={tab === s.key}
                  onClick={() => setTab(s.key)}
                  lines={[
                    `Kelmadi: ${d?.total.absent ?? 0} · sababli: ${d?.total.excused ?? 0}`,
                    `Ro'yxatda: ${d?.rosterNow ?? 0} · yo'qlama kunlari: ${d?.heldDays ?? 0}`,
                  ]}
                />
              )
            })}
          </div>

          {sessions.length > 0 && current && (
            <SessionDetails
              data={current}
              label={SESSIONS.find((s) => s.key === tab)!.label}
              exporting={exporting}
              onExport={() => {
                setExporting(true)
                downloadBoardingAnalytics(tab, from, to).finally(() => setExporting(false))
              }}
            />
          )}
          {sessions.length === 0 && (
            <Card>
              <p className="py-8 text-center text-sm text-slate-400">Kechki dars va yotoqxona davomatini ko'rishga ruxsatingiz yo'q.</p>
            </Card>
          )}
        </>
      )}
    </div>
  )
}

function OverviewCard({
  icon: Icon,
  title,
  present,
  lines,
  footer,
  active,
  onClick,
}: {
  icon: typeof Moon
  title: string
  present: number | null
  lines: string[]
  footer?: React.ReactNode
  active?: boolean
  onClick?: () => void
}) {
  const body = (
    <Card className={cn('h-full space-y-2 p-5 transition', active && 'ring-2 ring-brand-400', onClick && 'hover:shadow-md')}>
      <div className="flex items-center gap-2 text-sm font-medium text-slate-500">
        <Icon className="h-4 w-4" /> {title}
      </div>
      <p className="text-3xl font-semibold text-slate-800">{pct(present)}</p>
      <p className="text-xs text-slate-400">keldi (belgilanganlar ichida)</p>
      {lines.map((l) => (
        <p key={l} className="text-sm text-slate-600">
          {l}
        </p>
      ))}
      {footer}
    </Card>
  )
  return onClick ? (
    <button type="button" onClick={onClick} className="block text-left">
      {body}
    </button>
  ) : (
    body
  )
}

function SessionDetails({
  data,
  label,
  exporting,
  onExport,
}: {
  data: BoardingAnalytics
  label: string
  exporting: boolean
  onExport: () => void
}) {
  const [student, setStudent] = useState<string | null>(null)
  const [status, setStatus] = useState<string>('all')
  const [query, setQuery] = useState('')
  const reasonName = useMemo(() => new Map(data.reasons.map((r) => [r.reasonId, r.name])), [data])

  const chart = data.trend.map((p) => ({
    date: formatDate(p.date).slice(0, 5),
    keldi: p.tally.present,
    kelmadi: p.tally.absent,
    sababli: p.tally.excused,
    belgilanmagan: p.tally.unchecked,
  }))
  const students = useMemo(() => {
    const q = query.trim().toLowerCase()
    return data.students.filter((s) => !q || s.fullName.toLowerCase().includes(q) || s.groupName.toLowerCase().includes(q))
  }, [data, query])
  const events = data.events.filter((e) => (!student || e.studentId === student) && (status === 'all' || e.status === status))
  const t = data.total

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-semibold text-slate-800">{label} — batafsil</h2>
        <Button variant="secondary" onClick={onExport} disabled={exporting}>
          <FileSpreadsheet className="h-4 w-4" /> {exporting ? 'Tayyorlanmoqda…' : 'Excel'}
        </Button>
      </div>

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-6">
        <StatCard label="Ro'yxatda" value={data.rosterNow} icon={Users} hint="bugun" />
        <StatCard label="Yo'qlama kunlari" value={data.heldDays} icon={CalendarDays} iconBg="bg-slate-50" iconColor="text-slate-500" />
        <StatCard label="Keldi" value={pct(t.presentPct)} icon={UserCheck} iconBg="bg-emerald-50" iconColor="text-emerald-600"
          hint={`${t.present} o'quvchi-kun${t.late ? ` · ${t.late} kech` : ''}`} />
        <StatCard label="Kelmadi" value={t.absent} icon={UserX} iconBg="bg-red-50" iconColor="text-red-600" hint="sababsiz"
          onClick={() => setStatus('absent')} />
        <StatCard label="Sababli" value={t.excused} icon={UserX} iconBg="bg-slate-50" iconColor="text-slate-500"
          onClick={() => setStatus('excused')} />
        <StatCard label="Belgilanmagan" value={t.unchecked} icon={HelpCircle} iconBg="bg-amber-50" iconColor="text-amber-600"
          onClick={() => setStatus('unchecked')} />
      </div>

      <Card>
        <h3 className="mb-3 font-semibold text-slate-800">Kunlar bo'yicha</h3>
        {chart.length === 0 ? (
          <p className="py-8 text-center text-sm text-slate-400">Bu davrda yo'qlama olinmagan.</p>
        ) : (
          <div className="h-64">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={chart}>
                <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" />
                <XAxis dataKey="date" tick={{ fontSize: 12 }} />
                <YAxis allowDecimals={false} tick={{ fontSize: 12 }} />
                <Tooltip />
                <Bar dataKey="keldi" stackId="a" fill="#10b981" />
                <Bar dataKey="kelmadi" stackId="a" fill="#ef4444" />
                <Bar dataKey="sababli" stackId="a" fill="#94a3b8" />
                <Bar dataKey="belgilanmagan" stackId="a" fill="#f59e0b" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        )}
      </Card>

      <div className="grid gap-5 lg:grid-cols-[2fr_1fr]">
        <Card className="p-0">
          <h3 className="px-5 pt-5 font-semibold text-slate-800">Guruhlar kesimi</h3>
          <div className="mt-3 overflow-x-auto">
            <table className="w-full min-w-[520px] text-sm tabular-nums">
              <thead className="bg-slate-50 text-left text-xs text-slate-400">
                <tr>
                  <th className="px-5 py-2 font-medium">Guruh</th>
                  <th className="px-3 py-2 text-right font-medium">O'quvchi</th>
                  <th className="px-3 py-2 text-right font-medium">Keldi</th>
                  <th className="px-3 py-2 text-right font-medium">Kelmadi</th>
                  <th className="px-3 py-2 text-right font-medium">Sababli</th>
                  <th className="px-5 py-2 text-right font-medium">Belgilanmagan</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.groups.map((g) => (
                  <tr key={g.key}>
                    <td className="px-5 py-2 font-medium text-slate-800">{g.name}</td>
                    <td className="px-3 py-2 text-right text-slate-600">{g.students}</td>
                    <td className="px-3 py-2 text-right font-semibold text-emerald-600">{pct(g.tally.presentPct)}</td>
                    <td className="px-3 py-2 text-right text-red-600">{g.tally.absent || '—'}</td>
                    <td className="px-3 py-2 text-right text-slate-600">{g.tally.excused || '—'}</td>
                    <td className={cn('px-5 py-2 text-right', g.tally.unchecked ? 'text-amber-600' : 'text-slate-300')}>
                      {g.tally.unchecked || '—'}
                    </td>
                  </tr>
                ))}
                {data.groups.length === 0 && (
                  <tr>
                    <td colSpan={6} className="px-5 py-6 text-center text-slate-400">Ma'lumot yo'q</td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </Card>
        <Card>
          <h3 className="mb-3 font-semibold text-slate-800">Sabablar</h3>
          {data.reasons.length === 0 ? (
            <p className="py-6 text-center text-sm text-slate-400">Sabab qo'yilmagan</p>
          ) : (
            <div className="flex flex-wrap gap-2">
              {data.reasons.map((r) => (
                <span
                  key={r.reasonId}
                  className={cn('rounded-lg px-3 py-1.5 text-sm font-medium', r.isLate ? 'bg-sky-50 text-sky-700' : 'bg-slate-100 text-slate-600')}
                >
                  {r.name}: {r.count}
                </span>
              ))}
            </div>
          )}
        </Card>
      </div>

      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-3 px-5 pt-5">
          <div>
            <h3 className="font-semibold text-slate-800">O'quvchilar</h3>
            <p className="text-xs text-slate-400">Kelmaganlari ko'pidan. Qatorni bosing — kunlari va sabablari.</p>
          </div>
          <input
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Ism yoki guruh"
            className="w-52 rounded-lg border border-slate-200 px-3 py-1.5 text-sm outline-none focus:border-brand-400"
          />
        </div>
        <div className="mt-3 max-h-[28rem] overflow-auto">
          <table className="w-full min-w-[640px] text-sm tabular-nums">
            <thead className="sticky top-0 bg-slate-50 text-left text-xs text-slate-400">
              <tr>
                <th className="px-5 py-2 font-medium">O'quvchi</th>
                <th className="px-3 py-2 font-medium">Guruh</th>
                <th className="px-3 py-2 text-right font-medium">Keldi</th>
                <th className="px-3 py-2 text-right font-medium">Kelmadi</th>
                <th className="px-3 py-2 text-right font-medium">Sababli</th>
                <th className="px-5 py-2 font-medium">Oxirgi kelmagan</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {students.map((s) => (
                <tr
                  key={s.studentId}
                  onClick={() => setStudent((cur) => (cur === s.studentId ? null : s.studentId))}
                  className={cn('cursor-pointer hover:bg-brand-50/40', student === s.studentId && 'bg-brand-50/70')}
                >
                  <td className="px-5 py-2">
                    <span className="font-medium text-slate-800">{s.fullName}</span>
                    <span className="block text-xs text-slate-400">{s.className || '—'}</span>
                  </td>
                  <td className="px-3 py-2 text-slate-500">{s.groupName}</td>
                  <td className="px-3 py-2 text-right font-semibold text-emerald-600">{pct(s.tally.presentPct)}</td>
                  <td className={cn('px-3 py-2 text-right', s.tally.absent ? 'font-semibold text-red-600' : 'text-slate-300')}>
                    {s.tally.absent || '—'}
                  </td>
                  <td className="px-3 py-2 text-right text-slate-600">{s.tally.excused || '—'}</td>
                  <td className="px-5 py-2 text-slate-500">{s.lastAbsent ? formatDate(s.lastAbsent) : '—'}</td>
                </tr>
              ))}
              {students.length === 0 && (
                <tr>
                  <td colSpan={6} className="px-5 py-6 text-center text-slate-400">Ma'lumot yo'q</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </Card>

      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-3 px-5 pt-5">
          <div>
            <h3 className="font-semibold text-slate-800">Kim, qaysi kuni, nima sababdan</h3>
            {student && (
              <button type="button" onClick={() => setStudent(null)} className="text-xs text-brand-600 hover:underline">
                {data.students.find((s) => s.studentId === student)?.fullName} — barcha o'quvchilarni ko'rsatish
              </button>
            )}
          </div>
          <div className="flex gap-1 rounded-xl bg-slate-100 p-1">
            {(['all', 'absent', 'excused', 'late', 'unchecked'] as const).map((k) => (
              <button
                key={k}
                type="button"
                onClick={() => setStatus(k)}
                className={cn(
                  'rounded-lg px-3 py-1 text-xs font-medium transition-colors',
                  status === k ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500',
                )}
              >
                {k === 'all' ? 'Hammasi' : STATUS_LABEL[k].label}
              </button>
            ))}
          </div>
        </div>
        <div className="mt-3 max-h-[24rem] overflow-auto">
          <table className="w-full min-w-[560px] text-sm">
            <thead className="sticky top-0 bg-slate-50 text-left text-xs text-slate-400">
              <tr>
                <th className="px-5 py-2 font-medium">Sana</th>
                <th className="px-3 py-2 font-medium">O'quvchi</th>
                <th className="px-3 py-2 font-medium">Guruh</th>
                <th className="px-3 py-2 font-medium">Holat</th>
                <th className="px-5 py-2 font-medium">Sabab</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {events.map((e, i) => (
                <tr key={`${e.studentId}-${e.date}-${i}`}>
                  <td className="px-5 py-2 text-slate-500">{formatDate(e.date)}</td>
                  <td className="px-3 py-2 font-medium text-slate-800">{e.fullName}</td>
                  <td className="px-3 py-2 text-slate-500">{e.groupName}</td>
                  <td className="px-3 py-2">
                    <span className={cn('rounded-md px-2 py-0.5 text-xs font-medium', STATUS_LABEL[e.status]?.tone)}>
                      {STATUS_LABEL[e.status]?.label ?? e.status}
                    </span>
                  </td>
                  <td className="px-5 py-2 text-slate-600">{e.reasonId ? reasonName.get(e.reasonId) ?? "O'chirilgan sabab" : '—'}</td>
                </tr>
              ))}
              {events.length === 0 && (
                <tr>
                  <td colSpan={5} className="px-5 py-6 text-center text-slate-400">Mos yozuv yo'q</td>
                </tr>
              )}
            </tbody>
          </table>
          {data.eventsTruncated && (
            <p className="px-5 py-2 text-xs text-amber-600">Ro'yxat 5000 qatorda kesildi — davrni toraytiring yoki Excel'ni yuklab oling.</p>
          )}
        </div>
      </Card>
    </div>
  )
}
