/**
 * BUGUN — kunning bitta ekranda ko'rinishi.
 *
 * O'qituvchi ertalab ilovani ochganda ko'radigan yagona savol: "hozir nima
 * bo'lyapti va nimani qilishim kerak?". Shuning uchun tartib shunday: avval
 * HOZIRGI (yoki keyingi) dars, keyin raqamlar, keyin kun jadvali. Kun jadvalida
 * har dars "o'tildi / o'tilmadi" deb belgilanadi va bosilsa to'g'ridan-to'g'ri
 * davomatga olib boradi — bu ekrandan chiqadigan asosiy amal.
 */
import { useEffect, useState } from 'react'
import { CalendarOff, ChevronRight, Clock } from 'lucide-react'
import {
  Badge, Card, DateChip, EmptyState, Hero, HeroProgress, Row, Screen, StatGrid,
} from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { readSeen, unreadChannelCount } from '../../lib/chatSeen'
import { todayISO } from '../../lib/weeks'
import { fullDate, todayIndex, weekdayName } from '../../lib/format'
import {
  AsyncBlock, lessonPairs, lessonProgress, lessonTimeState, nowHHmm,
} from './shared'

export function TodayTab({ profile, meta, onOpenAttendance }) {
  const perms = profile.permissions || []
  const canSchedule = perms.includes('schedule')
  const canJournal = perms.includes('journal')
  const canMessages = perms.includes('messages')
  const quarter = meta.currentQuarter
  const week = meta.currentWeek

  // Har daqiqada qayta chizamiz — "hozir dars ketmoqda" va "qoldi" jonli qolsin.
  const [, setTick] = useState(0)
  useEffect(() => {
    const id = setInterval(() => setTick((n) => n + 1), 60_000)
    return () => clearInterval(id)
  }, [])

  const q = useAsync(async () => {
    const date = todayISO()
    const day = todayIndex()

    const schedule = canSchedule ? await teacherApi.schedule(quarter, week) : []
    const lessons = schedule.filter((l) => l.day === day).sort((a, b) => a.period - b.period)

    // "Dars o'tildi" belgisi dars qaydlarida turadi — bugungi har (sinf, fan)
    // uchun bittadan so'rov. Darslar o'qituvchining o'ziniki, ya'ni ruxsat bor.
    const pairs = canJournal ? lessonPairs(lessons) : []
    const noteLists = await Promise.all(
      pairs.map((p) => teacherApi.journalNotes(p.classId, p.subjectId, quarter)),
    )
    const conducted = new Set()
    noteLists.forEach((list, i) => {
      for (const n of list) {
        if (n.date === date && n.conducted) conducted.add(`${pairs[i].classId}|${n.period}`)
      }
    })

    const [progress, lastMessages] = await Promise.all([
      teacherApi.progress(quarter),
      canMessages ? teacherApi.chatLastMessages() : Promise.resolve({}),
    ])

    return { date, lessons, conducted, progress, lastMessages }
  }, [quarter, week])

  const d = q.data
  const total = d?.lessons.length ?? 0
  const done = d ? d.lessons.filter((l) => d.conducted.has(`${l.classId}|${l.period}`)).length : 0

  const subjectLine = (profile.subjects || []).map((s) => s.name).join(', ')
  const homeroomLine = profile.homeroomClass ? `${profile.homeroomClass} sinf rahbari` : null
  const subtitle = [subjectLine, homeroomLine].filter(Boolean).join(' · ') || "O'qituvchi"

  return (
    <Screen>
      <Hero title={profile.fullName} subtitle={subtitle}>
        {total > 0 && (
          <HeroProgress
            value={done}
            max={total}
            lead={`${done}/${total}`}
            note={done === total ? "bugungi darslar o'tildi" : "dars o'tildi"}
          />
        )}
      </Hero>

      <AsyncBlock query={q} loadingLabel="Bugungi kun yuklanmoqda…">
        {(data) => (
          <>
            <FocusLesson
              lessons={data.lessons}
              conducted={data.conducted}
              canJournal={canJournal}
              canSchedule={canSchedule}
              onOpenAttendance={onOpenAttendance}
            />

            <StatGrid
              items={[
                {
                  label: 'Bugungi darslar',
                  value: String(total),
                  note: weekdayName(todayIndex()),
                },
                {
                  label: 'Davomat olindi',
                  value: `${done}/${total}`,
                  note: total - done > 0 ? `${total - done} ta qoldi` : 'hammasi tayyor',
                },
                {
                  label: 'Joriy davr',
                  value: `${meta.currentQuarter}-chorak`,
                  note: `${meta.currentWeek}-hafta`,
                },
                canMessages
                  ? {
                      label: 'Yangi xabar',
                      value: String(unreadChannelCount(data.lastMessages, readSeen())),
                      note: `${Object.keys(data.lastMessages).length} ta guruhda`,
                    }
                  : {
                      label: 'Sinf rahbarligi',
                      value: profile.homeroomClass || '—',
                      note: profile.homeroomClass ? 'sinf' : 'biriktirilmagan',
                    },
              ]}
            />

            <QuarterProgress progress={data.progress} />

            <Card
              title="Bugungi darslar"
              action={<Badge tone={done === total && total > 0 ? 'success' : 'neutral'}>{fullDate(data.date)}</Badge>}
            >
              {!canSchedule ? (
                <EmptyState
                  title="Jadval yopiq"
                  note="Sizda dars jadvalini ko'rish ruxsati yo'q. Maktab administratoriga murojaat qiling."
                />
              ) : data.lessons.length === 0 ? (
                <EmptyState
                  icon={<CalendarOff className="h-9 w-9" />}
                  title="Bugun dars yo'q"
                  note={`${weekdayName(todayIndex())} kuniga jadvalda sizga dars biriktirilmagan.`}
                />
              ) : (
                data.lessons.map((l) => (
                  <LessonListRow
                    key={`${l.classId}-${l.period}`}
                    lesson={l}
                    conducted={data.conducted.has(`${l.classId}|${l.period}`)}
                    onClick={canJournal ? () => onOpenAttendance(l) : undefined}
                  />
                ))
              )}
            </Card>

            <div className="h-4" />
          </>
        )}
      </AsyncBlock>
    </Screen>
  )
}

