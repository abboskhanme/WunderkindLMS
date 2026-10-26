/**
 * DAVOMAT VA BAHO — bir dars, bir ro'yxat, bitta "Saqlash".
 *
 * Baho ham shu ro'yxatda (mijoz, 2026-09-29: "o'qituvchi profilida baho qo'yish ham
 * mumkin bo'lsin"): kelgan o'quvchi qatorida 1–5 tugmalari — admin jurnali va PWA bilan
 * bir xil shkala. Bir katak (sana + dars) davomat sababini ham, bahoni ham saqlaydi,
 * shuning uchun ikkalasi bitta so'rovda yoziladi va uyga vazifa / xulq / o'zlashtirishga
 * tegilmaydi.
 *
 * Davomat DARSGA bog'liq, sinfga emas: bir kunda bir sinfda ikki dars bo'lishi
 * mumkin va jurnal katagi (sana + dars raqami) bilan aniqlanadi. Shuning uchun
 * tanlov ham bugungi DARSLAR ro'yxatidan boshlanadi — o'qituvchi "qaysi
 * darsning davomati" degan savolga javob berishi shart emas, u allaqachon
 * jadvalda turibdi.
 *
 * Saqlash ikki ish qiladi: kelmagan o'quvchilarga sabab yozadi va darsni
 * "o'tildi" deb belgilaydi. Mavzu va uyga vazifa ham shu ekranda (mijoz,
 * 2026-10-12: o'qituvchi profilida "baho qo'yish, vazifa berish, mavzu berish") —
 * ota-ona ularni Mini App'ning "Jadval" bo'limida dars ostida ko'radi.
 *
 * Uy vazifasi bajarilishi ham shu ro'yxatda (mijoz, 2026-10-15; homework-completion.md): har
 * o'quvchida bir teginishli besh belgi (+ − * K Y) va "Hammasi bajardi". U o'sha jurnal katagining
 * `homework` maydoni, shuning uchun baho va davomat bilan BITTA so'rovda saqlanadi.
 *
 * O'QITUVCHI DAVOMAT OLMAYDI (mijoz, 2026-10-15; teacher-attendance-off.md): davomatni hamshira oladi.
 * `profile.canMarkAttendance` false bo'lsa — "Hammasi keldi", qator teginishi va sabab tugmalari yo'q;
 * hamshiraning belgisi faqat ko'rinadi ("Yo'q" / "Kech"), saqlashda o'sha sabab o'zgarmasdan qaytadi
 * (server ham uni saqlab qoladi). Baho va vazifa — hamshira "yo'q" demagan o'quvchilarga.
 *
 * XULQ (mijoz, 2026-10-15; behaviour.md): har qatorda "Xulq" — pastdan chiqadigan oynada Ijobiy /
 * Salbiy, sabab va izoh; DARHOL saqlanadi. Tugmada bugungi ballar va joriy ball (100 dan).
 *
 * O'TGAN KUNLAR (mijoz, 2026-10-26): o'qituvchi oldingi sanaga qaytib, qo'yilgan bahoni o'zgartira
 * yoki qo'yilmaganini qo'ya oladi. Tepada kun o'tkagichi — bugundan orqaga, o'quv yili boshigacha;
 * kelajakka o'tib bo'lmaydi (server ham rad etadi). Sananing choragi va haftasi `weekAxis` dan
 * olinadi, shuning uchun o'sha kungi jadval va o'sha chorak jurnali ochiladi. Xulq — faqat bugun:
 * u o'tgan sanaga emas, yozilgan paytiga tushadi.
 */
import { useEffect, useMemo, useState } from 'react'
import { CalendarOff, Check, ChevronLeft, ShieldCheck, UserX } from 'lucide-react'
import {
  Badge, Card, DateChip, EmptyState, ErrorState, Hero, Loader, Row, Screen, Stepper,
} from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { addDays, todayISO, weekAxis } from '../../lib/weeks'
import { dayMonth, fullDate, weekdayName } from '../../lib/format'
import { haptic, showBackButton } from '../../lib/telegram'
import { AsyncBlock, lessonKey, lessonOwnerTitle, lessonPairs } from './shared'
import { HOMEWORK_STATUSES, HW_DONE } from '../../lib/homework'
import { BehaviourSheet, signedPoints } from './BehaviourSheet'

