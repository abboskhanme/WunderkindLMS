import { useCallback, useEffect, useMemo, useState } from 'react'
import { AlertTriangle, CheckCircle2, Search } from 'lucide-react'
import type {
  DailyAttendanceClassDay,
  DailyAttendanceLesson,
  DailyAttendanceOverview,
} from '@/api/services/attendanceMarking'
import {
  getDailyClass,
  getDailyOverview,
  saveDailyAttendance,
} from '@/api/services/attendanceMarking'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Toast } from '@/components/ui/Toast'
import { Select } from '@/components/ui/Input'
import { DatePicker } from '@/components/ui/DatePicker'
import { cn } from '@/lib/utils'

/* ==========================================================================
   DAVOMAT — sinf va dars TANLANADI (dropdown), keyin uchta belgi

   Mijoz, 2026-09-18: "davomat qilish joyi juda sodda oddiy bo'lishi kerak
   yani sinf tanlansa o'sha soatda darsiga ko'ra sinfni davomat qilish mumkin
   bo'lsin ... shunchaki keldi-yashil, kelmadi-qizil, sababli-sariq, va
   bulardan butun ustunni belgilash uchun tepada bitta button ham", va
   ketidan: "sinf va darsni dropdown orqali tanlansin."

   EKRAN BIR QATOR TANLOV + BITTA RO'YXAT: Kun · Sinf · Dars → o'quvchilar
   va har birida uchta doira. Ro'yxat sarlavhasidagi uchta doira BUTUN
   USTUNNI bir bosishda belgilaydi.

   UCHTA BELGI KATALOGDAGI SABABGA BOG'LANADI: qizil — "Sababsiz", sariq —
   sababli tur (kasal / ruxsat bilan). Qaysi tur ekanini SERVER hal qiladi va
   nomini ham qaytaradi (`DailyAttendanceService.ResolveReasons`) — tanlov
   ekranda ochiq yozib qo'yiladi.

   NEGA "KELDI" SUKUT: sinfda 25-30 o'quvchi bo'ladi, odatda 1-3 tasi
   kelmaydi. Ochilganda hamma yashil turadi, xodim faqat kerakligini bosadi.
   ========================================================================== */

/** Uchta holat — ekrandagi uchta doira. */
type Mark = 'present' | 'absent' | 'excused'

const pad = (n: number) => String(n).padStart(2, '0')