/* --------------------------------------------------- hozirgi / keyingi dars */

/**
 * Kunning eng muhim kartochkasi. Qorong'i fon ATAYLAB tanlangan: qolgan hamma
 * kartochka oq, shuning uchun ko'z avval shu yerga tushadi.
 */
function FocusLesson({ lessons, conducted, canJournal, canSchedule, onOpenAttendance }) {
  if (!canSchedule || lessons.length === 0) return null

  const now = nowHHmm()
  const current = lessons.find((l) => lessonTimeState(l, now) === 'now')
  const next = lessons.find((l) => lessonTimeState(l, now) === 'later')
  const lesson = current || next

  if (!lesson) {
    const allDone = lessons.every((l) => conducted.has(`${l.classId}|${l.period}`))
    return (
      <Card className="bg-brand-ink text-white">
        <div className="px-4 py-5">
          <p className="text-[12px] font-bold tracking-wide text-brand">BUGUNGI DARSLAR TUGADI</p>
          <p className="mt-2 text-[16px] font-semibold leading-snug">
            {allDone
              ? "Hamma darslar o'tildi va jurnalga belgilandi."
              : 'Darslar tugadi, lekin ayrimlari jurnalda belgilanmagan.'}
          </p>
        </div>
      </Card>
    )
  }

  const live = Boolean(current)
  const { fraction, remaining } = lessonProgress(lesson, now)
  const isConducted = conducted.has(`${lesson.classId}|${lesson.period}`)

  return (
    <Card className="bg-brand-ink text-white">
      <div className="px-4 py-4">
        <div className="flex items-center gap-2">
          <span className={'h-2 w-2 rounded-full bg-brand ' + (live ? 'animate-pulse' : '')} />
          <span className="text-[12px] font-bold tracking-wide text-brand">
            {live ? 'HOZIR DARS KETMOQDA' : 'KEYINGI DARS'}
          </span>
          <span className="ml-auto rounded-lg bg-white/15 px-2.5 py-1 text-[12px] font-bold">
            {lesson.period}-dars
          </span>
        </div>

        <p className="mt-3 text-[24px] font-extrabold leading-tight">{lesson.subjectName}</p>
        <div className="mt-1 flex items-center gap-2 text-[14px] text-white/80">
          <span className="font-semibold text-white">{lesson.className}</span>
          {lesson.subGroup > 0 && <span>{lesson.subGroup}-guruh</span>}
          <span className="flex items-center gap-1">
            <Clock className="h-3.5 w-3.5" />
            {lesson.startTime}–{lesson.endTime}
          </span>
        </div>

        {live && (
          <div className="mt-4 flex items-center gap-3">
            <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-white/20">
              <div className="h-full rounded-full bg-brand" style={{ width: `${fraction * 100}%` }} />
            </div>
            <span className="shrink-0 text-[12px] font-bold text-white/85">{remaining} daqiqa qoldi</span>
          </div>
        )}

        {canJournal && (
          <button
            type="button"
            onClick={() => onOpenAttendance(lesson)}
            className="mt-4 flex w-full items-center justify-center gap-1.5 rounded-2xl bg-brand py-3 text-[15px] font-bold text-brand-ink"
          >
            {isConducted ? "Davomatni ko'rish" : 'Davomat olish'}
            <ChevronRight className="h-4 w-4" />
          </button>
        )}
      </div>
    </Card>
  )
}