export function AttendanceTab({ profile, meta, focusLesson, onClearFocus }) {
  const today = todayISO()
  const [date, setDate] = useState(today)
  const isToday = date === today
  // O'quv yilining haftalari — sana qaysi chorak/haftaga tushishi shu o'qdan (server bilan bir xil).
  const axis = useMemo(() => weekAxis(meta.quarters), [meta.quarters])
  const spot = axis.find((a) => a.startISO <= date && date <= a.endISO) || null
  const quarter = spot?.quarter ?? (isToday ? meta.currentQuarter : null)
  const week = spot?.week ?? (isToday ? meta.currentWeek : null)
  const day = (new Date(`${date}T00:00:00`).getDay() + 6) % 7
  // Orqaga — o'quv yili boshigacha, oldinga — bugungacha.
  const firstDay = axis[0]?.startISO ?? today
  const shiftDay = (n) => {
    haptic()
    const next = addDays(date, n)
    setDate(next > today ? today : next)
  }
  const [selected, setSelected] = useState(focusLesson ?? null)
  // The school decides whether teachers mark attendance (default: no — the nurse does).
  const canMark = profile?.canMarkAttendance === true
  // The behaviour sheet has its own Telegram back button; the lesson's steps aside meanwhile.
  const [sheetOpen, setSheetOpen] = useState(false)

  // "Bugun" tabidan dars bilan kirilganda — o'sha darsni ochamiz.
  useEffect(() => {
    if (!focusLesson) return
    // "Bugun" tabidagi dars — doim bugungi sana.
    setDate(todayISO())
    setSelected(focusLesson)
  }, [focusLesson])

  const close = () => {
    setSelected(null)
    onClearFocus?.()
  }

  // Telegramning o'z "orqaga" tugmasi ro'yxatga qaytaradi.
  useEffect(() => {
    if (!selected || sheetOpen) return undefined
    return showBackButton(close)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selected, sheetOpen])

  const q = useAsync(async () => {
    // Sana hech bir chorak haftasiga tushmaydi (ta'til, yakshanba) — dars yo'q.
    if (quarter == null || week == null) return { lessons: [], conducted: new Set() }
    const schedule = await teacherApi.schedule(quarter, week)
    const lessons = schedule.filter((l) => l.day === day).sort((a, b) => a.period - b.period)

    const pairs = lessonPairs(lessons)
    // Bitta darsning qaydlari yiqilsa ro'yxat baribir ochilsin — o'sha dars
    // "Olinmagan" ko'rinadi, xatoning o'zi dars ichida (403 matni bilan) chiqadi.
    const noteLists = await Promise.all(
      pairs.map((p) => teacherApi.journalNotes(p.classId, p.subjectId, quarter).catch(() => [])),
    )
    const conducted = new Set()
    noteLists.forEach((list, i) => {
      for (const n of list) {
        if (n.date === date && n.conducted) conducted.add(`${pairs[i].classId}|${n.period}`)
      }
    })
    return { lessons, conducted }
  }, [quarter, week, day, date])

  if (selected) {
    return (
      <LessonAttendance
        lesson={selected}
        quarter={quarter}
        date={date}
        isToday={isToday}
        reasons={meta.absenceReasons || []}
        canMark={canMark}
        onSheetChange={setSheetOpen}
        onBack={close}
        onSaved={q.reload}
      />
    )
  }

  return (
    <Screen>
      <Hero title={canMark ? 'Davomat va baho' : 'Baho va vazifa'} subtitle={fullDate(date)}>
        <Stepper
          label={isToday ? `Bugun · ${dayMonth(date)}` : `${weekdayName(day)} · ${dayMonth(date)}`}
          onPrev={() => shiftDay(-1)}
          onNext={() => shiftDay(1)}
          disabledPrev={date <= firstDay}
          disabledNext={isToday}
        />
        {!isToday && (
          <button
            type="button"
            onClick={() => {
              haptic()
              setDate(today)
            }}
            className="mt-1 block w-full text-center text-[13px] font-semibold underline opacity-70"
          >
            Bugungi kunga qaytish
          </button>
        )}
      </Hero>
      <AsyncBlock query={q} loadingLabel="Darslar yuklanmoqda…">
        {(data) => (
          <>
            <Card
              title="Qaysi dars?"
              action={
                <Badge tone={data.lessons.length ? 'brand' : 'neutral'}>
                  {data.lessons.length} dars
                </Badge>
              }
            >
              {data.lessons.length === 0 ? (
                <EmptyState
                  icon={<CalendarOff className="h-9 w-9" />}
                  title={isToday ? "Bugun dars yo'q" : "Bu kunda dars yo'q"}
                  note={
                    quarter == null
                      ? "Bu sana hech bir chorakka tushmaydi (ta'til yoki dam olish kuni)."
                      : `${weekdayName(day)} kuniga jadvalda sizga dars biriktirilmagan, shuning uchun davomat olinadigan dars ham yo'q.`
                  }
                />
              ) : (
                data.lessons.map((l) => {
                  const done = data.conducted.has(`${l.classId}|${l.period}`)
                  return (
                    <Row
                      key={lessonKey(l)}
                      lead={<DateChip day={l.period} month="dars" tone={done ? 'brand' : 'neutral'} />}
                      title={lessonOwnerTitle(l)}
                      subtitle={`${l.subjectName} · ${l.startTime}–${l.endTime}`}
                      right={
                        <Badge tone={done ? 'success' : 'neutral'}>
                          {canMark ? (done ? 'Olingan' : 'Olinmagan') : done ? "O'tildi" : "O'tilmagan"}
                        </Badge>
                      }
                      onClick={() => {
                        haptic()
                        setSelected(l)
                      }}
                    />
                  )
                })
              )}
            </Card>
            <p className="mx-6 mt-3 text-[12px] leading-relaxed text-slate-400">
              {canMark
                ? "Davomat saqlanganda dars avtomatik \"o'tildi\" deb belgilanadi va o'sha sinf yoki guruh jurnalida ko'rinadi."
                : "Saqlanganda dars avtomatik \"o'tildi\" deb belgilanadi. Davomatni hamshira oladi — uning belgisi ro'yxatda ko'rinadi."}
            </p>
            <div className="h-4" />
          </>
        )}
      </AsyncBlock>
    </Screen>
  )
}

