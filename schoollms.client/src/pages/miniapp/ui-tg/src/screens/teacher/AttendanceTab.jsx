/**
 * DAVOMAT — bir dars, bir ro'yxat, bitta "Saqlash".
 *
 * Davomat DARSGA bog'liq, sinfga emas: bir kunda bir sinfda ikki dars bo'lishi
 * mumkin va jurnal katagi (sana + dars raqami) bilan aniqlanadi. Shuning uchun
 * tanlov ham bugungi DARSLAR ro'yxatidan boshlanadi — o'qituvchi "qaysi
 * darsning davomati" degan savolga javob berishi shart emas, u allaqachon
 * jadvalda turibdi.
 *
 * Saqlash ikki ish qiladi: kelmagan o'quvchilarga sabab yozadi va darsni
 * "o'tildi" deb belgilaydi. Mavzu va uyga vazifa mavjud qayddan olib
 * uzatiladi — aks holda o'qituvchi kiritgan mavzu o'chib ketardi.
 */
import { useEffect, useMemo, useState } from 'react'
import { CalendarOff, Check, ChevronLeft, UserX } from 'lucide-react'
import {
  Badge, Card, DateChip, EmptyState, ErrorState, Hero, Loader, Row, Screen,
} from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { todayISO } from '../../lib/weeks'
import { fullDate, todayIndex, weekdayName } from '../../lib/format'
import { haptic, showBackButton } from '../../lib/telegram'
import { AsyncBlock, lessonPairs, lessonKey } from './shared'

