/**
 * Kechki dars va yotoqxona davomati (mijoz, 2026-09-23; ikki tugma — 2026-09-26).
 *
 * "Davomat belgilash" bilan bir xil: ikki tugma — K/J (keldi / joyida; ostida kech keldi
 * sabablari) va Y (kelmadi / yo'q; ostida sabab, sukut — Sababsiz). Kuniga bir marta, ikki
 * yorliq — ikki xil odam oladi. Ro'yxat yo'nalish guruhlari bo'yicha; guruhi yo'q yotoqxona
 * o'quvchilari o'z sinfi bilan "Guruhsiz" bo'limida.
 *
 * Saqlashda holat: keldi → `present`; kelmadi + Sababsiz → `absent` (ota-onaga Telegram xabar,
 * bir holat — bir marta); kelmadi + boshqa sabab → `excused`. Aniq sabab `reasonId` da.
 *
 * Yotoqxona abonementi yo'q o'quvchi KULRANG: belgilanmaydi, hisobga kirmaydi, xabar ketmaydi.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { BedDouble, Moon, Search } from 'lucide-react'
import type { AbsenceReason } from '@/types'
import {
  getBoardingDay,
  saveBoarding,
  type BoardingDay,
  type BoardingSection,
  type BoardingSession,
  type BoardingStatus,
  type BoardingStudent,
} from '@/api/services/boardingAttendance'
import { getSettings } from '@/api/services/settings'
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

/** Ekrandagi ikki holat. */
type Mark = 'present' | 'absent'
const MARKS: Mark[] = ['present', 'absent']
const LABELS: Record<BoardingSession, Record<Mark, { letter: string; title: string }>> = {
  evening: { present: { letter: 'K', title: 'Keldi' }, absent: { letter: 'Y', title: 'Kelmadi' } },
  dorm: { present: { letter: 'J', title: 'Joyida' }, absent: { letter: 'Y', title: "Yo'q" } },
}

/** Belgi: holat + aniq sabab (keldi — kechikish yoki yo'q; kelmadi — sabab yoki sukut). */
interface Pick {
  mark: Mark
  reasonId: string | null
}

const chip = (active: boolean, tone: Mark, disabled = false) =>
  cn(
    'flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-xs font-semibold transition-colors',
    disabled && 'cursor-not-allowed opacity-40',
    active && tone === 'present' && 'bg-emerald-500 text-white',
    active && tone === 'absent' && 'bg-red-500 text-white',
    !active && 'bg-slate-100 text-slate-400',
    !active && !disabled && 'hover:bg-slate-200',
  )

