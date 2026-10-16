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
import { BedDouble, CalendarOff, ChevronRight, Clock, Moon } from 'lucide-react'
import {
  Badge, Card, DateChip, EmptyState, Hero, HeroProgress, Row, Screen, StatGrid,
} from '../../components/ui'
import { AccountsButton } from '../../components/AccountSwitcher'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { todayISO } from '../../lib/weeks'
import { fullDate, todayIndex, weekdayName } from '../../lib/format'
import {
  AsyncBlock, lessonOwnerTitle, lessonPairs, lessonProgress, lessonTimeState,
  nowHHmm,
} from './shared'
import { NewsCard } from './NewsCard'
import { PenaltiesCard } from './PenaltiesCard'
import { FeedbackCard } from './FeedbackCard'
import { HomeroomDebtsCard } from './HomeroomDebtsCard'
import { RedListCard } from './RedListCard'
import { PollsCard } from '../../components/PollSheet'

// So'rovnomalar (2026-10-07): the teacher's open polls.
const POLLS_API = {
  list: () => teacherApi.polls(),
  submit: (id, answers) => teacherApi.answerPoll(id, answers),
}
import { LessonAnalysisCard } from './LessonAnalysis'
import { boardingSessions } from './BoardingScreen'

export function TodayTab({ profile, meta, onOpenAttendance, onOpenBoarding }) {
  const perms = profile.permissions || []
  const canSchedule = perms.includes('schedule')
  const canJournal = perms.includes('journal')
  // 2026-10-15: the nurse takes attendance unless the school lets teachers do it — the labels follow.
  const canMark = profile.canMarkAttendance === true
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
    // uchun bittadan so'rov. Bittasi yiqilsa (masalan, jurnal ruxsati o'sha
    // sinfga yo'q — 403) butun "Bugun" ekrani xato bo'lib qolmasin: o'sha dars
    // shunchaki "belgilanmagan" ko'rinadi.
    const pairs = canJournal ? lessonPairs(lessons) : []
    const noteLists = await Promise.all(
      pairs.map((p) => teacherApi.journalNotes(p.classId, p.subjectId, quarter).catch(() => [])),
    )
    const conducted = new Set()
    noteLists.forEach((list, i) => {
      for (const n of list) {
        if (n.date === date && n.conducted) conducted.add(`${pairs[i].classId}|${n.period}`)
      }
    })

    const progress = await teacherApi.progress(quarter)

    return { date, lessons, conducted, progress }
  }, [quarter, week])

  const d = q.data
  const total = d?.lessons.length ?? 0
  const done = d ? d.lessons.filter((l) => d.conducted.has(`${l.classId}|${l.period}`)).length : 0

  // One shape for every teacher (2026-10-07): the homeroom first — at the end it was cut off for
  // primary teachers with seven subjects — then at most two subjects and "+N".
  const subjectNames = (profile.subjects || []).map((s) => s.name)
  const subjectLine =
    subjectNames.length > 2 ? `${subjectNames.slice(0, 2).join(', ')} +${subjectNames.length - 2}` : subjectNames.join(', ')
  const homeroomLine = profile.homeroomClass ? `${profile.homeroomClass} sinf rahbari` : null
  // Komendant kabi qo'shimcha rol (2026-10-12) — "O'qituvchi" dan keyin.
  const subtitle = ["O'qituvchi", profile.accessRoleName, homeroomLine, subjectLine].filter(Boolean).join(' · ')
  const hasBoarding = boardingSessions(profile).length > 0

  return (
    <Screen>
      <Hero title={profile.fullName} subtitle={subtitle} right={<AccountsButton />}>
        <HeroProgress
          value={done}
          max={total}
          lead={`${done}/${total}`}
          note={total === 0 ? "bugun dars yo'q" : done === total ? "bugungi darslar o'tildi" : "dars o'tildi"}
        />
      </Hero>

      <AsyncBlock query={q} loadingLabel="Bugungi kun yuklanmoqda…">
        {(data) => (
          <>
            <FocusLesson
              lessons={data.lessons}
              conducted={data.conducted}
              canJournal={canJournal}
              canSchedule={canSchedule}
              canMark={canMark}
              onOpenAttendance={onOpenAttendance}
            />

            {/* Komendant (2026-10-12) — faqat roli bor o'qituvchida. */}
            {hasBoarding && <CommandantCard profile={profile} onOpen={onOpenBoarding} />}

            {/* Qizil ro'yxat (2026-10-15): ota-ona bilan bog'lanish kerak bo'lgan o'quvchilar — faqat holat bo'lsa. */}
            {profile.homeroomClass && <RedListCard />}

            {/* Sinf rahbari: o'quvchilari va qarzi (2026-10-12) — faqat rahbarlik bor o'qituvchida. */}
            {profile.homeroomClass && <HomeroomDebtsCard />}

            <StatGrid
              items={[
                {
                  label: 'Bugungi darslar',
                  value: String(total),
                  note: weekdayName(todayIndex()),
                },
                {
                  label: canMark ? 'Davomat olindi' : "Dars o'tildi",
                  value: `${done}/${total}`,
                  note: total - done > 0 ? `${total - done} ta qoldi` : 'hammasi tayyor',
                },
                {
                  label: 'Joriy davr',
                  value: `${meta.currentQuarter}-chorak`,
                  note: `${meta.currentWeek}-hafta`,
                },
                // "Yangi xabar" o'rnida (guruh chati olib tashlandi, 2026-10-11).
                {
                  label: 'Fanlarim',
                  value: String(subjectNames.length),
                  note: homeroomLine ?? (subjectNames.length > 0 ? 'fan' : "fan biriktirilmagan"),
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

            {/* Polls (client, 2026-10-07) — always present for teachers, "none open" when empty. */}
            <PollsCard api={POLLS_API} alwaysShow />

            {/* Lesson analysis results (client, 2026-09-28) — always present, "none yet" when empty. */}
            <LessonAnalysisCard />

            {/* Own fines (client, 2026-10-07) — always present, "none" when empty. */}
            <PenaltiesCard />

            {/* School news, after today's lessons (§3.4). It loads itself:
                the feed can never take the day's schedule down with it. */}
            <NewsCard />

            {/* Taklif va shikoyatlar: own items with status + a new one (client, 2026-10-07). */}
            <FeedbackCard />

            <div className="h-4" />
          </>
        )}
      </AsyncBlock>
    </Screen>
  )
}

/* ------------------------------------------------------------ komendant */

/**
 * Komendant kartasi (2026-10-12: o'qituvchi yotoqxona komendanti ham). Roli ochgan har sessiya —
 * bitta qator: bugun nechta o'quvchi va nechtasi belgilangan. Bosilsa yo'qlama ochiladi.
 */
function CommandantCard({ profile, onOpen }) {
  const sessions = boardingSessions(profile)
  const date = todayISO()
  const q = useAsync(
    () => Promise.all(sessions.map((s) => teacherApi.boardingDay(date, s.key).catch(() => null))),
    [sessions.map((s) => s.key).join(',')],
  )

  return (
    <Card
      title={profile.accessRoleName || 'Komendant'}
      action={<Badge>{fullDate(date)}</Badge>}
      className="ring-2 ring-brand"
    >
      {sessions.map((s, i) => {
        const day = q.data?.[i] ?? null
        const total = day?.eligible ?? 0
        const marked = day?.marked ?? 0
        const subtitle = !day
          ? q.loading
            ? 'Yuklanmoqda…'
            : "Ma'lumot olinmadi"
          : total === 0
            ? "Ro'yxat bo'sh"
            : `${total} o'quvchi · ${marked}/${total} belgilandi`
        const Icon = s.key === 'dorm' ? BedDouble : Moon
        return (
          <Row
            key={s.key}
            onClick={() => onOpen(s.key)}
            lead={
              <span
                className={
                  'flex h-11 w-11 shrink-0 items-center justify-center rounded-xl ' +
                  (s.key === 'dorm' ? 'bg-brand-ink text-brand' : 'bg-brand/25 text-brand-ink')
                }
              >
                <Icon className="h-5 w-5" />
              </span>
            }
            title={s.title}
            subtitle={subtitle}
            right={
              <span className="flex shrink-0 items-center gap-1">
                {day && total > 0 && (
                  <Badge tone={marked >= total ? 'success' : 'danger'}>{marked >= total ? 'Olingan' : 'Olinmagan'}</Badge>
                )}
                <ChevronRight className="h-4 w-4 text-slate-300" />
              </span>
            }
          />
        )
      })}
    </Card>
  )
}

/* --------------------------------------------------- hozirgi / keyingi dars */

/**
 * Kunning eng muhim kartochkasi. Qorong'i fon ATAYLAB tanlangan: qolgan hamma
 * kartochka oq, shuning uchun ko'z avval shu yerga tushadi.
 */
function FocusLesson({ lessons, conducted, canJournal, canSchedule, canMark, onOpenAttendance }) {
  // The same dark card for every teacher (2026-10-07) — also on a day without lessons.
  if (!canSchedule || lessons.length === 0)
    return (
      <Card className="bg-brand-ink text-white">
        <div className="px-4 py-5">
          <p className="text-[12px] font-bold tracking-wide text-brand">{canSchedule ? "BUGUN DARS YO'Q" : 'JADVAL YOPIQ'}</p>
          <p className="mt-2 text-[16px] font-semibold leading-snug">
            {canSchedule
              ? `${weekdayName(todayIndex())} kuniga jadvalda sizga dars biriktirilmagan.`
              : "Dars jadvalini ko'rish ruxsati yo'q — maktab administratoriga murojaat qiling."}
          </p>
        </div>
      </Card>
    )

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
        {substituteNote(lesson) && (
          <p className="mt-2 inline-flex rounded-lg bg-brand/15 px-2.5 py-1 text-[12px] font-bold text-brand">
            {substituteNote(lesson)}
          </p>
        )}

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
            {canMark ? (isConducted ? 'Davomat va baholar' : 'Davomat va baho') : 'Baho va vazifa'}
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
        : `rejadan ${Math.abs(diff)} ta orqadasiz`

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

/** O'rindosh ustoz (2026-10-07): "covering for X" on a substitute's lesson, "X covers" on one's own. */
function substituteNote(lesson) {
  if (lesson.substituteFor) return `O'rindosh: ${lesson.substituteFor} o'rniga`
  if (lesson.substitutedBy) return `${lesson.substitutedBy} o'tadi`
  return null
}

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
      title={lessonOwnerTitle(lesson)}
      subtitle={[`${lesson.subjectName} · ${lesson.startTime}–${lesson.endTime}`, substituteNote(lesson)]
        .filter(Boolean)
        .join(' · ')}
      right={<Badge tone={badge.tone}>{badge.text}</Badge>}
      onClick={onClick}
    />
  )
}
