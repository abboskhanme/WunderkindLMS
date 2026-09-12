/**
 * OVQAT — oshxonaning haftalik menyusi.
 *
 * MENYU BUTUN MAKTAB UCHUN BITTA (bugungi backendda), lekin so'rov baribir
 * farzand id'si bilan yuboriladi — `parentApi.getMenu` izohiga qarang.
 *
 * BO'SH KUN — ODATIY HOL. Oshxona menyuni bir necha kunga oldindan kiritadi,
 * ya'ni kelasi hafta ko'pincha bo'sh bo'ladi. Shuning uchun bo'sh holat
 * "xato" kabi emas, SABABI bilan ko'rsatiladi: "menyu hali kiritilmagan".
 */
import { Fragment, useState } from 'react'
import { UtensilsCrossed } from 'lucide-react'
import { Badge, Card, EmptyState, Row, Stepper } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { dayMonth, weekdayName } from '../../lib/format'
import { haptic } from '../../lib/telegram'
import { getMenu } from '../../lib/parentApi'
import { AsyncBlock } from './shared'
import { addDays, todayISO } from '../../lib/weeks'
import { calendarWeek, lessonDow, parseISO } from './weeks'

/** Server kalitlari → ota-ona tili. Tartib ham shu — ertalabdan kechgacha. */
const MEALS = [
  { key: 'breakfast', label: 'Nonushta' },
  { key: 'lunch', label: 'Tushlik' },
  { key: 'dinner', label: 'Kechki ovqat' },
]

/** Stepper sarlavhasi — sanalar pastdagi qatorda, bu yerda faqat yo'nalish. */
function weekLabel(offset) {
  if (offset === 0) return 'Shu hafta'
  if (offset === -1) return "O'tgan hafta"
  if (offset === 1) return 'Kelasi hafta'
  return offset < 0 ? `${-offset} hafta oldin` : `${offset} hafta keyin`
}

export function MenuTab({ child }) {
  // Haftalar bugungi haftadan sanaladi: 0 — shu hafta, −1 — o'tgan hafta.
  const [offset, setOffset] = useState(0)
  const base = addDays(todayISO(), offset * 7)
  const { startISO, endISO } = calendarWeek(base)

  const state = useAsync(() => getMenu(child.id, startISO, endISO), [child.id, startISO, endISO])

  const move = (delta) => {
    haptic('light')
    setOffset((n) => n + delta)
  }

  return (
    <>
      <Card>
        <div className="px-2 pb-3 pt-2">
          <Stepper
            label={weekLabel(offset)}
            onPrev={() => move(-1)}
            onNext={() => move(1)}
          />
          <p className="text-center text-[13px] text-slate-500">
            {dayMonth(parseISO(startISO))} – {dayMonth(parseISO(endISO))}
          </p>
        </div>
      </Card>

      <AsyncBlock state={state} loadingLabel="Menyu yuklanmoqda…">
        {(days) => <WeekMenu days={days ?? []} />}
      </AsyncBlock>
    </>
  )
}

function WeekMenu({ days }) {
  const today = todayISO()
  const filled = days.filter((d) => MEALS.some((m) => (d.meals?.[m.key] ?? []).length > 0))

  if (filled.length === 0) {
    return (
      <Card>
        <EmptyState
          icon={<UtensilsCrossed className="h-8 w-8" />}
          title="Bu haftaga menyu kiritilmagan"
          note="Oshxona menyuni odatda bir necha kun oldin kiritadi. Kiritilgach shu yerda taomlar va tarkibi ko'rinadi."
        />
      </Card>
    )
  }

  return (
    <>
      {filled.map((d) => (
        <DayMenu key={d.date} day={d} isToday={d.date === today} />
      ))}
    </>
  )
}

function DayMenu({ day, isToday }) {
  return (
    <Card className={isToday ? 'ring-2 ring-brand' : ''}>
      <div
        className={
          'flex items-center justify-between px-4 py-3 ' + (isToday ? 'bg-brand/25' : 'bg-slate-50')
        }
      >
        <div>
          <p className="text-[15px] font-bold">{weekdayName(lessonDow(day.date))}</p>
          <p className="text-[13px] text-slate-500">{dayMonth(parseISO(day.date))}</p>
        </div>
        {isToday && <Badge tone="brand">Bugun</Badge>}
      </div>

      {MEALS.map((meal) => {
        const dishes = day.meals?.[meal.key] ?? []
        if (dishes.length === 0) return null
        return (
          // Ovqat nomi o'rovchi <div> dan TASHQARIDA: shunda ichkaridagi
          // birinchi taom `Row` ning `first:border-t-0` iga tushadi va
          // sarlavha bilan birinchi taom orasida ortiqcha chiziq chiqmaydi.
          <Fragment key={meal.key}>
            <p className="border-t border-slate-100 px-4 pb-1 pt-3 text-[13px] font-semibold uppercase tracking-wide text-slate-400">
              {meal.label}
            </p>
            <div>
              {dishes.map((dish) => (
                <Row
                  key={dish.id}
                  lead={
                    dish.imageUrl ? (
                      <img
                        src={dish.imageUrl}
                        alt=""
                        className="h-11 w-11 shrink-0 rounded-xl object-cover"
                      />
                    ) : (
                      <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
                        <UtensilsCrossed className="h-5 w-5" />
                      </div>
                    )
                  }
                  title={dish.name}
                  subtitle={dish.ingredients}
                />
              ))}
            </div>
          </Fragment>
        )
      })}
    </Card>
  )
}