/* ------------------------------------------------------- bitta darsning ro'yxati */

function LessonAttendance({ lesson, quarter, date, isToday = true, reasons, canMark, onSheetChange, onBack, onSaved }) {
  const { classId, subjectId, period } = lesson
  // `classId` — darsning EGASI: sinf yoki o'quv guruhi (§2.1.4). Server ro'yxatni
  // o'zi yechadi, shuning uchun bu yerda sinf nomi bo'yicha filtr yo'q.
  const isGroup = lesson.ownerKind === 'group'
  // Guruhda sinf ichidagi 1/2-bo'linish YO'Q — hamma faol a'zo qatnashadi.
  const subGroup = isGroup ? 0 : (lesson.subGroup ?? 0)

  // Sababsiz kelmaslik — bir teginishda qo'yiladigan sukut sabab.
  const defaultReason = useMemo(() => {
    const list = reasons.filter((r) => !r.isLate)
    return (
      list.find((r) => r.name.toLowerCase().includes('sababsiz')) || list[0] || reasons[0] || null
    )
  }, [reasons])

  const q = useAsync(async () => {
    const [students, entries, notes] = await Promise.all([
      teacherApi.journalStudents(classId),
      teacherApi.journalEntries(classId, subjectId, quarter),
      teacherApi.journalNotes(classId, subjectId, quarter),
    ])
    const roster = students.filter((s) => subGroup === 0 || s.subGroup === subGroup)
    const todayEntries = entries.filter((e) => e.date === date && e.period === period)
    const note = notes.find((n) => n.date === date && n.period === period) || null
    return { roster, todayEntries, note }
  }, [classId, subjectId, quarter, period, date])

  // Xulq — loaded beside the roster and never in its way: a failure only hides the "Xulq" buttons.
  const behaviourReasons = useAsync(() => teacherApi.disciplineReasons().catch(() => null), [])
  const behaviourScores = useAsync(
    () => teacherApi.disciplineScores(classId).catch(() => []),
    [classId],
  )
  const scoreOf = useMemo(
    () => new Map((behaviourScores.data || []).map((r) => [r.studentId, r])),
    [behaviourScores.data],
  )
  const [sheetStudent, setSheetStudent] = useState(null)
  const openSheet = (student) => {
    haptic()
    setSheetStudent(student)
    onSheetChange?.(true)
  }
  const closeSheet = () => {
    setSheetStudent(null)
    onSheetChange?.(false)
  }
  // Leaving the lesson with the sheet open must give the back button back.
  useEffect(() => () => onSheetChange?.(false), [onSheetChange])

  // Belgilar: { [studentId]: reasonId } — sababi yo'q o'quvchi "keldi".
  const [marks, setMarks] = useState(null)
  // Baholar: { [studentId]: 1..5 } — yo'q = baho qo'yilmagan.
  const [grades, setGrades] = useState(null)
  // Uy vazifasi: { [studentId]: 1..5 } (lib/homework) — yo'q = belgilanmagan.
  const [homework, setHomework] = useState(null)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState(null)
  const [savedAt, setSavedAt] = useState(null)
  // Dars mavzusi va uyga vazifa (lesson note) — saqlashda dars bilan birga yoziladi.
  const [topic, setTopic] = useState('')
  const [homeworkText, setHomeworkText] = useState('')

  // Server javobi kelganda belgilarni bir marta serverdagi holatdan to'ldiramiz.
  const loadedKey = q.data ? `${classId}|${date}|${period}|${q.data.todayEntries.length}` : null
  const [seededKey, setSeededKey] = useState(null)
  if (q.data && seededKey !== loadedKey) {
    const seed = {}
    const gradeSeed = {}
    const homeworkSeed = {}
    for (const e of q.data.todayEntries) {
      if (e.reasonId) seed[e.studentId] = e.reasonId
      if (e.grade != null) gradeSeed[e.studentId] = e.grade
      if (e.homework) homeworkSeed[e.studentId] = e.homework
    }
    setSeededKey(loadedKey)
    setMarks(seed)
    setGrades(gradeSeed)
    setHomework(homeworkSeed)
    setTopic(q.data.note?.topic ?? '')
    setHomeworkText(q.data.note?.homework ?? '')
    setSaveError(null)
  }

  if (q.loading && q.data === null) {
    return (
      <Screen>
        <LessonHero lesson={lesson} date={date} conducted={false} canMark={canMark} onBack={onBack} />
        <Loader label="O'quvchilar yuklanmoqda…" />
      </Screen>
    )
  }
  if (q.error) {
    return (
      <Screen>
        <LessonHero lesson={lesson} date={date} conducted={false} canMark={canMark} onBack={onBack} />
        <ErrorState message={q.error} onRetry={q.reload} />
      </Screen>
    )
  }

  const { roster, todayEntries, note } = q.data
  const current = marks || {}
  const currentGrades = grades || {}
  const currentHomework = homework || {}
  // Kechikkan o'quvchi darsda bo'lgan — uy vazifasi undan ham so'raladi; kelmaganidan esa yo'q.
  const lateIds = new Set(reasons.filter((r) => r.isLate).map((r) => r.id))
  const inClass = (studentId) => !current[studentId] || lateIds.has(current[studentId])
  const lateCount = roster.filter((s) => current[s.id] && lateIds.has(current[s.id])).length
  const reasonById = new Map(reasons.map((r) => [r.id, r]))
  const homeworkCount = roster.filter((s) => currentHomework[s.id]).length
  const homeworkEmpty = roster.filter((s) => inClass(s.id) && !currentHomework[s.id]).length
  // Faqat SHU ro'yxatdagilarni sanaymiz: bo'lingan darsda jurnalda boshqa
  // guruhning yozuvi ham bo'lishi mumkin, u bu darsga tegishli emas.
  const absentCount = roster.filter((s) => current[s.id]).length
  const presentCount = roster.length - absentCount
  const gradedCount = roster.filter((s) => currentGrades[s.id] != null).length
  const conducted = Boolean(note?.conducted)

  const toggle = (studentId) => {
    haptic()
    setMarks((prev) => {
      const next = { ...(prev || {}) }
      if (next[studentId]) delete next[studentId]
      else next[studentId] = defaultReason?.id || null
      return next
    })
    setSavedAt(null)
  }

  const setReason = (studentId, reasonId) => {
    haptic()
    setMarks((prev) => ({ ...(prev || {}), [studentId]: reasonId }))
    setSavedAt(null)
  }

  /** Bir teginish — baho qo'yiladi; o'sha bahoga yana teginish — olib tashlanadi. */
  const setGrade = (studentId, grade) => {
    haptic()
    setGrades((prev) => {
      const next = { ...(prev || {}) }
      if (next[studentId] === grade) delete next[studentId]
      else next[studentId] = grade
      return next
    })
    setSavedAt(null)
  }

  /** Bir teginish — belgi qo'yiladi; o'sha belgiga yana teginish — olib tashlanadi. */
  const setHomeworkMark = (studentId, value) => {
    haptic()
    setHomework((prev) => {
      const next = { ...(prev || {}) }
      if (next[studentId] === value) delete next[studentId]
      else next[studentId] = value
      return next
    })
    setSavedAt(null)
  }

  /** Darsdagi, hali belgisiz o'quvchilarga "+ Bajardi"; qo'yilgan belgilarga tegmaydi. */
  const markAllDone = () => {
    haptic('medium')
    setHomework((prev) => {
      const next = { ...(prev || {}) }
      for (const s of roster) if (inClass(s.id) && !next[s.id]) next[s.id] = HW_DONE
      return next
    })
    setSavedAt(null)
  }

  const markAllPresent = () => {
    haptic('medium')
    setMarks({})
    setSavedAt(null)
  }

  const save = async () => {
    setSaving(true)
    setSaveError(null)
    try {
      const byStudent = new Map(todayEntries.map((e) => [e.studentId, e]))
      for (const s of roster) {
        const desired = current[s.id] || null
        const desiredGrade = currentGrades[s.id] ?? null
        const desiredHomework = currentHomework[s.id] ?? 0
        const existing = byStudent.get(s.id) || null
        if (
          (existing?.reasonId || null) === desired &&
          (existing?.grade ?? null) === desiredGrade &&
          (existing?.homework ?? 0) === desiredHomework
        )
          continue

        const keepsSomething =
          desired !== null ||
          desiredGrade !== null ||
          desiredHomework !== 0 ||
          (existing?.behavior ?? 0) !== 0 ||
          existing?.mastery != null

        if (!keepsSomething) {
          // Belgidan boshqa hech narsa qolmadi — katakni butunlay tozalaymiz.
          await teacherApi.clearJournalEntry({
            classId, subjectId, quarter, studentId: s.id, date, period,
          })
          continue
        }
        await teacherApi.setJournalEntry({
          classId,
          subjectId,
          quarter,
          studentId: s.id,
          date,
          period,
          grade: desiredGrade,
          reasonId: desired,
          homework: desiredHomework,
          // Xulq va o'zlashtirish bu ekranda yo'q — ular o'zgarmasdan qoladi.
          behavior: existing?.behavior ?? 0,
          mastery: existing?.mastery ?? null,
        })
      }

      // Darsni "o'tildi" deb belgilaymiz — mavzu va uyga vazifa bilan birga.
      await teacherApi.setJournalNote({
        classId,
        subjectId,
        quarter,
        date,
        period,
        subGroup,
        topic: topic.trim(),
        homework: homeworkText.trim() || null,
        conducted: true,
      })

      haptic('medium')
      setSavedAt(new Date())
      q.reload()
      onSaved?.()
    } catch (e) {
      setSaveError(
        e?.status === 400
          ? e?.message || "Kelajakdagi darsga davomat yoki baho qo'yib bo'lmaydi."
          : e?.status === 403
            ? "Bu sinf jurnaliga ruxsatingiz yo'q."
            : e?.message || 'Saqlashda xatolik',
      )
    } finally {
      setSaving(false)
    }
  }

  return (
    <Screen>
      <LessonHero lesson={lesson} date={date} conducted={conducted} canMark={canMark} onBack={onBack} />

      {canMark && reasons.length === 0 ? (
        <Card>
          <EmptyState
            title="Davomat sabablari sozlanmagan"
            note="Kelmaganlikni belgilash uchun maktab sozlamalarida kamida bitta sabab (masalan «Kasal», «Sababsiz») bo'lishi kerak. Administratorga murojaat qiling."
          />
        </Card>
      ) : roster.length === 0 ? (
        <Card>
          <EmptyState
            icon={<UserX className="h-9 w-9" />}
            title="O'quvchi yo'q"
            note={
              isGroup
                ? `${lesson.className} guruhiga hali o'quvchi biriktirilmagan.`
                : subGroup > 0
                  ? `${lesson.className} sinfining ${subGroup}-guruhida o'quvchi biriktirilmagan.`
                  : `${lesson.className} sinfiga hali o'quvchi biriktirilmagan.`
            }
          />
        </Card>
      ) : (
        <>
          <Card title="Mavzu va uyga vazifa">
            <div className="space-y-3 px-4 pb-4 pt-2">
              <label className="block">
                <span className="mb-1 block text-[13px] font-semibold text-slate-500">Dars mavzusi</span>
                <input
                  type="text"
                  value={topic}
                  maxLength={300}
                  onChange={(e) => {
                    setTopic(e.target.value)
                    setSavedAt(null)
                  }}
                  placeholder="Masalan: Kasrlarni qo'shish"
                  className="w-full rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5 text-[15px] outline-none focus:border-brand focus:bg-white"
                />
              </label>
              <label className="block">
                <span className="mb-1 block text-[13px] font-semibold text-slate-500">Uyga vazifa</span>
                <textarea
                  value={homeworkText}
                  maxLength={1000}
                  rows={3}
                  onChange={(e) => {
                    setHomeworkText(e.target.value)
                    setSavedAt(null)
                  }}
                  placeholder="Masalan: 45-bet, 3–5-mashqlar"
                  className="w-full resize-none rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5 text-[15px] outline-none focus:border-brand focus:bg-white"
                />
              </label>
              <p className="text-[12px] text-slate-400">Ota-ona va o'quvchi buni ilovada dars ostida ko'radi.</p>
            </div>
          </Card>

          <Card
            title="O'quvchilar"
            action={
              canMark ? (
                <button
                  type="button"
                  onClick={markAllPresent}
                  className="rounded-full bg-slate-100 px-3 py-1.5 text-[12px] font-semibold text-slate-600"
                >
                  Hammasi keldi
                </button>
              ) : null
            }
          >
            <div className="px-4 pb-2 pt-1">
              <p className="text-[13px] text-slate-500">
                {canMark ? (
                  <>
                    <span className="font-bold text-emerald-600">{presentCount} keldi</span>
                    {' · '}
                    <span className={absentCount ? 'font-bold text-red-600' : ''}>
                      {absentCount} kelmadi
                    </span>
                  </>
                ) : (
                  // The nurse's roll-call, read-only: absent (not late) and late.
                  <>
                    <span className={absentCount - lateCount ? 'font-bold text-red-600' : ''}>
                      {absentCount - lateCount} yo'q
                    </span>
                    {lateCount > 0 && (
                      <>
                        {' · '}
                        <span className="font-bold text-amber-600">{lateCount} kech</span>
                      </>
                    )}
                    <span className="text-slate-400"> (hamshira)</span>
                  </>
                )}
                {' · '}
                {roster.length} ta jami
                {' · '}
                <span className={gradedCount ? 'font-bold text-slate-700' : ''}>{gradedCount} ta baho</span>
                {' · '}
                <span className={homeworkCount ? 'font-bold text-slate-700' : ''}>{homeworkCount} ta vazifa</span>
              </p>
              <div className="mt-2 flex items-start gap-2">
                <p className="min-w-0 flex-1 text-[11px] leading-snug text-slate-400">
                  Uy vazifasi:{' '}
                  {HOMEWORK_STATUSES.map((h, i) => (
                    <span key={h.value}>
                      {i > 0 && ' · '}
                      <b className={h.tone}>{h.symbol}</b> {h.label.toLowerCase()}
                    </span>
                  ))}
                </p>
                <button
                  type="button"
                  onClick={markAllDone}
                  disabled={homeworkEmpty === 0}
                  className="shrink-0 rounded-full bg-slate-100 px-3 py-1.5 text-[12px] font-semibold text-slate-600 disabled:opacity-40"
                >
                  Hammasi bajardi
                </button>
              </div>
            </div>
            {roster.map((s, i) => (
              <StudentRow
                key={s.id}
                index={i + 1}
                student={s}
                reasonId={current[s.id] || null}
                reasons={reasons}
                canMark={canMark}
                nurseMark={current[s.id] ? reasonById.get(current[s.id]) || { name: "Yo'q", isLate: false } : null}
                showGrades={canMark ? !current[s.id] : inClass(s.id)}
                // Xulq o'tgan sanaga yozilmaydi — faqat bugungi darsda.
                behaviour={isToday && behaviourReasons.data ? scoreOf.get(s.id) || { remaining: null, today: 0 } : null}
                onBehaviour={() => openSheet(s)}
                grade={currentGrades[s.id] ?? null}
                homework={currentHomework[s.id] ?? 0}
                // Kelmaganda yashirin — lekin oldin qo'yilgan belgi bo'lsa, ko'rinib turadi (yashirin qiymat qolmasin).
                showHomework={inClass(s.id) || Boolean(currentHomework[s.id])}
                onToggle={() => toggle(s.id)}
                onReason={(rid) => setReason(s.id, rid)}
                onGrade={(g) => setGrade(s.id, g)}
                onHomework={(v) => setHomeworkMark(s.id, v)}
              />
            ))}
          </Card>

          {saveError && (
            <p className="mx-6 mt-3 text-[13px] font-medium text-red-600">{saveError}</p>
          )}

          {sheetStudent && behaviourReasons.data && (
            <BehaviourSheet
              student={sheetStudent}
              reasons={behaviourReasons.data}
              onClose={closeSheet}
              onChanged={behaviourScores.reload}
            />
          )}

          {/* Yopishqoq panel tab panelining ustida turadi — ro'yxat uzun bo'lsa ham
              "Saqlash" har doim ko'rinadi. */}
          <div className="h-28" />
          <div className="fixed inset-x-0 bottom-[calc(76px+var(--tg-safe-bottom))] z-10 border-t border-slate-200 bg-white px-4 py-3">
            <button
              type="button"
              onClick={save}
              disabled={saving}
              className="flex w-full items-center justify-center gap-2 rounded-2xl bg-brand py-3.5 text-[16px] font-bold text-brand-ink disabled:opacity-50"
            >
              {saving ? (
                'Saqlanmoqda…'
              ) : savedAt ? (
                <>
                  <Check className="h-5 w-5" /> Saqlandi
                </>
              ) : (
                'Saqlash'
              )}
            </button>
          </div>
        </>
      )}
    </Screen>
  )
}

