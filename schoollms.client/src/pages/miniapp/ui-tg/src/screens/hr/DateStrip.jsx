/**
 * Sana tanlagich: 7 kunlik lenta + haftama-hafta ikki tomonga surish
 * (eski `components/DateStrip.tsx`, sariq hero ichida).
 *
 * - Kelajak kunlar OCHIQ: standart lenta bugundan BIR KUN oldinga cho'ziladi
 *   (`AHEAD_DAYS`), "Ertaga" doim bir bosish narida.
 * - Oldinga surish `MAX_WEEKS_AHEAD` bilan cheklangan, orqaga — cheklov yo'q.
 * - Tanlangan kun TASHQARIDAN o'zgarsa ("Bugunga qaytish", boshqa kunga qo'yilgan
 *   topshiriq) lenta o'sha kunga o'zi suriladi — holat render paytida to'g'rilanadi,
 *   `useEffect` bilan lenta bir kadr eski joyida ko'rinib, keyin sakrardi.
 */
import { useState } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { addDays, dayOfMonth, diffDays, shortWeekday } from '../../lib/hrFormat'
import { hapticSelect } from '../../lib/telegram'
import { t } from './texts'

const VISIBLE_DAYS = 7
const AHEAD_DAYS = 1
const MAX_WEEKS_AHEAD = 4

function shiftFor(date, today) {
  return Math.ceil((diffDays(date, today) - AHEAD_DAYS) / VISIBLE_DAYS)
}

export function DateStrip({ selected, today, onSelect }) {
  const [weekShift, setWeekShift] = useState(() => shiftFor(selected, today))
  const [tracked, setTracked] = useState(selected)
  if (tracked !== selected) {
    setTracked(selected)
    const needed = shiftFor(selected, today)
    if (needed !== weekShift) setWeekShift(needed)
  }

  const days = []
  for (let i = 0; i < VISIBLE_DAYS; i += 1) {
    days.push(addDays(today, i - (VISIBLE_DAYS - 1) + AHEAD_DAYS + weekShift * VISIBLE_DAYS))
  }

  const pick = (date) => {
    if (date === selected) return
    hapticSelect()
    onSelect(date)
  }

  return (
    <div className="flex items-center gap-0.5">
      <button
        type="button"
        aria-label={t('date.prev_week')}
        onClick={() => {
          hapticSelect()
          setWeekShift((v) => v - 1)
        }}
        className="shrink-0 rounded-full p-1.5 active:bg-brand-dark"
      >
        <ChevronLeft className="h-5 w-5" />
      </button>

      <div className="flex min-w-0 flex-1 gap-0.5">
        {days.map((date) => {
          const isToday = date === today
          const isSelected = date === selected
          const delta = diffDays(date, today)
          const muted = !isSelected && (delta < -6 || delta > AHEAD_DAYS)
          return (
            <button
              key={date}
              type="button"
              aria-pressed={isSelected}
              aria-label={date}
              onClick={() => pick(date)}
              className={
                'flex min-w-0 flex-1 flex-col items-center rounded-xl py-1.5 transition ' +
                (isSelected ? 'bg-brand-ink text-brand' : 'text-brand-ink') +
                (muted ? ' opacity-45' : '')
              }
            >
              <span className={'text-[11px] leading-tight ' + (isSelected ? '' : 'opacity-70')}>
                {shortWeekday(date)}
              </span>
              <span className={'text-[17px] leading-tight tabular-nums ' + (isToday || isSelected ? 'font-extrabold' : 'font-semibold')}>
                {dayOfMonth(date)}
              </span>
              {/* Nuqta bo'lmaganda ham AYNI o'lchamdagi joy qoladi — qator sakramasin. */}
              <span
                className={
                  'mt-0.5 h-1 w-1 rounded-full ' +
                  (isToday ? (isSelected ? 'bg-brand' : 'bg-brand-ink') : 'bg-transparent')
                }
              />
            </button>
          )
        })}
      </div>

      <button
        type="button"
        aria-label={t('date.next_week')}
        disabled={weekShift >= MAX_WEEKS_AHEAD}
        onClick={() => {
          hapticSelect()
          setWeekShift((v) => Math.min(MAX_WEEKS_AHEAD, v + 1))
        }}
        className="shrink-0 rounded-full p-1.5 active:bg-brand-dark disabled:opacity-30"
      >
        <ChevronRight className="h-5 w-5" />
      </button>
    </div>
  )
}
