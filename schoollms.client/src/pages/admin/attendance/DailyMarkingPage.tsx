import { useCallback, useEffect, useMemo, useState } from 'react'
import { AlertTriangle, CheckCircle2, ListChecks, Search } from 'lucide-react'
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

/**
 * "Barcha darslar" — sinfning shu kundagi HAMMA soati bitta ro'yxatda (mijoz, 2026-09-25: "hamshira bir kunda
 * bir marta davomat qiladi ... sinfni tanlab bittada barcha darslar uchun davomat qilish"). Belgi har bir darsga
 * alohida yoziladi (server tomoni o'zgarmagan): bo'lingan darsda faqat o'sha guruh o'quvchilari.
 */
const ALL_KEY = 'ALL'

function allLessonsOf(data: DailyAttendanceClassDay): DailyAttendanceLesson | null {
  if (data.lessons.length === 0) return null
  const ids = [...new Set(data.lessons.flatMap((l) => l.studentIds))]
  const marked = data.lessons.filter((l) => l.marked)
  return {
    subjectId: '',
    subjectName: 'Barcha darslar',
    period: 0,
    subGroup: 0,
    startTime: data.lessons[0].startTime,
    endTime: data.lessons[data.lessons.length - 1].endTime,
    // Hammasi belgilangan bo'lsagina "belgilangan": aks holda xodim har birini qaytadan belgilaydi.
    marked: marked.length === data.lessons.length,
    markedByName: null,
    markedAt: null,
    absentCount: 0,
    lateCount: 0,
    studentIds: ids,
    // Qaysidir darsda yo'q bo'lgan o'quvchi — yo'q ko'rinadi.
    marks: Object.assign({}, ...marked.map((l) => l.marks)) as Record<string, string>,
  }
}