export function AttendanceTab({ profile, meta, focusLesson, onClearFocus }) {
  const quarter = meta.currentQuarter
  const week = meta.currentWeek
  const date = todayISO()
  const [selected, setSelected] = useState(focusLesson ?? null)

  // "Bugun" tabidan dars bilan kirilganda — o'sha darsni ochamiz.
  useEffect(() => {
    if (focusLesson) setSelected(focusLesson)
  }, [focusLesson])

  const close = () => {
    setSelected(null)
    onClearFocus?.()
  }

  // Telegramning o'z "orqaga" tugmasi ro'yxatga qaytaradi.
  useEffect(() => {
    if (!selected) return undefined
    return showBackButton(close)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selected])

  const q = useAsync(async () => {
    const schedule = await teacherApi.schedule(quarter, week)
    const lessons = schedule.filter((l) => l.day === todayIndex()).sort((a, b) => a.period - b.period)

    const pairs = lessonPairs(lessons)
    const noteLists = await Promise.all(
      pairs.map((p) => teacherApi.journalNotes(p.classId, p.subjectId, quarter)),
    )
    const conducted = new Set()
    noteLists.forEach((list, i) => {
      for (const n of list) {
        if (n.date === date && n.conducted) conducted.add(`${pairs[i].classId}|${n.period}`)
      }
    })
    return { lessons, conducted }
  }, [quarter, week])

  if (selected) {
    return (
      <LessonAttendance
        lesson={selected}
        quarter={quarter}
        date={date}
        reasons={meta.absenceReasons || []}
        onBack={close}
        onSaved={q.reload}
      />
    )
  }

  return (
    <Screen>
      <Hero title="Davomat" subtitle={fullDate(date)} />
      <AsyncBlock query={q} loadingLabel="Bugungi darslar yuklanmoqda…">
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
                  title="Bugun dars yo'q"
                  note={`${weekdayName(todayIndex())} kuniga jadvalda sizga dars biriktirilmagan, shuning uchun davomat olinadigan dars ham yo'q.`}
                />
              ) : (
                data.lessons.map((l) => {
                  const done = data.conducted.has(`${l.classId}|${l.period}`)
                  return (
                    <Row
                      key={lessonKey(l)}
                      lead={<DateChip day={l.period} month="dars" tone={done ? 'brand' : 'neutral'} />}
                      title={`${l.className}${l.subGroup > 0 ? ` · ${l.subGroup}-guruh` : ''}`}
                      subtitle={`${l.subjectName} · ${l.startTime}–${l.endTime}`}
                      right={
                        <Badge tone={done ? 'success' : 'neutral'}>{done ? 'Olingan' : 'Olinmagan'}</Badge>
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
              Davomat saqlanganda dars avtomatik "o'tildi" deb belgilanadi va sinf jurnalida
              ko'rinadi.
            </p>
            <div className="h-4" />
          </>
        )}
      </AsyncBlock>
    </Screen>
  )
}

/* ------------------------------------------------------- bitta darsning ro'yxati */

function LessonAttendance({ lesson, quarter, date, reasons, onBack, onSaved }) {
  const { classId, subjectId, period } = lesson
  const subGroup = lesson.subGroup ?? 0

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

  // Belgilar: { [studentId]: reasonId } — sababi yo'q o'quvchi "keldi".
  const [marks, setMarks] = useState(null)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState(null)
  const [savedAt, setSavedAt] = useState(null)

  // Server javobi kelganda belgilarni bir marta serverdagi holatdan to'ldiramiz.
  const loadedKey = q.data ? `${classId}|${period}|${q.data.todayEntries.length}` : null
  const [seededKey, setSeededKey] = useState(null)
  if (q.data && seededKey !== loadedKey) {
    const seed = {}
    for (const e of q.data.todayEntries) if (e.reasonId) seed[e.studentId] = e.reasonId
    setSeededKey(loadedKey)
    setMarks(seed)
    setSaveError(null)
  }

  if (q.loading && q.data === null) {
    return (
      <Screen>
        <LessonHero lesson={lesson} date={date} conducted={false} onBack={onBack} />
        <Loader label="O'quvchilar yuklanmoqda…" />
      </Screen>
    )
  }
  if (q.error) {
    return (
      <Screen>
        <LessonHero lesson={lesson} date={date} conducted={false} onBack={onBack} />
        <ErrorState message={q.error} onRetry={q.reload} />
      </Screen>
    )
  }

  const { roster, todayEntries, note } = q.data
  const current = marks || {}
  // Faqat SHU ro'yxatdagilarni sanaymiz: bo'lingan darsda jurnalda boshqa
  // guruhning yozuvi ham bo'lishi mumkin, u bu darsga tegishli emas.
  const absentCount = roster.filter((s) => current[s.id]).length
  const presentCount = roster.length - absentCount
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
        const existing = byStudent.get(s.id) || null
        if ((existing?.reasonId || null) === desired) continue

        const keepsSomething =
          desired !== null ||
          existing?.grade != null ||
          (existing?.homework ?? 0) !== 0 ||
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
          // Bahoga TEGMAYMIZ — davomat ekrani bahoni o'chirib yubormasligi kerak.
          grade: existing?.grade ?? null,
          reasonId: desired,
          homework: existing?.homework ?? 0,
          behavior: existing?.behavior ?? 0,
          mastery: existing?.mastery ?? null,
        })
      }

      // Darsni "o'tildi" deb belgilaymiz; mavzu va uy vazifa saqlanib qoladi.
      await teacherApi.setJournalNote({
        classId,
        subjectId,
        quarter,
        date,
        period,
        subGroup,
        topic: note?.topic ?? '',
        homework: note?.homework ?? null,
        conducted: true,
      })

      haptic('medium')
      setSavedAt(new Date())
      q.reload()
      onSaved?.()
    } catch (e) {
      setSaveError(
        e?.status === 400
          ? "Kelajakdagi darsga davomat qo'yib bo'lmaydi."
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
      <LessonHero lesson={lesson} date={date} conducted={conducted} onBack={onBack} />

      {reasons.length === 0 ? (
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
              subGroup > 0
                ? `${lesson.className} sinfining ${subGroup}-guruhida o'quvchi biriktirilmagan.`
                : `${lesson.className} sinfiga hali o'quvchi biriktirilmagan.`
            }
          />
        </Card>
      ) : (
        <>
          <Card
            title="O'quvchilar"
            action={
              <button
                type="button"
                onClick={markAllPresent}
                className="rounded-full bg-slate-100 px-3 py-1.5 text-[12px] font-semibold text-slate-600"
              >
                Hammasi keldi
              </button>
            }
          >
            <div className="px-4 pb-2 pt-1">
              <p className="text-[13px] text-slate-500">
                <span className="font-bold text-emerald-600">{presentCount} keldi</span>
                {' · '}
                <span className={absentCount ? 'font-bold text-red-600' : ''}>
                  {absentCount} kelmadi
                </span>
                {' · '}
                {roster.length} ta jami
              </p>
            </div>
            {roster.map((s, i) => (
              <StudentRow
                key={s.id}
                index={i + 1}
                student={s}
                reasonId={current[s.id] || null}
                reasons={reasons}
                onToggle={() => toggle(s.id)}
                onReason={(rid) => setReason(s.id, rid)}
              />
            ))}
          </Card>

          {saveError && (
            <p className="mx-6 mt-3 text-[13px] font-medium text-red-600">{saveError}</p>
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
                'Davomatni saqlash'
              )}
            </button>
          </div>
        </>
      )}
    </Screen>
  )
}

/* -------------------------------------------------------------- yordamchilar */

function LessonHero({ lesson, date, conducted, onBack }) {
  return (
    <Hero
      title={`${lesson.className}${lesson.subGroup > 0 ? ` · ${lesson.subGroup}-guruh` : ''}`}
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
          {conducted ? 'Davomat olingan' : 'Davomat olinmagan'}
        </Badge>
        <span className="text-[13px] opacity-70">{fullDate(date)}</span>
      </div>
    </Hero>
  )
}

/**
 * Bitta o'quvchi. Qatorning o'zi — bitta teginishda "keldi ⇄ kelmadi".
 * Kelmagan o'quvchida sabab tugmalari ochiladi (sukut: sababsiz).
 */
function StudentRow({ index, student, reasonId, reasons, onToggle, onReason }) {
  // Sabab bor = kelmagan. Sabablar ro'yxati bo'sh bo'lganda bu ekranga umuman
  // kirilmaydi (yuqorida tekshiriladi), shuning uchun bu yerda shart yetarli.
  const absent = Boolean(reasonId)

  return (
    <div className="border-t border-slate-100 px-4 py-2.5 first:border-t-0">
      <button type="button" onClick={onToggle} className="flex w-full items-center gap-3 text-left">
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

      {absent && (
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
