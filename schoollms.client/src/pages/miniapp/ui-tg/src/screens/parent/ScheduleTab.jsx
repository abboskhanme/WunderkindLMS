/**
 * JADVAL — farzandning bir haftasi.
 *
 * HAFTA — YAGONA O'Q. Backend jadvalni `?quarter=&week=` bo'yicha beradi,
 * ya'ni "keyingi hafta" degan tugma chorak chegarasidan o'tishi kerak.
 * Shuning uchun butun o'quv yilining haftalari bitta tekis ro'yxatga
 * yig'iladi (`weekAxis`, `src/lib/weeks.js`) va `Stepper` shu ro'yxat bo'ylab yuradi:
 * 1-chorakning oxirgi haftasidan keyin 2-chorakning birinchisi keladi,
 * hech qanday alohida holat yo'q.
 *
 * NEGA `/journal`, `/schedule` EMAS. Jurnal javobida SANA bor — shu sababli
 * "bugun" ni taxmin qilmasdan belgilash mumkin; ustiga mavzu, uy vazifasi,
 * qo'yilgan baho va davomat sababi ham o'sha bitta so'rovda keladi.
 *
 * Stepper HAR DOIM ko'rinib turadi — hafta yuklanayotganda ham. Aks holda
 * har bosishda tugmalar yo'qolib, ota-ona qayerda ekanini yo'qotadi.
 */
import { useState } from 'react'
import { CalendarOff } from 'lucide-react'
import { Badge, Card, DateChip, EmptyState, Row, Stepper } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { dayMonth, weekdayName } from '../../lib/format'
import { haptic } from '../../lib/telegram'
import { getMeta, getWeek } from '../../lib/parentApi'
import { AsyncBlock, gradeTone } from './shared'
import { addDays, todayISO, weekAxis } from '../../lib/weeks'
import { lessonDow, parseISO, weekIndexFor } from './weeks'

export function ScheduleTab({ child }) {
  const state = useAsync(async () => {
    const meta = await getMeta()
    return { meta, weeks: weekAxis(meta.quarters) }
  }, [child.id])

  return (
    <AsyncBlock state={state} loadingLabel="Jadval yuklanmoqda…">
      {({ weeks }) => {
        if (weeks.length === 0) {
          return (
            <Card>
              <EmptyState
                icon={<CalendarOff className="h-8 w-8" />}
                title="O'quv yili kiritilmagan"
                note="Choraklar sanalari belgilanmagani uchun jadvalni ko'rsatib bo'lmaydi. Maktab ma'muriyatiga xabar bering."
              />
            </Card>
          )
        }
        const start = Math.max(0, weekIndexFor(weeks, todayISO()))
        return <WeekView child={child} weeks={weeks} initialIndex={start} />
      }}
    </AsyncBlock>
  )
}

function WeekView({ child, weeks, initialIndex }) {
  const [index, setIndex] = useState(initialIndex)
  const week = weeks[index]

  const state = useAsync(
    () => getWeek(child.id, week.quarter, week.week),
    [child.id, week.quarter, week.week],
  )

  const move = (delta) => {
    const next = index + delta
    if (next < 0 || next >= weeks.length) return
    haptic('light')
    setIndex(next)
  }

  return (
    <>
      <Card>
        <div className="px-2 pb-3 pt-2">
          <Stepper
            label={`${week.week}-hafta`}
            onPrev={() => move(-1)}
            onNext={() => move(1)}
            disabledPrev={index <= 0}
            disabledNext={index >= weeks.length - 1}
          />
          <p className="text-center text-[13px] text-slate-500">
            {week.quarter}-chorak · {dayMonth(parseISO(week.startISO))} – {dayMonth(parseISO(week.endISO))}
          </p>
        </div>
      </Card>

      <AsyncBlock state={state} loadingLabel="Hafta yuklanmoqda…">
        {(rows) => <WeekDays week={week} rows={rows} />}
      </AsyncBlock>
    </>
  )
}

function WeekDays({ week, rows }) {
  const today = todayISO()

  // Kunlar SANA bo'yicha yig'iladi — jurnal qatorlari allaqachon shu haftaga
  // qisilgan holda keladi.
  const byDate = new Map()
  for (const r of rows) {
    if (!byDate.has(r.date)) byDate.set(r.date, [])
    byDate.get(r.date).push(r)
  }

  // Haftaning kunlari: dushanbadan shanbagacha, chorak chetiga qisilgan.
  const days = []
  for (let iso = week.startISO; iso <= week.endISO; iso = addDays(iso, 1)) {
    const lessons = (byDate.get(iso) ?? []).sort((a, b) => a.period - b.period)
    // Darssiz kun ro'yxatni cho'zadi — faqat bugun bo'lsa ko'rsatamiz,
    // chunki "bugun nima bor" degan savolga javob berilishi kerak.
    if (lessons.length === 0 && iso !== today) continue
    days.push({ iso, lessons })
  }

  if (days.length === 0) {
    return (
      <Card>
        <EmptyState
          icon={<CalendarOff className="h-8 w-8" />}
          title="Bu haftada dars yo'q"
          note="Bu haftaga jadval kiritilmagan yoki ta'til kunlari. Boshqa haftani strelka bilan tanlang."
        />
      </Card>
    )
  }

  return (
    <>
      {days.map((d) => (
        <DayCard key={d.iso} iso={d.iso} lessons={d.lessons} isToday={d.iso === today} />
      ))}
    </>
  )
}

function DayCard({ iso, lessons, isToday }) {
  return (
    <Card className={isToday ? 'ring-2 ring-brand' : ''}>
      <div
        className={
          'flex items-center justify-between px-4 py-3 ' +
          (isToday ? 'bg-brand/25' : 'bg-slate-50')
        }
      >
        <div>
          <p className="text-[15px] font-bold">{weekdayName(lessonDow(iso))}</p>
          <p className="text-[13px] text-slate-500">{dayMonth(parseISO(iso))}</p>
        </div>
        {isToday && <Badge tone="brand">Bugun</Badge>}
      </div>

      {lessons.length === 0 ? (
        <EmptyState title="Bugun dars yo'q" note="Jadvalda bu kunga dars qo'yilmagan." />
      ) : (
        lessons.map((l) => (
          <Row
            key={`${l.ownerKind ?? 'class'}-${l.period}-${l.subjectId}`}
            lead={
              <DateChip
                day={l.period}
                month="dars"
                tone={l.reasonId ? 'danger' : isToday ? 'brand' : 'neutral'}
              />
            }
            title={l.subjectName}
            subtitle={
              <>
                {[l.startTime && l.endTime ? `${l.startTime}–${l.endTime}` : null, l.teacherName]
                  .filter(Boolean)
                  .join(' · ')}
                {/* Guruh darsi — sinf jadvalidan tashqarida, shuning uchun
                    ota-ona uni nima ekanini bilishi kerak (G-18). */}
                {l.ownerKind === 'group' && l.ownerName && (
                  <>
                    <br />
                    Guruh: {l.ownerName}
                  </>
                )}
                {l.homework && (
                  <>
                    <br />
                    Uy vazifasi: {l.homework}
                  </>
                )}
              </>
            }
            right={
              l.reasonId ? (
                <Badge tone={l.isLate ? 'brand' : 'danger'}>{l.reasonName}</Badge>
              ) : l.grade ? (
                <Badge tone={gradeTone(l.grade)}>{l.grade}</Badge>
              ) : null
            }
          />
        ))
      )}
    </Card>
  )
}
