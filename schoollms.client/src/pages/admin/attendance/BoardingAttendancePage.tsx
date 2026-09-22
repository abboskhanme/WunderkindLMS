/**
 * Kechki dars va yotoqxona davomati (mijoz, 2026-09-23).
 *
 * Ikki yorliq — ikki xil odam oladi: "Kechki dars" (keldi / kelmadi / sababli) va "Yotoqxona"
 * (joyida / yo'q / ruxsat bilan). Kuniga bir marta. Ro'yxat yo'nalish guruhlari bo'yicha;
 * guruhi yo'q yotoqxona o'quvchilari o'z sinfi bilan "Guruhsiz" bo'limida.
 *
 * Yotoqxona abonementi yo'q o'quvchi KULRANG: belgilanmaydi, hisobga kirmaydi, xabar ketmaydi.
 * "Kelmadi / Yo'q" saqlanganda ota-onaga Telegram xabar ketadi (bir holat — bir marta).
 * Kunduzgi dars davomati (jurnal) bu yerda yo'q va tegilmaydi.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { BedDouble, Moon, Search } from 'lucide-react'
import {
  getBoardingDay,
  saveBoarding,
  type BoardingDay,
  type BoardingSection,
  type BoardingSession,
  type BoardingStatus,
} from '@/api/services/boardingAttendance'
import { useAuth } from '@/context/auth-context'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { DatePicker } from '@/components/ui/DatePicker'
import { Toast } from '@/components/ui/Toast'
import { cn } from '@/lib/utils'

const pad = (n: number) => String(n).padStart(2, '0')
const todayISO = () => {
  const d = new Date()
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

const SESSIONS: { key: BoardingSession; label: string; perm: string; icon: typeof Moon }[] = [
  { key: 'evening', label: 'Kechki dars', perm: 'attendanceEvening', icon: Moon },
  { key: 'dorm', label: 'Yotoqxona', perm: 'attendanceDorm', icon: BedDouble },
]

const STATUSES: BoardingStatus[] = ['present', 'absent', 'excused']
const LABELS: Record<BoardingSession, Record<BoardingStatus, { letter: string; title: string }>> = {
  evening: {
    present: { letter: 'K', title: 'Keldi' },
    absent: { letter: 'Y', title: 'Kelmadi' },
    excused: { letter: 'S', title: 'Sababli' },
  },
  dorm: {
    present: { letter: 'J', title: 'Joyida' },
    absent: { letter: 'Y', title: "Yo'q" },
    excused: { letter: 'R', title: 'Ruxsat bilan' },
  },
}

const chip = (active: boolean, tone: BoardingStatus, disabled = false) =>
  cn(
    'flex h-8 w-8 items-center justify-center rounded-full text-xs font-semibold transition-colors',
    disabled && 'cursor-not-allowed opacity-40',
    active && tone === 'present' && 'bg-emerald-500 text-white',
    active && tone === 'absent' && 'bg-red-500 text-white',
    active && tone === 'excused' && 'bg-amber-500 text-white',
    !active && 'bg-slate-100 text-slate-400',
    !active && !disabled && 'hover:bg-slate-200',
  )

export function BoardingAttendancePage() {
  const { user } = useAuth()
  const [params, setParams] = useSearchParams()
  const allowed = SESSIONS.filter((s) => !user?.permissions || user.permissions.includes(s.perm))
  const requested = params.get('session') as BoardingSession | null
  const session: BoardingSession =
    allowed.find((s) => s.key === requested)?.key ?? allowed[0]?.key ?? 'evening'

  const [date, setDate] = useState(todayISO())
  const [day, setDay] = useState<BoardingDay | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  /** Saqlanmagan belgilar: bo'lim kaliti → o'quvchi → holat. */
  const [draft, setDraft] = useState<Record<string, Record<string, BoardingStatus>>>({})
  const [saving, setSaving] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [hideIneligible, setHideIneligible] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const closeToast = useCallback(() => setToast(null), [])

  // Qayta yuklash — saqlagandan keyin (server hisoblagan "belgilandi"/"yo'q" sonlari uchun).
  const [reloadTick, setReloadTick] = useState(0)
  const load = () => setReloadTick((n) => n + 1)

  useEffect(() => {
    let active = true
    const t = setTimeout(() => {
      setLoading(true)
      setError(null)
      getBoardingDay(date, session)
        .then((d) => {
          if (!active) return
          setDay(d)
          setDraft({})
        })
        .catch((e) => {
          if (active)
            setError((e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? "Yuklab bo'lmadi")
        })
        .finally(() => active && setLoading(false))
    }, 0)
    return () => {
      active = false
      clearTimeout(t)
    }
  }, [date, session, reloadTick])

  // Sukut — BELGILANMAGAN (mijoz, 2026-09-23): hech kim o'z-o'zidan "keldi/joyida" emas.
  const statusOf = (sec: BoardingSection, id: string, saved: BoardingStatus | null): BoardingStatus | null =>
    draft[sec.key]?.[id] ?? saved ?? null
  /** "Saqlash" belgisizlar bilan bosilgan bo'limlar — belgisiz qatorlar sariq. */
  const [flagged, setFlagged] = useState<Set<string>>(new Set())

  const setAll = (sec: BoardingSection, st: BoardingStatus) =>
    setDraft((prev) => ({
      ...prev,
      [sec.key]: Object.fromEntries(sec.students.filter((x) => x.eligible).map((x) => [x.studentId, st])),
    }))

  const setMark = (secKey: string, id: string, st: BoardingStatus) => {
    setError(null)
    setDraft((prev) => ({ ...prev, [secKey]: { ...prev[secKey], [id]: st } }))
  }

  const saveSection = async (sec: BoardingSection) => {
    const eligible = sec.students.filter((s) => s.eligible)
    const unmarked = eligible.filter((s) => !statusOf(sec, s.studentId, s.status)).length
    if (unmarked > 0) {
      // Hamma belgilanmaguncha saqlanmaydi — belgisiz o'quvchi hisobdan tushib qolmasin.
      setFlagged((prev) => new Set(prev).add(sec.key))
      setError(`${sec.title}: ${unmarked} ta o'quvchi belgilanmagan — avval hammasini belgilang.`)
      return
    }
    const marks = eligible.map((s) => ({ studentId: s.studentId, status: statusOf(sec, s.studentId, s.status)! }))
    if (marks.length === 0) return
    setSaving(sec.key)
    setError(null)
    try {
      const res = await saveBoarding(date, session, marks)
      setToast(
        `${sec.title} saqlandi` + (res.notified > 0 ? ` · ${res.notified} ta ota-onaga xabar ketdi` : ''),
      )
      load()
    } catch (e) {
      setError((e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? "Saqlab bo'lmadi")
    } finally {
      setSaving(null)
    }
  }

  const needle = search.trim().toLocaleLowerCase('uz')
  const sections = useMemo(
    () =>
      (day?.sections ?? []).map((sec) => ({
        ...sec,
        students: sec.students.filter(
          (s) =>
            (!hideIneligible || s.eligible) && (!needle || s.fullName.toLocaleLowerCase('uz').includes(needle)),
        ),
      })),
    [day, hideIneligible, needle],
  )

  if (allowed.length === 0) {
    return <p className="text-slate-500">Bu bo'lim uchun ruxsatingiz yo'q.</p>
  }

  const L = LABELS[session]

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Kechki va yotoqxona davomati</h1>
          <p className="text-sm text-slate-400">
            Faqat yotoqxona abonementi bor o'quvchilar belgilanadi · {L.present.title} (yashil), {L.absent.title}{' '}
            (qizil), {L.excused.title} (sariq)
          </p>
        </div>
        {day && (
          <p className="text-sm text-slate-600">
            <b>{day.marked}</b> / {day.eligible} belgilandi · <span className="text-red-600">{day.absent} {L.absent.title.toLowerCase()}</span>
          </p>
        )}
      </header>

      <Card className="flex flex-wrap items-end gap-4">
        <DatePicker label="Kun" value={date} onChange={setDate} max={todayISO()} />
        <div className="flex gap-1 rounded-xl bg-slate-100 p-1">
          {allowed.map((s) => (
            <button
              key={s.key}
              type="button"
              onClick={() => setParams({ session: s.key })}
              className={cn(
                'flex items-center gap-1.5 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
                session === s.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              <s.icon className="h-4 w-4" /> {s.label}
            </button>
          ))}
        </div>
        <label className="flex h-10 min-w-[14rem] flex-1 items-center gap-2 rounded-xl border border-slate-200 bg-white px-3">
          <Search className="h-4 w-4 text-slate-400" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="O'quvchi ismi"
            className="min-w-0 flex-1 bg-transparent text-sm outline-none"
          />
        </label>
        <label className="flex items-center gap-2 text-sm text-slate-600">
          <input
            type="checkbox"
            checked={hideIneligible}
            onChange={(e) => setHideIneligible(e.target.checked)}
            className="h-4 w-4 accent-brand-600"
          />
          Abonementsizlarni yashirish
        </label>
      </Card>

      {error && <p className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{error}</p>}

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : sections.length === 0 ? (
        <Card>
          <p className="py-10 text-center text-sm text-slate-400">
            Ro'yxat bo'sh. Yo'nalish guruhlarini "O'quv bo'limi → Guruhlar"da belgilang, yotoqxona
            abonementini esa o'quvchi kartasida qo'shing.
          </p>
        </Card>
      ) : (
        <div className="space-y-4">
          {sections.map((sec) => {
            const done = sec.eligible > 0 && sec.marked === sec.eligible
            return (
              <Card key={sec.key} className="p-0">
                <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
                  <div className="min-w-0">
                    <p className="truncate font-semibold text-slate-800">{sec.title}</p>
                    <p className="text-xs text-slate-400">
                      {sec.eligible} ta yotoqxonada · {sec.marked} belgilangan
                      {sec.absent > 0 && <span className="text-red-600"> · {sec.absent} {L.absent.title.toLowerCase()}</span>}
                      {done && <span className="text-emerald-600"> · tugadi</span>}
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    {STATUSES.map((st) => (
                      <button
                        key={st}
                        type="button"
                        disabled={sec.eligible === 0}
                        onClick={() => setAll(sec, st)}
                        title={`Hammasi — ${L[st].title}`}
                        aria-label={`Hammasi — ${L[st].title}`}
                        className={chip(true, st, sec.eligible === 0)}
                      >
                        {L[st].letter}
                      </button>
                    ))}
                  </div>
                </div>
                <ul className="divide-y divide-slate-50">
                  {sec.students.map((s, i) => {
                    const cur = statusOf(sec, s.studentId, s.status)
                    return (
                      <li
                        key={s.studentId}
                        className={cn(
                          'flex items-center gap-3 px-4 py-2',
                          !s.eligible && 'bg-slate-50/60',
                          s.eligible && !cur && flagged.has(sec.key) && 'bg-amber-50/70',
                        )}
                        title={s.eligible ? undefined : "Shu kuni yotoqxona abonementi yo'q — davomat olinmaydi"}
                      >
                        <span className="w-6 text-right text-xs tabular-nums text-slate-400">{i + 1}</span>
                        <span className="min-w-0 flex-1">
                          <span className={cn('block truncate', s.eligible ? 'text-slate-800' : 'text-slate-400')}>
                            {s.fullName}
                          </span>
                          <span className="text-[11px] text-slate-400">
                            {s.className || '—'}
                            {!s.eligible && ' · abonement yo\'q'}
                          </span>
                        </span>
                        <span className="flex gap-1.5">
                          {STATUSES.map((st) => (
                            <button
                              key={st}
                              type="button"
                              disabled={!s.eligible}
                              title={L[st].title}
                              onClick={() => setMark(sec.key, s.studentId, st)}
                              className={chip(s.eligible && cur === st, st, !s.eligible)}
                            >
                              {L[st].letter}
                            </button>
                          ))}
                        </span>
                      </li>
                    )
                  })}
                </ul>
                {/* Saqlash — pastda, "Davomat belgilash" sahifasidagidek (mijoz, 2026-09-23). */}
                {(() => {
                  const left = sec.students.filter((x) => x.eligible && !statusOf(sec, x.studentId, x.status)).length
                  return (
                    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 px-4 py-3">
                      <span className="text-sm text-slate-400">
                        {left > 0 ? (
                          <span className="text-amber-600">{left} ta o'quvchi hali belgilanmagan</span>
                        ) : sec.eligible > 0 ? (
                          'Hammasi belgilandi'
                        ) : (
                          "Yotoqxona abonementi bor o'quvchi yo'q"
                        )}
                      </span>
                      <Button onClick={() => void saveSection(sec)} disabled={saving === sec.key || sec.eligible === 0}>
                        {saving === sec.key ? 'Saqlanmoqda...' : 'Saqlash'}
                      </Button>
                    </div>
                  )
                })()}
              </Card>
            )
          })}
        </div>
      )}

      <Toast message={toast} onClose={closeToast} />
    </div>
  )
}