export function BoardingAttendancePage() {
  const { user } = useAuth()
  const [params, setParams] = useSearchParams()
  const allowed = SESSIONS.filter(
    (s) => !user?.permissions || user.permissions.includes(s.perm) || user.permissions.includes(`${s.perm}:view`),
  )
  const requested = params.get('session') as BoardingSession | null
  const session: BoardingSession =
    allowed.find((s) => s.key === requested)?.key ?? allowed[0]?.key ?? 'evening'

  const [date, setDate] = useState(todayISO())
  const [day, setDay] = useState<BoardingDay | null>(null)
  const [reasons, setReasons] = useState<AbsenceReason[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  /** Saqlanmagan belgilar: bo'lim kaliti → o'quvchi → belgi. */
  const [draft, setDraft] = useState<Record<string, Record<string, Pick>>>({})
  const [saving, setSaving] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [hideIneligible, setHideIneligible] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const closeToast = useCallback(() => setToast(null), [])

  // Qayta yuklash — saqlagandan keyin (server hisoblagan "belgilandi"/"yo'q" sonlari uchun).
  const [reloadTick, setReloadTick] = useState(0)
  const load = () => setReloadTick((n) => n + 1)

  useEffect(() => {
    getSettings()
      .then((s) => setReasons(s.absenceReasons))
      .catch(() => setReasons([]))
  }, [])

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

  // Sabablar: K ostida — kechikish (isLate); Y ostida — Sababsiz (sukut) va qolgan yo'qlik sabablari.
  const lateChoices = reasons.filter((r) => r.isLate)
  const absentAll = reasons.filter((r) => !r.isLate)
  const unexcused = absentAll.find((r) => /sababsiz/i.test(r.name)) ?? absentAll[0] ?? null
  const absentChoices = unexcused ? [unexcused, ...absentAll.filter((r) => r.id !== unexcused.id)] : absentAll
  const lateIds = new Set(lateChoices.map((r) => r.id))

  /** Saqlangan qator → ekrandagi belgi. Sabab yo'q eski "sababli" — Y, sukutdan boshqa birinchi sabab. */
  const savedPick = (s: BoardingStudent): Pick | null => {
    if (!s.status) return null
    if (s.status === 'present') return { mark: 'present', reasonId: s.reasonId && lateIds.has(s.reasonId) ? s.reasonId : null }
    const fallback =
      s.status === 'excused' ? (absentChoices.find((r) => r.id !== unexcused?.id)?.id ?? null) : (unexcused?.id ?? null)
    return { mark: 'absent', reasonId: s.reasonId ?? fallback }
  }
  // Sukut — BELGILANMAGAN (mijoz, 2026-09-23): hech kim o'z-o'zidan "keldi/joyida" emas.
  const pickOf = (sec: BoardingSection, s: BoardingStudent): Pick | null => draft[sec.key]?.[s.studentId] ?? savedPick(s)

  /** "Saqlash" belgisizlar bilan bosilgan bo'limlar — belgisiz qatorlar sariq. */
  const [flagged, setFlagged] = useState<Set<string>>(new Set())

  const defaultPick = (mark: Mark): Pick => ({ mark, reasonId: mark === 'absent' ? (unexcused?.id ?? null) : null })

  const setAll = (sec: BoardingSection, mark: Mark) =>
    setDraft((prev) => ({
      ...prev,
      [sec.key]: Object.fromEntries(sec.students.filter((x) => x.eligible).map((x) => [x.studentId, defaultPick(mark)])),
    }))

  const setPick = (secKey: string, id: string, pick: Pick) => {
    setError(null)
    setDraft((prev) => ({ ...prev, [secKey]: { ...prev[secKey], [id]: pick } }))
  }

  const saveSection = async (sec: BoardingSection) => {
    const eligible = sec.students.filter((s) => s.eligible)
    const unmarked = eligible.filter((s) => !pickOf(sec, s)).length
    if (unmarked > 0) {
      // Hamma belgilanmaguncha saqlanmaydi — belgisiz o'quvchi hisobdan tushib qolmasin.
      setFlagged((prev) => new Set(prev).add(sec.key))
      setError(`${sec.title}: ${unmarked} ta o'quvchi belgilanmagan — avval hammasini belgilang.`)
      return
    }
    const marks = eligible.map((s) => {
      const p = pickOf(sec, s)!
      const status: BoardingStatus =
        p.mark === 'present' ? 'present' : !p.reasonId || p.reasonId === unexcused?.id ? 'absent' : 'excused'
      return { studentId: s.studentId, status, reasonId: p.reasonId }
    })
    if (marks.length === 0) return
    setSaving(sec.key)
    setError(null)
    try {
      const res = await saveBoarding(date, session, marks)
      setToast(`${sec.title} saqlandi` + (res.notified > 0 ? ` · ${res.notified} ta ota-onaga xabar ketdi` : ''))
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
          (s) => (!hideIneligible || s.eligible) && (!needle || s.fullName.toLocaleLowerCase('uz').includes(needle)),
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
            Faqat yotoqxona abonementi bor o'quvchilar belgilanadi · {L.present.letter} — {L.present.title.toLowerCase()}
            {lateChoices.length > 0 && ' (ostidan: kech keldi)'} · {L.absent.letter} — {L.absent.title.toLowerCase()}
            {unexcused && `, sukut: ${unexcused.name}`}
          </p>
        </div>
        {day && (
          <p className="text-sm text-slate-600">
            <b>{day.marked}</b> / {day.eligible} belgilandi ·{' '}
            <span className="text-red-600">
              {day.absent} {L.absent.title.toLowerCase()}
            </span>
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
            Ro'yxat bo'sh. Yo'nalish guruhlarini "O'quv bo'limi → Guruhlar"da belgilang, yotoqxona abonementini esa
            o'quvchi kartasida qo'shing.
          </p>
        </Card>
      ) : (
        <div className="space-y-4">
          {sections.map((sec) => {
            const done = sec.eligible > 0 && sec.marked === sec.eligible
            return (
              <Card key={sec.key} className="p-0">
                {/* Sarlavha: ustun tugmalari o'ngda — pastdagi qator tugmalari bilan AYNAN bir ustunda
                    (mijoz, 2026-09-26: "ustuni ustuniga mos kelsin"). Qatorlar o'ralmaydi. */}
                <div className="flex items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-semibold text-slate-800">{sec.title}</p>
                    <p className="truncate text-xs text-slate-400">
                      {sec.eligible} ta yotoqxonada · {sec.marked} belgilangan
                      {sec.absent > 0 && (
                        <span className="text-red-600">
                          {' '}
                          · {sec.absent} {L.absent.title.toLowerCase()}
                        </span>
                      )}
                      {done && <span className="text-emerald-600"> · tugadi</span>}
                    </p>
                  </div>
                  <div className="flex shrink-0 items-center gap-1.5">
                    {MARKS.map((m) => (
                      <button
                        key={m}
                        type="button"
                        disabled={sec.eligible === 0}
                        onClick={() => setAll(sec, m)}
                        title={`Hammasi — ${L[m].title}`}
                        aria-label={`Hammasi — ${L[m].title}`}
                        className={chip(true, m, sec.eligible === 0)}
                      >
                        {L[m].letter}
                      </button>
                    ))}
                  </div>
                </div>
                <ul className="divide-y divide-slate-50">
                  {sec.students.map((s, i) => {
                    const cur = s.eligible ? pickOf(sec, s) : null
                    return (
                      <li
                        key={s.studentId}
                        className={cn(
                          'px-4 py-2',
                          !s.eligible && 'bg-slate-50/60',
                          s.eligible && !cur && flagged.has(sec.key) && 'bg-amber-50/70',
                        )}
                        title={s.eligible ? undefined : "Shu kuni yotoqxona abonementi yo'q — davomat olinmaydi"}
                      >
                        <div className="flex items-center justify-between gap-3">
                          <span className="flex min-w-0 flex-1 items-center gap-3">
                            <span className="w-6 shrink-0 text-right text-xs tabular-nums text-slate-400">{i + 1}</span>
                            <span className="min-w-0">
                              <span className={cn('block truncate', s.eligible ? 'text-slate-800' : 'text-slate-400')}>
                                {s.fullName}
                              </span>
                              <span className="block truncate text-[11px] text-slate-400">
                                {s.className || '—'}
                                {!s.eligible && " · abonement yo'q"}
                              </span>
                            </span>
                          </span>
                          <span className="flex shrink-0 items-center gap-1.5">
                            {MARKS.map((m) => (
                              <button
                                key={m}
                                type="button"
                                disabled={!s.eligible}
                                title={L[m].title}
                                aria-label={`${s.fullName} — ${L[m].title}`}
                                onClick={() => setPick(sec.key, s.studentId, cur?.mark === m ? cur : defaultPick(m))}
                                className={chip(!!cur && cur.mark === m, m, !s.eligible)}
                              >
                                {L[m].letter}
                              </button>
                            ))}
                          </span>
                        </div>

                        {/* Sabab — ism OSTIDA (Davomat belgilash bilan bir xil). */}
                        {cur?.mark === 'present' && lateChoices.length > 0 && (
                          <div className="mt-1 pl-9">
                            <select
                              value={cur.reasonId ?? ''}
                              onChange={(e) => setPick(sec.key, s.studentId, { mark: 'present', reasonId: e.target.value || null })}
                              aria-label={`${s.fullName} — ${L.present.title.toLowerCase()} / kech keldi`}
                              className={cn(
                                'min-w-0 max-w-full rounded-lg border px-2 py-0.5 text-xs outline-none',
                                cur.reasonId
                                  ? 'border-amber-200 bg-amber-50 font-medium text-amber-800'
                                  : 'border-transparent bg-transparent text-slate-400 hover:border-slate-200',
                              )}
                            >
                              <option value="">{L.present.title}</option>
                              {lateChoices.map((r) => (
                                <option key={r.id} value={r.id}>
                                  {r.name}
                                </option>
                              ))}
                            </select>
                          </div>
                        )}
                        {cur?.mark === 'absent' && absentChoices.length > 1 && (
                          <div className="mt-1 pl-9">
                            <select
                              value={cur.reasonId ?? unexcused?.id ?? ''}
                              onChange={(e) => setPick(sec.key, s.studentId, { mark: 'absent', reasonId: e.target.value })}
                              aria-label={`${s.fullName} — sabab`}
                              className="min-w-0 max-w-full rounded-lg border border-red-200 bg-red-50 px-2 py-0.5 text-xs font-medium text-red-700 outline-none focus:border-red-400"
                            >
                              {absentChoices.map((r) => (
                                <option key={r.id} value={r.id}>
                                  {r.name}
                                </option>
                              ))}
                            </select>
                          </div>
                        )}
                      </li>
                    )
                  })}
                </ul>
                {/* Saqlash — pastda, "Davomat belgilash" sahifasidagidek (mijoz, 2026-09-23). */}
                {(() => {
                  const left = sec.students.filter((x) => x.eligible && !pickOf(sec, x)).length
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