/** Sarlavha matni: "3-dars · Matematika" yoki "Barcha darslar (5 ta)". */
function lessonTitle(l: DailyAttendanceLesson, total: number): string {
  if (l.period === 0) return `Barcha darslar (${total} ta)`
  return `${l.period}-dars · ${l.subjectName}${l.subGroup > 0 ? ` · ${l.subGroup}-guruh` : ''}`
}

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
  /**
   * Sariq belgining ANIQ sababi: studentId → katalogdagi sabab id'si. Yo'q bo'lsa — sukut
   * (`excusedReasonId`). Ilgari sariq har doim bitta sababni yozardi: katalogdagi qolgan
   * sabablar chiqmasdi va tanlanmasdi (mijoz, 2026-09-25).
   */
  const [picked, setPicked] = useState<Record<string, string>>({})
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
  const allMode = lessonKey === ALL_KEY
  const lesson = !day ? null : allMode ? allLessonsOf(day) : (day.lessons.find((l) => keyOf(l) === lessonKey) ?? null)
  const lessonCount = day?.lessons.length ?? 0
  const currentClass = overview?.classes.find((c) => c.classId === classId) ?? null
  /** Sariq belgida tanlanadigan sabablar — qizil tugmaniki (Sababsiz) bundan mustasno. */
  const excusedChoices = (day?.reasons ?? []).filter((r) => r.id !== day?.absentReasonId)

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
    setPicked({})
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

  /**
   * Saqlangan sariq sabablar (Kasal, Kech qoldi ...) — qayta saqlashda O'ZICHA qoladi. Ilgari
   * ular sariq tugmaning sukut sababiga almashib ketardi. Katalogda yo'q id olinmaydi (server
   * uni rad etadi) — o'sha o'quvchiga sukut sabab yoziladi.
   */
  const pickedOf = (data: DailyAttendanceClassDay, target: DailyAttendanceLesson | null) => {
    const known = new Set((data.reasons ?? []).map((r) => r.id))
    return Object.fromEntries(
      Object.entries(target?.marks ?? {}).filter(([, rid]) => rid !== data.absentReasonId && known.has(rid)),
    ) as Record<string, string>
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
      setPicked(pickedOf(data, target))
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
    const target = key === ALL_KEY ? allLessonsOf(data) : (data.lessons.find((l) => keyOf(l) === key) ?? null)
    setDraft(draftOf(data, target))
    setPicked(pickedOf(data, target))
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

  /** Sariq belgili o'quvchiga katalogdan aniq sabab tanlash. */
  const setReason = (studentId: string, reasonId: string) => {
    setError(null)
    setPicked((prev) => ({ ...prev, [studentId]: reasonId }))
    setDirty(true)
    setSavedNote(null)
  }

  /** Butun ustun — mijoz so'ragan "tepadagi bitta button". */
  const setColumn = (mark: Mark) => {
    if (!day || !lesson || !allowed(mark)) return
    setDraft(Object.fromEntries(lesson.studentIds.map((id) => [id, mark])))
    // Butun ustun — hammaga bitta (sukut) sabab.
    setPicked({})
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
          reasonId: m === 'absent' ? day.absentReasonId : (picked[studentId] ?? day.excusedReasonId),
        }))

      const targets = allMode ? day.lessons : [lesson]
      let updated: DailyAttendanceClassDay | null = null
      let done = 0
      try {
        // Ketma-ket: har bir dars o'z so'rovi bilan (server tomoni o'zgarmagan). Bo'lingan darsga faqat
        // o'sha guruh o'quvchilarining belgisi ketadi.
        for (const t of targets) {
          const ids = new Set(t.studentIds)
          updated = await saveDailyAttendance(
            day.classId,
            date,
            t.subjectId,
            t.period,
            t.subGroup,
            marks.filter((m) => ids.has(m.studentId)),
          )
          done++
        }
      } catch (err) {
        if (done > 0) await loadOverview()
        throw done > 0
          ? Object.assign(new Error('partial'), {
              response: { data: { message: `${done}/${targets.length} dars saqlandi, qolganini saqlab bo'lmadi — qaytadan urinib ko'ring.` } },
            })
          : err
      }
      if (!updated) return
      // SAQLANGACH SINFDAN CHIQAMIZ (mijoz, 2026-09-19: "saqlangach bu
      // sinfdan chiqishi kerak"). Ilgari ekran o'zi keyingi soatga o'tardi va
      // xodim qaysi darsni belgilayotganini yo'qotib qo'yardi — endi tanlov
      // bo'shab qoladi, keyingi sinf/dars ataylab tanlanadi.
      setClassId('')
      setDay(null)
      setLessonKey('')
      setDraft({})
      setPicked({})
      setDirty(false)
      setSavedNote(`${updated.className} · ${lessonTitle(lesson, targets.length)} saqlandi.`)
      setToast(`${updated.className} · ${lessonTitle(lesson, targets.length)} — davomat saqlandi`)

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
    const current =
      lessonKey === ALL_KEY
        ? allLessonsOf(day)
        : day.lessons.find((l) => `${l.subjectId}|${l.period}|${l.subGroup}` === lessonKey)
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
            {day && day.lessons.length > 1 && (
              <option value={ALL_KEY}>
                Barcha darslar ({day.lessons.length} ta){day.lessons.every((l) => l.marked) ? ' ✓' : ''}
              </option>
            )}
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

        {day && day.lessons.length > 1 && !allMode && (
          <Button variant="secondary" className="mt-4 w-full whitespace-nowrap sm:w-auto" onClick={() => pickLesson(ALL_KEY)}>
            <ListChecks className="h-4 w-4 shrink-0" /> Barcha darslar ({day.lessons.length})
          </Button>
        )}

        {day && (
          <>
            <p className="mt-3 text-xs text-slate-400">
              Qizil — {day.absentReasonName ?? '—'} · Sariq — {day.excusedReasonName ?? '—'}
              {excusedChoices.length > 1 && ' (boshqa sababni o’quvchi qatorida tanlang)'}
              {currentClass && ` · ${currentClass.studentCount} o'quvchi`}
            </p>
            {(!day.absentReasonId || !day.excusedReasonId) && (
              <p className="mt-1 text-xs text-amber-600">
                "Kelmadi" / "sababli" uchun sabab yo'q.{' '}
                {(day.reasons ?? []).length > 0 && (day.reasons ?? []).every((r) => r.isLate)
                  ? 'Hamma sabab "Kech qolish" deb belgilangan — kech qolgan o\'quvchi darsda bor hisoblanadi. '
                  : ''}
                Sozlamalar → Umumiy sozlamalar → Davomat sabablari: "Sababsiz" va "Sababli" dan "Kech qolish"
                belgisini olib tashlang yoki shunday sabab qo'shing.
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
            <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
              <div className="min-w-0 flex-1">
                <p className="truncate font-semibold text-slate-800">
                  {day.className} · {lessonTitle(lesson, lessonCount)}
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
                      {mark === 'excused' && excusedChoices.length > 1 && (
                        <select
                          value={picked[s.studentId] ?? day.excusedReasonId ?? ''}
                          onChange={(e) => setReason(s.studentId, e.target.value)}
                          aria-label={`${s.fullName} — sabab`}
                          className="max-w-[9rem] truncate rounded-lg border border-amber-200 bg-amber-50 px-2 py-1 text-xs font-medium text-amber-800 outline-none focus:border-amber-400"
                        >
                          {excusedChoices.map((r) => (
                            <option key={r.id} value={r.id}>
                              {r.name}
                            </option>
                          ))}
                        </select>
                      )}
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
              {day.className} · {lessonTitle(lesson, lessonCount)}
            </p>
            {allMode && (
              <p className="text-xs text-slate-500">
                Belgilar {lessonCount} ta darsning hammasiga yoziladi — oldin belgilangan darslar ham shu belgilar
                bilan yangilanadi.
              </p>
            )}
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