function todayISO(): string {
  const d = new Date()
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/** Doira: tanlangani to'liq rangda, qolgani kulrang. */
const chip = (active: boolean, tone: Mark) =>
  cn(
    'flex h-8 w-8 items-center justify-center rounded-full text-xs font-semibold transition-colors',
    active && tone === 'present' && 'bg-emerald-500 text-white',
    active && tone === 'absent' && 'bg-red-500 text-white',
    active && tone === 'excused' && 'bg-amber-500 text-white',
    !active && 'bg-slate-100 text-slate-400 hover:bg-slate-200',
  )

const MARKS: Mark[] = ['present', 'absent', 'excused']
const MARK_LETTER: Record<Mark, string> = { present: 'K', absent: 'Y', excused: 'S' }
const MARK_TITLE: Record<Mark, string> = { present: 'Keldi', absent: 'Kelmadi', excused: 'Sababli' }

/** Tasdiq oynasi nima uchun ochilgani: boshqa sinf yoki boshqa dars. */
type Pending = { kind: 'class'; id: string } | { kind: 'lesson'; key: string }

export function DailyMarkingPage() {
  const [date, setDate] = useState(todayISO())
  const [overview, setOverview] = useState<DailyAttendanceOverview | null>(null)
  const [overviewLoading, setOverviewLoading] = useState(true)

  const [classId, setClassId] = useState('')
  const [day, setDay] = useState<DailyAttendanceClassDay | null>(null)
  const [dayLoading, setDayLoading] = useState(false)
  /** Tanlangan dars soati — "subjectId|period". */
  const [lessonKey, setLessonKey] = useState('')

  /** Ekranda turgan (hali saqlanmagan) belgilar: studentId → Mark. */
  const [draft, setDraft] = useState<Record<string, Mark>>({})
  const [dirty, setDirty] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [savedNote, setSavedNote] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  const [pending, setPending] = useState<Pending | null>(null)
  /** "Saqlash" bosildi — tasdiq oynasi ochiq (mijoz, 2026-09-19). */
  const [confirmSave, setConfirmSave] = useState(false)
  /** "Saqlash" belgisizlar bilan bosildi — ular sariq bo'lib ko'rinadi. */
  const [showUnmarked, setShowUnmarked] = useState(false)
  /** Saqlangandan keyingi yashil xabar (mijoz, 2026-09-19). */
  const [toast, setToast] = useState<string | null>(null)

  /**
   * Katalogda mos sabab bo'lmasa, o'sha tugma YOZILMAYDI — bosilsa belgi
   * jim ketib qolardi (server `reasonId: null` ni "keldi" deb tushunadi).
   * Shuning uchun tugma o'chiriladi va sozlamaga yo'l ko'rsatiladi.
   */
  const allowed = (m: Mark) =>
    m === 'present' ||
    (m === 'absent' ? !!day?.absentReasonId : !!day?.excusedReasonId)

  const keyOf = (l: DailyAttendanceLesson) => `${l.subjectId}|${l.period}|${l.subGroup}`
  const lesson = day?.lessons.find((l) => keyOf(l) === lessonKey) ?? null
  const currentClass = overview?.classes.find((c) => c.classId === classId) ?? null

  const loadOverview = useCallback(async () => {
    setOverviewLoading(true)
    try {
      const data = await getDailyOverview(date)
      setOverview(data)
      return data
    } finally {
      setOverviewLoading(false)
    }
  }, [date])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi kun tanlanganda tanlov va qoralama tozalanadi (maqsadli)
    setClassId('')
    setDay(null)
    setLessonKey('')
    setDraft({})
    setDirty(false)
    setSavedNote(null)
    setError(null)
    void loadOverview()
  }, [loadOverview])

  /**
   * Darsning joriy belgilarini qoralamaga oladi. HALI BELGILANMAGAN dars — bo'sh qoralama:
   * hech kim sukut bo'yicha "keldi" emas (mijoz, 2026-09-23), xodim har birini o'zi belgilaydi.
   * Saqlangan darsda belgisi yo'q o'quvchi — "keldi" (server shunday saqlaydi).
   */
  const draftOf = (data: DailyAttendanceClassDay, target: DailyAttendanceLesson | null) => {
    const next: Record<string, Mark> = {}
    // Bo'lingan darsda faqat O'SHA guruh belgilanadi (server ham shunday
    // yozadi) — qolgan yarim sinf boshqa xonada.
    const ids = target ? new Set(target.studentIds) : new Set(data.students.map((s) => s.studentId))
    if (!target?.marked) return next
    for (const s of data.students) {
      if (!ids.has(s.studentId)) continue
      const reason = target?.marks[s.studentId]
      next[s.studentId] = !reason ? 'present' : reason === data.absentReasonId ? 'absent' : 'excused'
    }
    return next
  }

  /** Sinfni YUKLAYDI (saqlanmagan belgilar tekshiruvi bu yerda emas). */
  const loadClass = async (id: string) => {
    setClassId(id)
    setDayLoading(true)
    setError(null)
    setSavedNote(null)
    setQuery('')
    try {
      const data = await getDailyClass(id, date)
      setDay(data)
      // Ochilganda birinchi BELGILANMAGAN soat tanlanadi — xodim o'sha
      // yerdan davom etadi.
      const target = data.lessons.find((l) => !l.marked) ?? data.lessons[0] ?? null
      setLessonKey(target ? keyOf(target) : '')
      setDraft(draftOf(data, target))
      setDirty(false)
      setShowUnmarked(false)
    } catch {
      setError("Sinf ma'lumotini yuklab bo'lmadi.")
    } finally {
      setDayLoading(false)
    }
  }

  const showLesson = (data: DailyAttendanceClassDay, key: string) => {
    setShowUnmarked(false)
    setLessonKey(key)
    setDraft(draftOf(data, data.lessons.find((l) => keyOf(l) === key) ?? null))
    setDirty(false)
    setError(null)
    setSavedNote(null)
  }

  /**
   * Dropdown'dan tanlash. Saqlanmagan belgilar bo'lsa — ILOVANING O'Z tasdiq
   * oynasi (brauzerning `confirm()` i butun sahifani muzlatadi).
   */
  const pickClass = (id: string) => {
    if (!id || id === classId) return
    if (dirty) {
      setPending({ kind: 'class', id })
      return
    }
    void loadClass(id)
  }

  const pickLesson = (key: string) => {
    if (!day || !key || key === lessonKey) return
    if (dirty) {
      setPending({ kind: 'lesson', key })
      return
    }
    showLesson(day, key)
  }

  const setMark = (studentId: string, mark: Mark) => {
    if (!allowed(mark)) return
    setError(null)
    setDraft((prev) => ({ ...prev, [studentId]: mark }))
    setDirty(true)
    setSavedNote(null)
  }

  /** Butun ustun — mijoz so'ragan "tepadagi bitta button". */
  const setColumn = (mark: Mark) => {
    if (!day || !lesson || !allowed(mark)) return
    setDraft(Object.fromEntries(lesson.studentIds.map((id) => [id, mark])))
    setDirty(true)
    setSavedNote(null)
  }

  // Oddiy hisob (kichik ro'yxat) — `lesson` har renderda hosil qilinadi, memo kerak emas.
  const counts = (() => {
    const values = Object.values(draft)
    return {
      present: values.filter((v) => v === 'present').length,
      absent: values.filter((v) => v === 'absent').length,
      excused: values.filter((v) => v === 'excused').length,
      unmarked: (lesson?.studentIds ?? []).filter((id) => !draft[id]).length,
    }
  })()

  const save = async () => {
    if (!day || !lesson || saving) return
    setSaving(true)
    setError(null)
    try {
      const marks = Object.entries(draft)
        .filter(([, m]) => m !== 'present')
        .map(([studentId, m]) => ({
          studentId,
          reasonId: m === 'absent' ? day.absentReasonId : day.excusedReasonId,
        }))

      const updated = await saveDailyAttendance(
        day.classId,
        date,
        lesson.subjectId,
        lesson.period,
        lesson.subGroup,
        marks,
      )
      // SAQLANGACH SINFDAN CHIQAMIZ (mijoz, 2026-09-19: "saqlangach bu
      // sinfdan chiqishi kerak"). Ilgari ekran o'zi keyingi soatga o'tardi va
      // xodim qaysi darsni belgilayotganini yo'qotib qo'yardi — endi tanlov
      // bo'shab qoladi, keyingi sinf/dars ataylab tanlanadi.
      setClassId('')
      setDay(null)
      setLessonKey('')
      setDraft({})
      setDirty(false)
      setSavedNote(`${updated.className} · ${lesson.period}-dars saqlandi.`)
      setToast(`${updated.className} · ${lesson.period}-dars · ${lesson.subjectName} — davomat saqlandi`)

      await loadOverview()
    } catch (err: unknown) {
      const message =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
        "Saqlab bo'lmadi."
      setError(message)
    } finally {
      setSaving(false)
    }
  }

  const filtered = useMemo(() => {
    if (!day) return []
    // `lesson` emas, `lessonKey` — bog'liqlik SODDA qiymat bo'lsin: hosila
    // obyektga bog'lanish React Compiler'ning memoizatsiyasini buzadi
    // (`react-hooks/preserve-manual-memoization`).
    const current = day.lessons.find(
      (l) => `${l.subjectId}|${l.period}|${l.subGroup}` === lessonKey,
    )
    const ids = current ? new Set(current.studentIds) : null
    const mine = ids ? day.students.filter((s) => ids.has(s.studentId)) : day.students
    const q = query.trim().toLowerCase()
    return q ? mine.filter((s) => s.fullName.toLowerCase().includes(q)) : mine
  }, [day, lessonKey, query])

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Davomat belgilash</h1>
          <p className="text-sm text-slate-400">
            Kun, sinf va darsni tanlang — keldi (yashil), kelmadi (qizil), sababli (sariq).
          </p>
        </div>
        {/* Kunning holati: DARS soati ham, sinf ham. Faqat sinf hisobi yetarli
            emas — bitta sinfda 5 ta soat bo'ladi va "0/10 sinf" ishning qay
            darajada bitganini ko'rsatmaydi (mijoz, 2026-09-19). */}
        {overview && (
          <div className="mb-0.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
            <span className="font-medium text-slate-700">
              {overview.markedLessons} / {overview.totalLessons} dars belgilandi
            </span>
            <span className="text-slate-400">·</span>
            <span className="text-slate-500">
              {overview.markedClasses} / {overview.totalClasses} sinf tugadi
            </span>
            <span className="text-red-600">{overview.absentTotal} yo'q</span>
          </div>
        )}
      </header>

      {/* ---- Tanlov qatori: Kun · Sinf · Dars ---- */}
      <Card>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-[13rem_minmax(12rem,1fr)_minmax(16rem,1.4fr)]">
          <DatePicker label="Kun" value={date} onChange={setDate} max={todayISO()} />

          <Select
            label="Sinf"
            value={classId}
            onChange={(e) => pickClass(e.target.value)}
            disabled={overviewLoading}
          >
            <option value="">Tanlang...</option>
            {overview?.classes.map((c) => (
              <option key={c.classId} value={c.classId} disabled={c.lessonCount === 0}>
                {c.className}
                {c.lessonCount === 0
                  ? " — bu kunda dars yo'q"
                  : ` — ${c.markedLessons}/${c.lessonCount} dars`}
              </option>
            ))}
          </Select>

          <Select
            label="Dars"
            value={lessonKey}
            onChange={(e) => pickLesson(e.target.value)}
            disabled={!day || dayLoading || day.lessons.length === 0}
          >
            {!day && <option value="">Avval sinfni tanlang</option>}
            {day?.lessons.length === 0 && <option value="">Bu kunda dars yo'q</option>}
            {day?.lessons.map((l) => (
              <option key={keyOf(l)} value={keyOf(l)}>
                {l.period}-dars · {l.subjectName}
                {l.subGroup > 0 ? ` · ${l.subGroup}-guruh` : ''}
                {l.startTime ? ` · ${l.startTime}` : ''}
                {l.marked ? ' ✓' : ''}
                {l.absentCount > 0 ? ` (${l.absentCount} yo'q)` : ''}
              </option>
            ))}
          </Select>
        </div>

        {day && (
          <>
            <p className="mt-3 text-xs text-slate-400">
              Qizil — {day.absentReasonName ?? '—'} · Sariq — {day.excusedReasonName ?? '—'}
              {currentClass && ` · ${currentClass.studentCount} o'quvchi`}
            </p>
            {(!day.absentReasonId || !day.excusedReasonId) && (
              <p className="mt-1 text-xs text-amber-600">
                Katalogda yetarli davomat sababi yo'q — Sozlamalar → Umumiy sozlamalar →
                Davomat sabablari bo'limiga qo'shing.
              </p>
            )}
          </>
        )}
      </Card>

      {/* ---- Ro'yxat ---- */}
      {dayLoading ? (
        <Card>
          <Loader className="py-16" label="Yuklanmoqda..." />
        </Card>
      ) : !day || !lesson ? (
        <Card>
          <div className="flex flex-col items-center gap-2 py-16 text-center">
            <CheckCircle2 className="h-8 w-8 text-slate-300" />
            <p className="font-medium text-slate-600">
              {day && day.lessons.length === 0 ? "Bu kunda dars yo'q" : 'Sinf tanlanmagan'}
            </p>
            <p className="max-w-sm text-sm text-slate-400">
              Yuqoridagi ro'yxatdan sinfni, so'ng darsni tanlang.
            </p>
          </div>
        </Card>
      ) : (
        <>
          <Card className="p-0">
            {/* --- Sarlavha: butun ustunni belgilaydigan uchta tugma --- */}
            <div className="flex items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
              <div className="min-w-0">
                <p className="truncate font-semibold text-slate-800">
                  {day.className} · {lesson.period}-dars · {lesson.subjectName}
                  {lesson.subGroup > 0 ? ` · ${lesson.subGroup}-guruh` : ''}
                </p>
                <p className="text-xs text-slate-400">
                  {counts.present} keldi · {counts.absent} kelmadi · {counts.excused} sababli
                  {lesson.marked && lesson.markedByName ? ` · ${lesson.markedByName}` : ''}
                </p>
              </div>

              <div className="flex shrink-0 items-center gap-2">
                {MARKS.map((m) => (
                  <button
                    key={m}
                    type="button"
                    disabled={!allowed(m)}
                    onClick={() => setColumn(m)}
                    title={
                      allowed(m)
                        ? `Hammasi — ${MARK_TITLE[m]}`
                        : 'Bu belgi uchun katalogda davomat sababi yo\u2019q'
                    }
                    aria-label={`Hammasi — ${MARK_TITLE[m]}`}
                    className={cn(chip(true, m), !allowed(m) && 'cursor-not-allowed opacity-30')}
                  >
                    {MARK_LETTER[m]}
                  </button>
                ))}
              </div>
            </div>

            <div className="border-b border-slate-100 px-4 py-2">
              <div className="relative">
                <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                <input
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder="O'quvchi ismi bo'yicha qidirish"
                  autoComplete="off"
                  className="w-full rounded-lg border border-slate-200 bg-white py-1.5 pl-9 pr-3 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
                />
              </div>
            </div>

            <ul className="divide-y divide-slate-100">
              {filtered.map((s, i) => {
                const mark: Mark | undefined = draft[s.studentId]
                return (
                  <li
                    key={s.studentId}
                    className={cn(
                      'flex items-center justify-between gap-3 px-4 py-2',
                      !mark && showUnmarked && 'bg-amber-50/70',
                    )}
                  >
                    <span className="flex min-w-0 items-center gap-3">
                      <span className="w-5 shrink-0 text-xs text-slate-400">{i + 1}</span>
                      <span className="truncate font-medium text-slate-800">{s.fullName}</span>
                    </span>

                    <span className="flex shrink-0 items-center gap-2">
                      {MARKS.map((m) => (
                        <button
                          key={m}
                          type="button"
                          disabled={!allowed(m)}
                          onClick={() => setMark(s.studentId, m)}
                          title={
                            allowed(m)
                              ? MARK_TITLE[m]
                              : 'Bu belgi uchun katalogda davomat sababi yo\u2019q'
                          }
                          aria-label={`${s.fullName} — ${MARK_TITLE[m]}`}
                          className={cn(
                            chip(mark === m, m),
                            !allowed(m) && 'cursor-not-allowed opacity-30',
                          )}
                        >
                          {MARK_LETTER[m]}
                        </button>
                      ))}
                    </span>
                  </li>
                )
              })}

              {filtered.length === 0 && (
                <li className="px-4 py-10 text-center text-sm text-slate-400">
                  O'quvchi topilmadi.
                </li>
              )}
            </ul>
          </Card>

          {error && (
            <div className="rounded-xl border border-red-200 bg-red-50/70 px-3 py-3">
              <div className="flex items-start gap-2 text-sm text-red-700">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                <span>{error}</span>
              </div>
            </div>
          )}

          <div className="flex flex-wrap items-center justify-between gap-3">
            <span className="text-sm text-slate-400">
              {counts.unmarked > 0 ? (
                <span className="text-amber-600">{counts.unmarked} ta o'quvchi hali belgilanmagan</span>
              ) : (
                savedNote ?? (dirty ? "Saqlanmagan o'zgarish bor." : 'O’zgarish yo’q.')
              )}
            </span>
            <Button
              onClick={() => {
                // Hamma belgilanmaguncha saqlanmaydi — belgisiz o'quvchi jimgina "keldi" bo'lib qolmasin.
                if (counts.unmarked > 0) {
                  setShowUnmarked(true)
                  setError(`${counts.unmarked} ta o'quvchi belgilanmagan — avval hammasini belgilang.`)
                  return
                }
                setConfirmSave(true)
              }}
              disabled={saving}
            >
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          </div>
        </>
      )}

      {/* Saqlash TASDIQ bilan (mijoz, 2026-09-19): davomat jurnalga tushadi,
          shuning uchun "bosib yubordim" degan holat bo'lmasligi kerak. */}
      <Modal
        open={confirmSave}
        onClose={() => setConfirmSave(false)}
        title="Davomatni saqlash"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setConfirmSave(false)} disabled={saving}>
              Bekor qilish
            </Button>
            <Button
              onClick={() => {
                setConfirmSave(false)
                void save()
              }}
              disabled={saving}
            >
              Saqlash
            </Button>
          </>
        }
      >
        {day && lesson && (
          <div className="space-y-2 text-sm text-slate-600">
            <p className="font-medium text-slate-800">
              {day.className} · {lesson.period}-dars · {lesson.subjectName}
            </p>
            <p>
              <span className="text-emerald-600">{counts.present} keldi</span> ·{' '}
              <span className="text-red-600">{counts.absent} kelmadi</span> ·{' '}
              <span className="text-amber-600">{counts.excused} sababli</span>
            </p>
            <p className="text-xs text-slate-400">
              Saqlangach bu sinf yopiladi — keyingi sinfni yuqoridan tanlaysiz.
            </p>
          </div>
        )}
      </Modal>

      <Toast message={toast} onClose={() => setToast(null)} />

      <Modal
        open={pending !== null}
        onClose={() => setPending(null)}
        title="Saqlanmagan belgilar"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setPending(null)}>
              Shu yerda qolaman
            </Button>
            <Button
              onClick={() => {
                const target = pending
                setPending(null)
                if (!target) return
                if (target.kind === 'class') void loadClass(target.id)
                else if (day) showLesson(day, target.key)
              }}
            >
              Saqlamasdan o'tish
            </Button>
          </>
        }
      >
        <p className="text-sm text-slate-600">
          Bu darsda saqlanmagan belgilar bor. Boshqasiga o'tilsa, ular yo'qoladi.
        </p>
      </Modal>
    </div>
  )
}

export default DailyMarkingPage
