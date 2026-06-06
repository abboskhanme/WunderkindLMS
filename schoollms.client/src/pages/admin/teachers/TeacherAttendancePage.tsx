import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import type { HubConnection } from '@microsoft/signalr'
import { RefreshCw, Wifi, WifiOff, CheckCircle2, Search } from 'lucide-react'
import {
  getTeacherAttendance,
  setTeacherAttendance,
  setTeacherAttendanceDay,
  getAttendanceDashboard,
  syncTurnstile,
  type TeacherAttendanceBoard,
  type AttendanceDashboard,
} from '@/api/services/teacherAttendance'
import { connectLiveTopic } from '@/api/services/live'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

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

const weekdayShort = (iso: string) => ['Ya', 'Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh'][new Date(iso).getDay()] ?? ''
const isSunday = (iso: string) => new Date(iso).getDay() === 0
const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
const currentMonth = () => today().slice(0, 7)
const initials = (name: string) =>
  name.split(' ').filter(Boolean).slice(0, 2).map((s) => s[0]?.toUpperCase()).join('')
const syncLabel = (iso: string) => (iso && iso.length >= 16 ? `${iso.slice(0, 10)} ${iso.slice(11, 16)}` : '—')

export function TeacherAttendancePage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">O'qituvchilar davomati</h1>
        <p className="text-sm text-slate-400">
          Turniket/FaceID qurilmasidan avtomatik yuklanadi. Kunlik holat — kim keldi, soat nechada, kechikdimi.
        </p>
      </div>
      <MonthlyGrid />
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

  useEffect(() => load(date), [date, load])

  // Real-time: turniketdan yangi o'tish kelganda joriy kun dashboard'ini jonli yangilaymiz (SignalR).
  const reloadRef = useRef<() => void>(() => {})
  reloadRef.current = () => load(date)
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

  // Holatni qo'lda almashtirish (admin tuzatishi mumkin — manual sifatida saqlanadi)
  const cycle = (teacherId: string, current: string) => {
    if (!dash?.inTeachingPeriod) return
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
          <input
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none focus:border-brand-400"
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

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : !dash || dash.rows.length === 0 ? (
        <p className="py-8 text-center text-slate-400">O'qituvchi yo'q</p>
      ) : !dash.inTeachingPeriod ? (
        <p className="py-8 text-center text-slate-400">Bu kun dars jadvali (chorak) davrida emas</p>
      ) : visibleRows.length === 0 ? (
        <p className="py-8 text-center text-slate-400">Filtrga mos o'qituvchi topilmadi</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
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
                          {!r.deviceUserId && (
                            <div className="text-[11px] text-amber-500">qurilma ID yo'q</div>
                          )}
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
                        onClick={() => cycle(r.teacherId, r.status)}
                        title="Bosing: qo'lda o'zgartirish"
                        className={cn(
                          'inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium transition-opacity hover:opacity-80',
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

/* ---------------- Oylik jadval (qo'lda belgilash) ---------------- */

function MonthlyGrid() {
  const [month, setMonth] = useState(currentMonth())
  const [board, setBoard] = useState<TeacherAttendanceBoard>({ teachers: [], entries: [], quarters: [] })
  const [loading, setLoading] = useState(true)

  const load = useCallback((m: string) => {
    setLoading(true)
    getTeacherAttendance(m)
      .then(setBoard)
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => load(month), [month, load])

  // Oydagi ish kunlari ("yyyy-MM-dd") — yakshanba (dam olish) chiqarib tashlanadi.
  const days = useMemo(() => {
    const [y, mo] = month.split('-').map(Number)
    const count = new Date(y, mo, 0).getDate()
    return Array.from({ length: count }, (_, i) => `${month}-${String(i + 1).padStart(2, '0')}`)
      .filter((d) => !isSunday(d))
  }, [month])

  // Sana dars jadvali (chorak) davrida ekanmi. Choraklar bo'sh = cheklov yo'q (hammasi ochiq).
  const inQuarter = (date: string) =>
    board.quarters.length === 0 || board.quarters.some((q) => date >= q.start && date <= q.end)

  // teacherId|date → status (tezkor qidiruv uchun)
  const map = useMemo(() => {
    const m: Record<string, string> = {}
    board.entries.forEach((e) => (m[`${e.teacherId}|${e.date}`] = e.status))
    return m
  }, [board.entries])

  const statusOf = (teacherId: string, date: string) => map[`${teacherId}|${date}`] ?? ''
  const notYet = (startDate: string, date: string) => !!startDate && date < startDate

  const cycle = (teacherId: string, date: string) => {
    const teacher = board.teachers.find((x) => x.id === teacherId)
    if (!inQuarter(date) || (teacher && notYet(teacher.startDate, date))) return

    const prevStatus = statusOf(teacherId, date)
    const next = CYCLE[prevStatus] ?? 'present'
    const apply = (status: string) =>
      setBoard((prev) => {
        const rest = prev.entries.filter((e) => !(e.teacherId === teacherId && e.date === date))
        return status
          ? { ...prev, entries: [...rest, { teacherId, date, status, note: '' }] }
          : { ...prev, entries: rest }
      })

    apply(next)
    setTeacherAttendance(teacherId, date, next || null).catch((e) => {
      apply(prevStatus)
      alert(e?.response?.data?.message ?? 'Davomatni saqlashda xatolik')
    })
  }

  const activeOn = (date: string) =>
    inQuarter(date) ? board.teachers.filter((t) => !notYet(t.startDate, date)) : []
  const allPresent = (date: string) => {
    const act = activeOn(date)
    return act.length > 0 && act.every((t) => statusOf(t.id, date) === 'present')
  }

  const toggleDay = (date: string) => {
    if (!inQuarter(date)) return
    const makePresent = !allPresent(date)
    const act = activeOn(date)
    setBoard((prev) => {
      const rest = prev.entries.filter((e) => e.date !== date)
      return {
        ...prev,
        entries: makePresent
          ? [...rest, ...act.map((t) => ({ teacherId: t.id, date, status: 'present', note: '' }))]
          : rest,
      }
    })
    setTeacherAttendanceDay(date, makePresent ? 'present' : null).catch(() => load(month))
  }

  const counts = (teacherId: string) => {
    const c = { present: 0, absent: 0, late: 0 }
    days.forEach((d) => {
      const st = statusOf(teacherId, d)
      if (st in c) c[st as keyof typeof c]++
    })
    return c
  }

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
        <div>
          <h2 className="font-semibold text-slate-800">Oylik jadval</h2>
          <p className="text-xs text-slate-400">
            Katakni bosing: Keldi → Kelmadi → Kechikdi → bo'sh. Sarlavhani bossangiz — kun hammaga "Keldi" / tozalash.
          </p>
        </div>
        <input
          type="month"
          value={month}
          onChange={(e) => setMonth(e.target.value)}
          className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none focus:border-brand-400"
        />
      </div>

      {loading ? (
        <div className="p-6">
          <Loader label="Yuklanmoqda..." />
        </div>
      ) : board.teachers.length === 0 ? (
        <p className="py-8 text-center text-slate-400">O'qituvchi yo'q</p>
      ) : (
        <>
          <div className="overflow-x-auto">
            <table className="w-full border-separate border-spacing-0 text-sm">
              <thead>
                <tr className="bg-slate-50 text-slate-400">
                  <th className="sticky left-0 z-10 bg-slate-50 px-3 py-2 text-left text-xs font-medium uppercase">
                    F.I.SH
                  </th>
                  {days.map((d) => {
                    const off = !inQuarter(d)
                    return (
                      <th key={d} className="px-0.5 py-1 text-center">
                        <button
                          type="button"
                          disabled={off}
                          onClick={() => toggleDay(d)}
                          title={
                            off
                              ? 'Dars jadvali (chorak) davri emas'
                              : allPresent(d)
                                ? 'Bosing: shu kunni tozalash'
                                : 'Bosing: hammasi keldi'
                          }
                          className={cn(
                            'mx-auto flex flex-col items-center rounded-md px-1 py-0.5 transition-colors',
                            off ? 'cursor-not-allowed opacity-40' : 'hover:bg-brand-50',
                          )}
                        >
                          <span className="text-[10px] font-normal text-slate-400">{weekdayShort(d)}</span>
                          <span className="text-xs font-semibold text-slate-600">{d.slice(8)}</span>
                        </button>
                      </th>
                    )
                  })}
                  <th className="px-2 py-2 text-center text-xs font-medium uppercase">Jami</th>
                </tr>
              </thead>
              <tbody>
                {board.teachers.map((t) => {
                  const c = counts(t.id)
                  return (
                    <tr key={t.id} className="border-t border-slate-100 hover:bg-slate-50/40">
                      <td className="sticky left-0 z-10 whitespace-nowrap bg-white px-3 py-1.5 font-medium text-slate-800">
                        {t.fullName}
                      </td>
                      {days.map((d) => {
                        const offQuarter = !inQuarter(d)
                        if (offQuarter || notYet(t.startDate, d)) {
                          return (
                            <td key={d} className="px-0.5 py-1 text-center">
                              <span
                                title={offQuarter ? 'Dars jadvali (chorak) davri emas' : "O'qituvchi hali ishga kirmagan"}
                                className="mx-auto flex h-7 w-7 items-center justify-center rounded-md bg-slate-50 text-slate-200"
                              >
                                ·
                              </span>
                            </td>
                          )
                        }
                        const st = statusOf(t.id, d)
                        const meta = st ? STATUS[st] : null
                        return (
                          <td key={d} className="px-0.5 py-1 text-center">
                            <button
                              type="button"
                              onClick={() => cycle(t.id, d)}
                              title={meta ? meta.label : 'Belgilash'}
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
            <span className="ml-auto text-slate-400">Jami: Keldi / Kelmadi / Kechikdi</span>
          </div>
        </>
      )}
    </Card>
  )
}