/* ---------------------------------------------------------- chorak progresi */

/** Chorak bo'yicha o'tilgan darslar — reja bilan solishtirib. */
function QuarterProgress({ progress }) {
  if (!progress || progress.totalPlanned === 0) {
    return (
      <Card title="Chorak progressi">
        <EmptyState
          title="Reja hali yo'q"
          note="Bu chorakka sizning sinflaringiz uchun jadval biriktirilmagan, shuning uchun o'tilgan darslar hisoblanmaydi."
        />
      </Card>
    )
  }

  const expected = (progress.items || []).reduce((sum, i) => sum + (i.expectedByToday || 0), 0)
  const diff = progress.totalConducted - expected
  const tone = diff >= 0 ? 'success' : 'danger'
  const note =
    diff === 0
      ? "reja bo'yicha ketyapsiz"
      : diff > 0
        ? `rejadan ${diff} ta oldindasiz`
        : `rejadan ${Math.abs(diff)} ta ortdasiz`

  return (
    <Card
      title="Chorak progressi"
      action={<Badge tone={tone}>{progress.totalPercent}%</Badge>}
    >
      <div className="px-4 pb-4 pt-2">
        <div className="h-2.5 overflow-hidden rounded-full bg-slate-100">
          <div
            className="h-full rounded-full bg-brand"
            style={{ width: `${Math.min(100, progress.totalPercent)}%` }}
          />
        </div>
        <p className="mt-2 text-[13px] text-slate-500">
          <span className="font-bold text-brand-ink">{progress.totalConducted}</span> / {progress.totalPlanned} dars
          o'tildi · {note}
        </p>
      </div>
    </Card>
  )
}

/* ------------------------------------------------------------- dars qatori */

function LessonListRow({ lesson, conducted, onClick }) {
  const state = lessonTimeState(lesson)
  const badge = conducted
    ? { tone: 'success', text: "O'tildi" }
    : state === 'now'
      ? { tone: 'brand', text: 'Hozir' }
      : state === 'past'
        ? { tone: 'danger', text: 'Belgilanmagan' }
        : { tone: 'neutral', text: 'Kutilmoqda' }

  return (
    <Row
      lead={
        <DateChip
          day={lesson.period}
          month="dars"
          tone={conducted ? 'brand' : state === 'now' ? 'danger' : 'neutral'}
        />
      }
      title={`${lesson.className}${lesson.subGroup > 0 ? ` · ${lesson.subGroup}-guruh` : ''}`}
      subtitle={`${lesson.subjectName} · ${lesson.startTime}–${lesson.endTime}`}
      right={<Badge tone={badge.tone}>{badge.text}</Badge>}
      onClick={onClick}
    />
  )
}
