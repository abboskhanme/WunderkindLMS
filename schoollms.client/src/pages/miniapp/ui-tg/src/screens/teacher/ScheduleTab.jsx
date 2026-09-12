/**
 * JADVAL — bitta hafta, kunlar bo'yicha.
 *
 * Hafta o'tkagichi chorak chegarasidan o'zi o'tadi: o'qituvchi "3-chorak
 * 1-hafta" ni qidirib yurmasligi kerak, u shunchaki oldinga bosadi. Hafta
 * raqami va sanalari serverdagi `ScheduleMath` bilan bir xil hisoblanadi
 * (`lib/weeks.js`), aks holda ilova bir haftani, server boshqasini ko'rsatardi.
 */
import { useMemo, useState } from 'react'
import { CalendarOff } from 'lucide-react'
import {
  Badge, Card, DateChip, EmptyState, Hero, Row, Screen, Stepper,
} from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { axisIndexOf, dateOfWeekday, todayISO, weekAxis } from '../../lib/weeks'
import { dayMonth, weekdayName } from '../../lib/format'
import { haptic } from '../../lib/telegram'
import { AsyncBlock, lessonKey } from './shared'

const DAYS = [0, 1, 2, 3, 4, 5] // dushanba … shanba

export function ScheduleTab({ meta }) {
  // O'quv yilining hamma haftalari bitta o'qda — Stepper shu bo'ylab yuradi.
  const axis = useMemo(() => weekAxis(meta.quarters), [meta.quarters])
  const startIndex = Math.max(0, axisIndexOf(axis, meta.currentQuarter, meta.currentWeek))
  const [index, setIndex] = useState(startIndex)

  const spot = axis[index] || null
  const quarter = spot?.quarter ?? meta.currentQuarter
  const week = spot?.week ?? meta.currentWeek

  const q = useAsync(() => teacherApi.schedule(quarter, week), [quarter, week])

  const today = todayISO()
  const step = (delta) => {
    haptic()
    setIndex((i) => Math.min(axis.length - 1, Math.max(0, i + delta)))
  }

  const byDay = useMemo(() => {
    const map = new Map(DAYS.map((d) => [d, []]))
    for (const l of q.data || []) {
      if (map.has(l.day)) map.get(l.day).push(l)
    }
    for (const list of map.values()) list.sort((a, b) => a.period - b.period)
    return map
  }, [q.data])

  const total = (q.data || []).length

  return (
    <Screen>
      <Hero
        title="Dars jadvali"
        subtitle={spot ? `${quarter}-chorak · ${week}-hafta` : "O'quv yili sozlanmagan"}
      >
        {axis.length > 0 && (
          <>
            <Stepper
              label={`${quarter}-chorak · ${week}-hafta`}
              onPrev={() => step(-1)}
              onNext={() => step(1)}
              disabledNext={index >= axis.length - 1}
            />
            <p className="mt-1 text-center text-[13px] opacity-70">
              {dayMonth(spot.startISO)} — {dayMonth(spot.endISO)}
              {total > 0 ? ` · ${total} dars` : ''}
            </p>
          </>
        )}
      </Hero>

      {axis.length === 0 ? (
        <Card>
          <EmptyState
            title="Choraklar belgilanmagan"
            note="Maktab sozlamalarida o'quv yili choraklari kiritilmagan, shuning uchun haftalarni hisoblab bo'lmaydi."
          />
        </Card>
      ) : (
        <AsyncBlock query={q} loadingLabel="Jadval yuklanmoqda…">
          {(lessons) =>
            lessons.length === 0 ? (
              <Card>
                <EmptyState
                  icon={<CalendarOff className="h-9 w-9" />}
                  title="Bu haftada dars yo'q"
                  note={`${quarter}-chorak ${week}-haftaga sizning sinflaringizga jadval biriktirilmagan (ta'til yoki jadval hali tuzilmagan).`}
                />
              </Card>
            ) : (
              <>
                {DAYS.map((day) => {
                  const list = byDay.get(day) || []
                  if (list.length === 0) return null
                  const date = dateOfWeekday(spot.startISO, day)
                  const isToday = date === today
                  return (
                    <Card
                      key={day}
                      title={weekdayName(day)}
                      action={
                        <Badge tone={isToday ? 'brand' : 'neutral'}>
                          {isToday ? 'Bugun' : dayMonth(date)}
                        </Badge>
                      }
                      className={isToday ? 'ring-2 ring-brand' : ''}
                    >
                      {list.map((l) => (
                        <Row
                          key={lessonKey(l)}
                          lead={
                            <DateChip day={l.period} month="dars" tone={isToday ? 'brand' : 'neutral'} />
                          }
                          title={`${l.className}${l.subGroup > 0 ? ` · ${l.subGroup}-guruh` : ''}`}
                          subtitle={l.subjectName}
                          right={
                            <span className="shrink-0 text-right text-[13px] font-semibold text-slate-500">
                              {l.startTime}
                              <span className="block text-[11px] font-normal text-slate-400">
                                {l.endTime}
                              </span>
                            </span>
                          }
                        />
                      ))}
                    </Card>
                  )
                })}
                <div className="h-4" />
              </>
            )
          }
        </AsyncBlock>
      )}
    </Screen>
  )
}
