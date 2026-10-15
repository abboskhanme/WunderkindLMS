import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import type { HubConnection } from '@microsoft/signalr'
import { RefreshCw, Wifi, WifiOff, CheckCircle2, Search } from 'lucide-react'
import {
  getTeacherAttendanceRange,
  setTeacherAttendance,
  setTeacherAttendanceDay,
  getAttendanceDashboard,
  syncTurnstile,
  type TeacherAttendanceDay,
  type TeacherAttendanceRangeBoard,
  type AttendanceDashboard,
} from '@/api/services/teacherAttendance'
import { connectLiveTopic } from '@/api/services/live'
import { addDaysISO, mondayOfISO } from '@/lib/weeks'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { DatePicker } from '@/components/ui/DatePicker'

// Bo'sh → keldi → kelmadi → kechikdi → bo'sh
const CYCLE: Record<string, string> = { '': 'present', present: 'absent', absent: 'late', late: '' }

const STATUS: Record<string, { label: string; short: string; cell: string }> = {
  present: { label: 'Keldi', short: '✓', cell: 'bg-emerald-100 text-emerald-700' },
  absent: { label: 'Kelmadi', short: '✗', cell: 'bg-red-100 text-red-600' },
  late: { label: 'Kechikdi', short: 'K', cell: 'bg-amber-100 text-amber-700' },
}

// Dashboard holat ko'rinishi (rang + yorliq)
const DASH_STATUS: Record<string, { label: string; badge: string; dot: string }> = {
  present: { label: 'Keldi', badge: 'bg-emerald-50 text-emerald-700', dot: 'bg-emerald-500' },
  late: { label: 'Kechikdi', badge: 'bg-amber-50 text-amber-700', dot: 'bg-amber-500' },
  absent: { label: 'Kelmadi', badge: 'bg-red-50 text-red-600', dot: 'bg-red-500' },
  '': { label: 'Kelmagan', badge: 'bg-slate-100 text-slate-500', dot: 'bg-slate-300' },
}

const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

/** The server refuses longer ranges (TeacherAttendanceController.MaxRangeDays). */
const MAX_RANGE_DAYS = 93

const weekdayShort = (iso: string) => ['Ya', 'Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh'][new Date(iso).getDay()] ?? ''
const pad = (n: number) => String(n).padStart(2, '0')
const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}
const initials = (name: string) =>
  name.split(' ').filter(Boolean).slice(0, 2).map((s) => s[0]?.toUpperCase()).join('')
const syncLabel = (iso: string) => (iso && iso.length >= 16 ? `${iso.slice(0, 10)} ${iso.slice(11, 16)}` : '—')

/** First and last day of the month that contains `iso`. */
const monthRange = (iso: string) => {
  const [y, m] = iso.split('-').map(Number)
  return { from: `${iso.slice(0, 7)}-01`, to: `${iso.slice(0, 7)}-${pad(new Date(y, m, 0).getDate())}` }
}
const daysBetween = (from: string, to: string) => Math.round((Date.parse(to) - Date.parse(from)) / 86_400_000)

const PRESETS: { key: string; label: string; range: () => { from: string; to: string } }[] = [
  { key: 'thisMonth', label: 'Shu oy', range: () => monthRange(today()) },
  {
    key: 'lastMonth',
    label: "O'tgan oy",
    range: () => {
      const [y, m] = today().split('-').map(Number)
      return monthRange(m === 1 ? `${y - 1}-12-01` : `${y}-${pad(m - 1)}-01`)
    },
  },
  {
    key: 'thisWeek',
    label: 'Shu hafta',
    range: () => {
      const monday = mondayOfISO(today())
      return { from: monday, to: addDaysISO(monday, 5) }
    },
  },
]

export function TeacherAttendancePage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">O'qituvchilar davomati</h1>
        <p className="text-sm text-slate-400">
          Turniket/FaceID qurilmasidan avtomatik yuklanadi. Kunlik holat — kim keldi, soat nechada, kechikdimi.
          Dars jadvaliga bog'langan: har kuni faqat o'sha kuni darsi bor ustozlar ko'rsatiladi.
        </p>
      </div>
      <RangeGrid />
      <DashboardSection />
    </div>
  )
}