/* -------------------------------------------------------------- yordamchilar */

function LessonHero({ lesson, date, conducted, canMark = true, onBack }) {
  return (
    <Hero
      title={lessonOwnerTitle(lesson)}
      subtitle={`${lesson.subjectName} · ${lesson.period}-dars · ${lesson.startTime}–${lesson.endTime}`}
      right={
        <button
          type="button"
          onClick={onBack}
          aria-label="Orqaga"
          className="flex h-9 items-center gap-1 rounded-full bg-brand-ink px-3 text-[13px] font-semibold text-brand"
        >
          <ChevronLeft className="h-4 w-4" />
          Darslar
        </button>
      }
    >
      <div className="flex items-center gap-2">
        <Badge tone={conducted ? 'success' : 'neutral'}>
          {canMark
            ? conducted ? 'Davomat olingan' : 'Davomat olinmagan'
            : conducted ? "Dars o'tildi" : "Dars o'tilmagan"}
        </Badge>
        <span className="text-[13px] opacity-70">{fullDate(date)}</span>
      </div>
    </Hero>
  )
}

/** Dars bahosi shkalasi — admin jurnali (`JournalCellModal`) va PWA bilan bir xil. */
const GRADES = [1, 2, 3, 4, 5]

/**
 * Bitta o'quvchi. Qatorning o'zi — bitta teginishda "keldi ⇄ kelmadi".
 * Kelgan o'quvchida baho tugmalari (1–5), kelmaganida sabab tugmalari (sukut: sababsiz).
 * Kelmagan o'quvchining oldin qo'yilgan bahosi o'chirilmaydi — faqat tugmalari yashirinadi.
 * Darsda bo'lgan (kelgan yoki kechikkan) o'quvchida — uy vazifasi belgilari (+ − * K Y).
 *
 * `canMark` false (davomatni hamshira oladi): qator teginilmaydi, sabab tugmalari yo'q — faqat
 * hamshira belgisi (`nurseMark`) kichik nishon bo'lib ko'rinadi. `behaviour` — "Xulq" tugmasi
 * (joriy ball va bugungi ballar); null bo'lsa tugma yo'q.
 */
function StudentRow({
  index, student, reasonId, reasons, canMark = true, nurseMark, showGrades, behaviour, onBehaviour,
  grade, homework, showHomework, onToggle, onReason, onGrade, onHomework,
}) {
  // Sabab bor = kelmagan. Sabablar ro'yxati bo'sh bo'lganda bu ekranga umuman
  // kirilmaydi (yuqorida tekshiriladi), shuning uchun bu yerda shart yetarli.
  const absent = Boolean(reasonId)

  const xulq = behaviour && (
    <button
      type="button"
      onClick={onBehaviour}
      aria-label={`${student.fullName} — xulq`}
      className="flex shrink-0 items-center gap-1 rounded-full bg-slate-100 px-2.5 py-1.5 text-[12px] font-semibold text-slate-600"
    >
      <ShieldCheck className="h-3.5 w-3.5" />
      Xulq
      {behaviour.remaining != null && <span className="font-normal text-slate-400">{behaviour.remaining}</span>}
      {behaviour.today !== 0 && (
        <span
          className={
            'rounded-full px-1.5 text-[11px] font-bold ' +
            (behaviour.today < 0 ? 'bg-red-100 text-red-600' : 'bg-emerald-100 text-emerald-700')
          }
        >
          {signedPoints(behaviour.today)}
        </span>
      )}
    </button>
  )

  return (
    <div className="border-t border-slate-100 px-4 py-2.5 first:border-t-0">
      {canMark ? (
        <div className="flex items-center gap-2">
          <button type="button" onClick={onToggle} className="flex min-w-0 flex-1 items-center gap-3 text-left">
            <span className="w-5 shrink-0 text-[12px] font-bold text-slate-300">{index}</span>
            <span className="min-w-0 flex-1 truncate text-[15px] font-semibold">{student.fullName}</span>
            <span
              className={
                'shrink-0 rounded-full px-3 py-1.5 text-[13px] font-bold ' +
                (absent ? 'bg-red-50 text-red-600' : 'bg-emerald-50 text-emerald-700')
              }
            >
              {absent ? "Yo'q" : 'Keldi'}
            </span>
          </button>
          {xulq}
        </div>
      ) : (
        <div className="flex items-center gap-2">
          <span className="w-5 shrink-0 text-[12px] font-bold text-slate-300">{index}</span>
          <span className="min-w-0 flex-1 truncate text-[15px] font-semibold">{student.fullName}</span>
          {nurseMark && (
            <span
              title={nurseMark.name}
              className={
                'shrink-0 rounded-full px-2.5 py-1 text-[12px] font-bold ' +
                (nurseMark.isLate ? 'bg-amber-50 text-amber-700' : 'bg-red-50 text-red-600')
              }
            >
              {nurseMark.isLate ? 'Kech' : "Yo'q"}
            </span>
          )}
          {xulq}
        </div>
      )}

      {(showGrades ?? !absent) && (
        <div className="mt-2 flex gap-1.5 pl-8" role="group" aria-label={`${student.fullName} — baho`}>
          {GRADES.map((g) => {
            const on = g === grade
            return (
              <button
                key={g}
                type="button"
                onClick={() => onGrade(g)}
                aria-pressed={on}
                className={
                  'h-9 min-w-[40px] flex-1 rounded-xl text-[15px] font-bold ' +
                  (on ? 'bg-brand-ink text-brand' : 'bg-slate-100 text-slate-500')
                }
              >
                {g}
              </button>
            )
          })}
        </div>
      )}

      {showHomework && (
        <div className="mt-1.5 flex items-center gap-1.5 pl-8" role="group" aria-label={`${student.fullName} — uy vazifasi`}>
          <span className="w-11 shrink-0 text-[11px] font-semibold text-slate-400">Vazifa</span>
          {HOMEWORK_STATUSES.map((h) => {
            const on = h.value === homework
            return (
              <button
                key={h.value}
                type="button"
                onClick={() => onHomework(h.value)}
                aria-pressed={on}
                aria-label={`Uy vazifasi: ${h.label}`}
                title={h.label}
                className={
                  'h-8 min-w-[34px] flex-1 rounded-lg text-[15px] font-bold ' +
                  (on ? 'bg-brand-ink text-brand' : `bg-slate-100 ${h.tone}`)
                }
              >
                {h.symbol}
              </button>
            )
          })}
        </div>
      )}

      {canMark && absent && (
        <div className="mt-2 flex flex-wrap gap-1.5 pl-8">
          {reasons.map((r) => {
            const on = r.id === reasonId
            return (
              <button
                key={r.id}
                type="button"
                onClick={() => onReason(r.id)}
                title={r.name}
                className={
                  'min-w-[34px] rounded-lg px-2 py-1 text-[12px] font-bold ' +
                  (on ? 'bg-brand-ink text-brand' : 'bg-slate-100 text-slate-500')
                }
              >
                {r.short}
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}