/* ---------------- Kunlik dashboard (turniket/FaceID) ---------------- */

function DashboardSection() {
  const [date, setDate] = useState(today())
  const [dash, setDash] = useState<AttendanceDashboard | null>(null)
  const [loading, setLoading] = useState(true)
  const [syncing, setSyncing] = useState(false)
  const [search, setSearch] = useState('')
  /** Holat filtri: 'all' | 'present' | 'late' | 'absent' | '' (kelmagan) */
  const [statusFilter, setStatusFilter] = useState<string>('all')

  const load = useCallback((d: string) => {
    setLoading(true)
    getAttendanceDashboard(d)
      .then(setDash)
      .finally(() => setLoading(false))
  }, [])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- sana o'zgarganda ma'lumotni qayta yuklaymiz, yangi so'rovdan oldin "yuklanmoqda" holati (maqsadli)
  useEffect(() => load(date), [date, load])

  // Real-time: turniketdan yangi o'tish kelganda joriy kun dashboard'ini jonli yangilaymiz (SignalR).
  const reloadRef = useRef<() => void>(() => {})
  useEffect(() => {
    reloadRef.current = () => load(date)
  }, [date, load])
  useEffect(() => {
    let conn: HubConnection | null = null
    connectLiveTopic('turnstile', { turnstileChanged: () => reloadRef.current() })
      .then((c) => { conn = c })
      .catch(() => {})
    return () => { conn?.stop() }
  }, [])

  const onSync = async () => {
    setSyncing(true)
    try {
      const res = await syncTurnstile()
      alert(res.message)
      load(date)
    } catch {
      alert('Sinxronlashda xatolik')
    } finally {
      setSyncing(false)
    }
  }

  // Holatni qo'lda almashtirish (admin tuzatishi mumkin — manual sifatida saqlanadi).
  // A teacher without lessons that day cannot be marked (the server refuses it too).
  const cycle = (teacherId: string, current: string, hasLessons: boolean) => {
    if (!dash?.inTeachingPeriod || !hasLessons) return
    const next = CYCLE[current] ?? 'present'
    setDash((prev) =>
      prev
        ? { ...prev, rows: prev.rows.map((r) => (r.teacherId === teacherId ? { ...r, status: next, source: 'manual' } : r)) }
        : prev,
    )
    setTeacherAttendance(teacherId, date, next || null).catch(() => load(date))
  }

  const s = dash?.summary
  const cards: { label: string; value: number; cls: string; filter: string }[] = [
    { label: 'Jami', value: s?.total ?? 0, cls: 'text-slate-800', filter: 'all' },
    { label: 'Keldi', value: s?.present ?? 0, cls: 'text-emerald-600', filter: 'present' },
    { label: 'Kechikdi', value: s?.late ?? 0, cls: 'text-amber-600', filter: 'late' },
    { label: 'Kelmadi', value: s?.absent ?? 0, cls: 'text-red-600', filter: 'absent' },
    { label: 'Kelmagan', value: s?.notArrived ?? 0, cls: 'text-slate-400', filter: '' },
  ]

  const q = search.trim().toLowerCase()
  const visibleRows = (dash?.rows ?? []).filter((r) => {
    if (q && !r.fullName.toLowerCase().includes(q)) return false
    if (statusFilter !== 'all' && r.status !== statusFilter) return false
    return true
  })

  return (
    <Card>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          {dash?.turnstileEnabled ? (
            <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
              <Wifi className="h-3.5 w-3.5" /> Turniket yoqilgan
            </span>
          ) : (
            <Link
              to="/admin/settings/turnstile"
              className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500 hover:bg-slate-200"
            >
              <WifiOff className="h-3.5 w-3.5" /> Turniket o'chiq — sozlash
            </Link>
          )}
          {dash?.lastSync && (
            <span className="text-xs text-slate-400">Oxirgi sinx: {syncLabel(dash.lastSync)}</span>
          )}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <div className="relative">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="O'qituvchi qidirish..."
              className="w-52 rounded-lg border border-slate-200 bg-white py-2 pl-8 pr-3 text-sm text-slate-700 outline-none focus:border-brand-400"
            />
          </div>
          <DatePicker
            value={date}
            onChange={(value: string) => setDate(value)}
            className="w-40"
          />
          <Button onClick={onSync} disabled={syncing}>
            <RefreshCw className={cn('h-4 w-4', syncing && 'animate-spin')} />
            {syncing ? 'Sinxron...' : 'Sinxronlash'}
          </Button>
        </div>
      </div>

      {/* Jamlama kartochkalar — bosilsa o'sha holat bo'yicha filtr */}
      <div className="mb-5 grid grid-cols-2 gap-3 sm:grid-cols-5">
        {cards.map((c) => {
          const active = statusFilter === c.filter
          return (
            <button
              key={c.label}
              type="button"
              onClick={() => setStatusFilter(c.filter)}
              title={`Filtr: ${c.label}`}
              className={cn(
                'rounded-xl border px-4 py-3 text-left transition-colors',
                active
                  ? 'border-brand-300 bg-brand-50/60 ring-1 ring-brand-200'
                  : 'border-slate-100 bg-slate-50/60 hover:bg-slate-100',
              )}
            >
              <div className={cn('text-2xl font-bold', c.cls)}>{c.value}</div>
              <div className="text-xs text-slate-400">{c.label}</div>
            </button>
          )
        })}
      </div>

      {!loading && dash && dash.noLessonTeachers > 0 && (
        <p className="-mt-2 mb-4 text-xs text-slate-400">
          Bu kuni {dash.noLessonTeachers} nafar ustozning darsi yo'q.
        </p>
      )}

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : !dash ? (
        <p className="py-8 text-center text-slate-400">O'qituvchi yo'q</p>
      ) : !dash.inTeachingPeriod ? (
        <p className="py-8 text-center text-slate-400">Bu kun dars jadvali (chorak) davrida emas</p>
      ) : dash.rows.length === 0 ? (
        <p className="py-8 text-center text-slate-400">
          Bu kuni darsi bor ustoz yo'q (yakshanba, bayram yoki jadvalda dars yo'q)
        </p>
      ) : visibleRows.length === 0 ? (
        <p className="py-8 text-center text-slate-400">Filtrga mos o'qituvchi topilmadi</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-3 py-2">O'qituvchi</th>
                <th className="px-3 py-2 text-center">Kirish</th>
                <th className="px-3 py-2 text-center">Chiqish</th>
                <th className="px-3 py-2 text-center">Kechikish</th>
                <th className="px-3 py-2 text-center">Holat</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {visibleRows.map((r) => {
                const meta = DASH_STATUS[r.status] ?? DASH_STATUS['']
                return (
                  <tr key={r.teacherId} className="hover:bg-slate-50/60">
                    <td className="px-3 py-2">
                      <div className="flex items-center gap-2.5">
                        {r.photoUrl ? (
                          <img src={r.photoUrl} alt="" className="h-8 w-8 rounded-full object-cover" />
                        ) : (
                          <span className="flex h-8 w-8 items-center justify-center rounded-full bg-brand-50 text-xs font-semibold text-brand-600">
                            {initials(r.fullName)}
                          </span>
                        )}
                        <div>
                          <div className="font-medium text-slate-800">{r.fullName}</div>
                          <div className="text-[11px] text-slate-400">
                            {r.hasLessons ? `${r.lessons} ta dars` : "darsi yo'q"}
                            {!r.deviceUserId && <span className="text-amber-500"> · qurilma ID yo'q</span>}
                          </div>
                        </div>
                      </div>
                    </td>
                    <td className="px-3 py-2 text-center font-medium text-slate-700">{r.checkIn || '—'}</td>
                    <td className="px-3 py-2 text-center text-slate-500">{r.checkOut || '—'}</td>
                    <td className="px-3 py-2 text-center text-xs">
                      {r.status === 'late' && r.lateMinutes > 0 ? (
                        <span className="text-amber-600">+{r.lateMinutes} daq</span>
                      ) : r.expected ? (
                        <span className="text-slate-400">{r.expected}</span>
                      ) : (
                        '—'
                      )}
                    </td>
                    <td className="px-3 py-2 text-center">
                      <button
                        type="button"
                        onClick={() => cycle(r.teacherId, r.status, r.hasLessons)}
                        disabled={!r.hasLessons}
                        title={
                          r.hasLessons
                            ? "Bosing: qo'lda o'zgartirish"
                            : "Bu kuni darsi yo'q — belgini yuqoridagi jadvaldan o'chirish mumkin"
                        }
                        className={cn(
                          'inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium transition-opacity',
                          r.hasLessons ? 'hover:opacity-80' : 'cursor-default opacity-60',
                          meta.badge,
                        )}
                      >
                        <span className={cn('h-1.5 w-1.5 rounded-full', meta.dot)} />
                        {meta.label}
                        {r.source === 'turnstile' && <CheckCircle2 className="h-3 w-3 opacity-60" />}
                      </button>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  )
}

/* ---------------- Range grid (manual marking, tied to the timetable) ---------------- */

const EMPTY_BOARD: TeacherAttendanceRangeBoard = {
  teachers: [], entries: [], quarters: [], from: '', to: '', days: [], lessons: [],
}

/** Why a day has no lessons for anybody — or, failing that, for this teacher. */
const noLessonReason = (d: TeacherAttendanceDay) =>
  d.isHoliday
    ? d.holidayName ? `Bayram: ${d.holidayName}` : 'Bayram'
    : !d.inQuarter
      ? 'Dars jadvali (chorak) davri emas'
      : "Dars yo'q"

function RangeGrid() {
  const [range, setRange] = useState(() => monthRange(today()))
  const [board, setBoard] = useState<TeacherAttendanceRangeBoard>(EMPTY_BOARD)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  // Only the latest request may fill the grid — switching ranges quickly must not show an older one.
  const requestRef = useRef(0)

  const load = useCallback((from: string, to: string) => {
    const id = ++requestRef.current
    setLoading(true)
    setError(null)
    getTeacherAttendanceRange(from, to)
      .then((b) => id === requestRef.current && setBoard(b))
      .catch((e) => id === requestRef.current && setError(e?.response?.data?.message ?? "Davomatni yuklab bo'lmadi"))
      .finally(() => id === requestRef.current && setLoading(false))
  }, [])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- reload when the range changes; the "loading" state before the request is intended
  useEffect(() => load(range.from, range.to), [range, load])

  const onFrom = (value: string) => {
    if (!value) return
    setRange((r) => {
      let to = r.to < value ? value : r.to
      if (daysBetween(value, to) >= MAX_RANGE_DAYS) to = addDaysISO(value, MAX_RANGE_DAYS - 1)
      return { from: value, to }
    })
  }
  const onTo = (value: string) => {
    if (value) setRange((r) => ({ ...r, to: value }))
  }
  const activePreset = PRESETS.find((p) => {
    const r = p.range()
    return r.from === range.from && r.to === range.to
  })?.key

  // Columns — the range's days without Sundays.
  const days = useMemo(() => board.days.filter((d) => !d.isSunday), [board.days])

  // Month headings over the day columns — a range may cross a month boundary.
  const months = useMemo(() => {
    const out: { key: string; label: string; span: number }[] = []
    days.forEach((d) => {
      const key = d.date.slice(0, 7)
      const last = out[out.length - 1]
      if (last?.key === key) last.span++
      else out.push({ key, label: `${MONTHS[Number(key.slice(5, 7)) - 1] ?? ''} ${key.slice(0, 4)}`, span: 1 })
    })
    return out
  }, [days])

  // teacherId|date → status / lesson count (quick lookup)
  const entryMap = useMemo(() => {
    const m: Record<string, string> = {}
    board.entries.forEach((e) => (m[`${e.teacherId}|${e.date}`] = e.status))
    return m
  }, [board.entries])
  const lessonMap = useMemo(() => {
    const m: Record<string, number> = {}
    board.lessons.forEach((l) => (m[`${l.teacherId}|${l.date}`] = l.lessons))
    return m
  }, [board.lessons])

  const statusOf = (teacherId: string, date: string) => entryMap[`${teacherId}|${date}`] ?? ''
  /** undefined — the teacher has no lessons that day and cannot be marked. */
  const lessonsOf = (teacherId: string, date: string): number | undefined => lessonMap[`${teacherId}|${date}`]
  const notYet = (startDate: string, date: string) => !!startDate && date < startDate

  const applyEntry = (teacherId: string, date: string, status: string) =>
    setBoard((prev) => {
      const rest = prev.entries.filter((e) => !(e.teacherId === teacherId && e.date === date))
      return status
        ? { ...prev, entries: [...rest, { teacherId, date, status, note: '' }] }
        : { ...prev, entries: rest }
    })

  const cycle = (teacherId: string, date: string) => {
    const teacher = board.teachers.find((x) => x.id === teacherId)
    if (lessonsOf(teacherId, date) === undefined || (teacher && notYet(teacher.startDate, date))) return

    const prevStatus = statusOf(teacherId, date)
    const next = CYCLE[prevStatus] ?? 'present'
    applyEntry(teacherId, date, next)
    setTeacherAttendance(teacherId, date, next || null).catch((e) => {
      applyEntry(teacherId, date, prevStatus)
      alert(e?.response?.data?.message ?? 'Davomatni saqlashda xatolik')
    })
  }

  // A mark left on a day without lessons (older data, a holiday declared later) cannot be changed,
  // only removed — it may still cost the teacher a salary deduction.
  const clearMark = (teacherId: string, date: string) => {
    const prevStatus = statusOf(teacherId, date)
    if (!prevStatus || !confirm("Bu kuni ustozning darsi yo'q. Belgini o'chirasizmi?")) return
    applyEntry(teacherId, date, '')
    setTeacherAttendance(teacherId, date, null).catch((e) => {
      applyEntry(teacherId, date, prevStatus)
      alert(e?.response?.data?.message ?? "Belgini o'chirishda xatolik")
    })
  }

  // The day header touches only the teachers who teach that day — those whose every lesson went to
  // a substitute are left out as well (the server does the same).
  const teachingOn = (date: string) =>
    board.teachers.filter((t) => !notYet(t.startDate, date) && (lessonsOf(t.id, date) ?? 0) > 0)
  const allPresent = (date: string) => {
    const act = teachingOn(date)
    return act.length > 0 && act.every((t) => statusOf(t.id, date) === 'present')
  }

  const toggleDay = (date: string) => {
    const act = teachingOn(date)
    if (act.length === 0) return
    const makePresent = !allPresent(date)
    const ids = new Set(act.map((t) => t.id))
    setBoard((prev) => {
      const rest = prev.entries.filter((e) => !(e.date === date && ids.has(e.teacherId)))
      return {
        ...prev,
        entries: makePresent
          ? [...rest, ...act.map((t) => ({ teacherId: t.id, date, status: 'present', note: '' }))]
          : rest,
      }
    })
    setTeacherAttendanceDay(date, makePresent ? 'present' : null).catch((e) => {
      alert(e?.response?.data?.message ?? 'Davomatni saqlashda xatolik')
      load(range.from, range.to)
    })
  }

  const totals = (teacherId: string) => {
    const c = { present: 0, absent: 0, late: 0, lessons: 0 }
    days.forEach((d) => {
      const st = statusOf(teacherId, d.date)
      if (st === 'present' || st === 'absent' || st === 'late') c[st]++
      c.lessons += lessonsOf(teacherId, d.date) ?? 0
    })
    return c
  }

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
        <div>
          <h2 className="font-semibold text-slate-800">Davomat jadvali</h2>
          <p className="text-xs text-slate-400">
            Faqat o'sha kuni darsi bor ustozlar belgilanadi. Katakni bosing: Keldi → Kelmadi → Kechikdi → bo'sh.
            Sarlavhani bossangiz — o'sha kuni darsi borlarga "Keldi" / tozalash.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <div className="flex items-center gap-2">
            <DatePicker value={range.from} onChange={onFrom} className="w-40" ariaLabel="Davr boshi" />
            <span className="text-slate-400">—</span>
            <DatePicker
              value={range.to}
              min={range.from}
              max={addDaysISO(range.from, MAX_RANGE_DAYS - 1)}
              onChange={onTo}
              className="w-40"
              ariaLabel="Davr oxiri"
            />
          </div>
          <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
            {PRESETS.map((p) => (
              <button
                key={p.key}
                type="button"
                onClick={() => setRange(p.range())}
                className={cn(
                  'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                  activePreset === p.key
                    ? 'bg-white text-slate-800 shadow-sm'
                    : 'text-slate-600 hover:bg-white hover:text-slate-800',
                )}
              >
                {p.label}
              </button>
            ))}
          </div>
        </div>
      </div>

      {loading ? (
        <div className="p-6">
          <Loader label="Yuklanmoqda..." />
        </div>
      ) : error ? (
        <p className="m-4 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{error}</p>
      ) : board.teachers.length === 0 ? (
        <p className="py-8 text-center text-slate-400">Bu davrda darsi bor ustoz yo'q</p>
      ) : (
        <>
          <div className="overflow-x-auto">
            <table className="w-full border-separate border-spacing-0 text-sm">
              <thead className="whitespace-nowrap">
                <tr className="bg-slate-50 text-slate-400">
                  <th
                    rowSpan={2}
                    className="sticky left-0 z-10 bg-slate-50 px-3 py-2 text-left text-xs font-medium uppercase"
                  >
                    F.I.SH
                  </th>
                  {months.map((m) => (
                    <th key={m.key} colSpan={m.span} className="px-1 pt-1.5 text-left text-[11px] font-medium text-slate-500">
                      {m.label}
                    </th>
                  ))}
                  <th rowSpan={2} className="px-2 py-2 text-center text-xs font-medium uppercase">Jami</th>
                  <th rowSpan={2} className="px-2 py-2 text-center text-xs font-medium uppercase">Darslar</th>
                </tr>
                <tr className="bg-slate-50 text-slate-400">
                  {days.map((d) => {
                    const off = d.isHoliday || !d.inQuarter
                    const count = off ? 0 : teachingOn(d.date).length
                    return (
                      <th key={d.date} className="px-0.5 py-1 text-center">
                        <button
                          type="button"
                          disabled={count === 0}
                          onClick={() => toggleDay(d.date)}
                          title={
                            off
                              ? noLessonReason(d)
                              : count === 0
                                ? "Bu kuni darsi bor ustoz yo'q"
                                : allPresent(d.date)
                                  ? 'Bosing: darsi borlarni tozalash'
                                  : `Bosing: darsi bor ${count} ustoz — keldi`
                          }
                          className={cn(
                            'mx-auto flex flex-col items-center rounded-md px-1 py-0.5 transition-colors',
                            count === 0 ? 'cursor-not-allowed opacity-40' : 'hover:bg-brand-50',
                          )}
                        >
                          <span className="text-[10px] font-normal text-slate-400">{weekdayShort(d.date)}</span>
                          <span className="text-xs font-semibold text-slate-600">{d.date.slice(8)}</span>
                        </button>
                      </th>
                    )
                  })}
                </tr>
              </thead>
              <tbody>
                {board.teachers.map((t) => {
                  const c = totals(t.id)
                  return (
                    <tr key={t.id} className="border-t border-slate-100 hover:bg-slate-50/40">
                      <td className="sticky left-0 z-10 whitespace-nowrap bg-white px-3 py-1.5 font-medium text-slate-800">
                        {t.fullName}
                      </td>
                      {days.map((d) => {
                        const st = statusOf(t.id, d.date)
                        const meta = st ? STATUS[st] : null
                        const count = lessonsOf(t.id, d.date)
                        const off = d.isHoliday || !d.inQuarter

                        // No lessons that day (or not hired yet): not markable. An old mark is shown and can
                        // only be removed. "—" = no lessons / holiday, "·" = outside the quarter / not hired yet.
                        if (count === undefined || notYet(t.startDate, d.date)) {
                          const noLessons = count === undefined
                          const reason = noLessons ? noLessonReason(d) : "O'qituvchi hali ishga kirmagan"
                          return (
                            <td key={d.date} className={cn('px-0.5 py-1 text-center', d.isHoliday && 'bg-slate-50/70')}>
                              {meta ? (
                                <button
                                  type="button"
                                  onClick={() => clearMark(t.id, d.date)}
                                  title={`${meta.label} · ${reason} — bosing: belgini o'chirish`}
                                  className={cn(
                                    'mx-auto flex h-7 w-7 items-center justify-center rounded-md text-xs font-bold opacity-50 transition-opacity hover:opacity-80',
                                    meta.cell,
                                  )}
                                >
                                  {meta.short}
                                </button>
                              ) : (
                                <span
                                  title={reason}
                                  className="mx-auto flex h-7 w-7 items-center justify-center rounded-md bg-slate-50 text-slate-300"
                                >
                                  {!noLessons || (off && !d.isHoliday) ? '·' : '—'}
                                </span>
                              )}
                            </td>
                          )
                        }

                        const lessonsLabel = count === 0 ? "darslari o'rindoshga berilgan" : `${count} ta dars`
                        return (
                          <td key={d.date} className="px-0.5 py-1 text-center">
                            <button
                              type="button"
                              onClick={() => cycle(t.id, d.date)}
                              title={`${meta ? meta.label : 'Belgilash'} · ${lessonsLabel}`}
                              className={cn(
                                'mx-auto flex h-7 w-7 items-center justify-center rounded-md text-xs font-bold transition-colors',
                                meta ? meta.cell : 'bg-slate-50 text-slate-300 hover:bg-brand-50',
                              )}
                            >
                              {meta ? meta.short : ''}
                            </button>
                          </td>
                        )
                      })}
                      <td className="whitespace-nowrap px-2 py-1.5 text-center text-xs">
                        <span className="font-semibold text-emerald-600">{c.present}</span>
                        <span className="text-slate-300"> / </span>
                        <span className="font-semibold text-red-600">{c.absent}</span>
                        <span className="text-slate-300"> / </span>
                        <span className="font-semibold text-amber-600">{c.late}</span>
                      </td>
                      <td className="whitespace-nowrap px-2 py-1.5 text-center text-xs font-semibold text-slate-600">
                        {c.lessons}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
          <div className="flex flex-wrap items-center gap-4 px-4 py-3 text-xs text-slate-500">
            <span className="inline-flex items-center gap-1.5">
              <span className="flex h-5 w-5 items-center justify-center rounded bg-emerald-100 font-bold text-emerald-700">✓</span> Keldi
            </span>
            <span className="inline-flex items-center gap-1.5">
              <span className="flex h-5 w-5 items-center justify-center rounded bg-red-100 font-bold text-red-600">✗</span> Kelmadi
            </span>
            <span className="inline-flex items-center gap-1.5">
              <span className="flex h-5 w-5 items-center justify-center rounded bg-amber-100 font-bold text-amber-700">K</span> Kechikdi
            </span>
            <span className="inline-flex items-center gap-1.5">
              <span className="flex h-5 w-5 items-center justify-center rounded bg-slate-50 text-slate-300">—</span> Dars yo'q / bayram
            </span>
            <span className="ml-auto text-slate-400">Jami: Keldi / Kelmadi / Kechikdi · Darslar: davrdagi darslar soni</span>
          </div>
        </>
      )}
    </Card>
  )
}
