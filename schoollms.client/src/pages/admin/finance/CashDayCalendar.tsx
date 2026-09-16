/**
 * "Kassa kuni" ning OY GRIDI — har katakda kunning sof harakati.
 *
 * Katakcha bosilganda panel o'sha kunga qayta yuklanadi, ya'ni bu jadval
 * emas, NAVIGATSIYA: direktor "qaysi kuni pul kirmagan?" degan savolga
 * ko'zi bilan javob topadi va o'sha kunga kiradi.
 *
 * RAQAM SERVERDAN. Bu fayl birorta summani qo'shmaydi yoki ayirmaydi —
 * `net` ham, `closing` ham `CashDayQueries.MonthAsync` dan tayyor keladi
 * (`BillingDtos.cs` 2-qoidasi: pulni frontend hisoblamaydi).
 */
import { ChevronLeft, ChevronRight } from 'lucide-react'
import type { CashMonth, CashMonthDay } from '@/api/services/cashDay'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn, formatMoney } from '@/lib/utils'
import { formatSignedMoney, shortAmount, signClass } from './reportLabels'

/** Dushanbadan boshlanadi — O'zbekistonda hafta shunday boshlanadi. */
const weekdays = ['Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh', 'Ya']

interface Props {
  month: CashMonth | null
  loading: boolean
  /** Hozir tanlangan kun, "YYYY-MM-DD". */
  selected: string
  /** Bugun, "YYYY-MM-DD" — server mintaqasidagi kun (sahifa beradi). */
  today: string
  onSelect: (date: string) => void
  onShift: (delta: number) => void
}

export function CashDayCalendar({ month, loading, selected, today, onSelect, onShift }: Props) {
  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 p-4">
        <div>
          <h2 className="font-semibold text-slate-800">Oylik kalendar</h2>
          <p className="text-sm text-slate-400">
            Har katakda — kunning sof harakati. Kunni tanlash uchun bosing.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Button variant="secondary" onClick={() => onShift(-1)} aria-label="Oldingi oy">
            <ChevronLeft className="h-4 w-4" />
          </Button>
          <span className="min-w-28 text-center text-sm font-medium text-slate-700">
            {month ? monthTitle(month.month) : '—'}
          </span>
          <Button variant="secondary" onClick={() => onShift(1)} aria-label="Keyingi oy">
            <ChevronRight className="h-4 w-4" />
          </Button>
        </div>
      </div>

      {loading || !month ? (
        <div className="p-6">
          <Loader label="Kalendar yuklanmoqda…" />
        </div>
      ) : (
        <div className="p-4">
          <div className="grid grid-cols-7 gap-1.5">
            {weekdays.map((name) => (
              <div
                key={name}
                className="pb-1 text-center text-xs font-medium uppercase tracking-wide text-slate-400"
              >
                {name}
              </div>
            ))}

            {/* Oyning 1-kuni haftaning qaysi kuniga tushishiga qarab bo'sh katakchalar. */}
            {Array.from({ length: leadingBlanks(month.days[0]) }, (_, i) => (
              <div key={`blank-${i}`} />
            ))}

            {month.days.map((day) => (
              <DayCell
                key={day.date}
                day={day}
                isSelected={day.date === selected}
                isToday={day.date === today}
                onSelect={onSelect}
              />
            ))}
          </div>

          <div className="mt-4 flex flex-wrap items-center gap-x-6 gap-y-1 border-t border-slate-100 pt-3 text-sm">
            <span className="text-slate-400">
              Oy boshi: <span className="text-slate-600">{formatMoney(month.opening)}</span>
            </span>
            <span className="text-slate-400">
              Kirim: <span className="text-emerald-600">{formatMoney(month.inflow)}</span>
            </span>
            <span className="text-slate-400">
              Chiqim: <span className="text-red-600">{formatMoney(month.outflow)}</span>
            </span>
            <span className="text-slate-400">
              Sof: <span className={signClass(month.net)}>{formatSignedMoney(month.net)}</span>
            </span>
            <span className="text-slate-400">
              Oy oxiri:{' '}
              <span className="font-medium text-slate-700">{formatMoney(month.closing)}</span>
            </span>
          </div>
        </div>
      )}
    </Card>
  )
}

function DayCell({
  day,
  isSelected,
  isToday,
  onSelect,
}: {
  day: CashMonthDay
  isSelected: boolean
  isToday: boolean
  onSelect: (date: string) => void
}) {
  return (
    <button
      type="button"
      onClick={() => onSelect(day.date)}
      title={`${day.date} · kun oxiridagi qoldiq: ${formatMoney(day.closing)}`}
      className={cn(
        'flex h-16 flex-col items-start justify-between rounded-xl border p-1.5 text-left transition-colors',
        isSelected
          ? 'border-brand-400 bg-brand-50'
          : day.hasMovement
            ? 'border-slate-200 bg-white hover:bg-slate-50'
            : 'border-slate-100 bg-slate-50/60 hover:bg-slate-100/70',
      )}
    >
      <span
        className={cn(
          'text-xs font-medium',
          isToday ? 'rounded bg-slate-800 px-1.5 text-white' : 'text-slate-500',
        )}
      >
        {Number(day.date.slice(8, 10))}
      </span>

      <span className={cn('w-full truncate text-xs font-semibold', signClass(day.net))}>
        {day.hasMovement ? withSign(day.net) : '—'}
      </span>
    </button>
  )
}

/**
 * Katakcha tor, shuning uchun qisqartirilgan summa: 12 500 000 → "+12.5M".
 * To'liq raqam `title` da (sichqoncha ustiga borganda) va kun tanlanganda
 * panelning o'zida ko'rinadi.
 */
function withSign(net: number): string {
  if (net === 0) return '0'
  return net > 0 ? `+${shortAmount(net)}` : `−${shortAmount(Math.abs(net))}`
}

/**
 * Oyning 1-kunidan oldingi bo'sh katakchalar soni (dushanbadan boshlanadigan
 * hafta uchun).
 *
 * Sana `Date.UTC` bilan quriladi: `new Date("2003-09-01")` ni brauzer UTC
 * yarim tuni deb o'qiydi va manfiy ofsetli mintaqada `getDay()` bir kun
 * orqaga siljib ketardi. Bu yerda kun raqamlari satrdan olinadi, ya'ni
 * mintaqa umuman qatnashmaydi.
 */
function leadingBlanks(first: CashMonthDay | undefined): number {
  if (!first) return 0
  const [y, m, d] = first.date.split('-').map(Number)
  const weekday = new Date(Date.UTC(y, m - 1, d)).getUTCDay() // 0 = yakshanba
  return (weekday + 6) % 7
}

/** "2003-09-01" → "Sentyabr 2003" */
function monthTitle(iso: string): string {
  const names = [
    'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
    'Iyul', 'Avgust', 'Sentyabr', 'Oktyabr', 'Noyabr', 'Dekabr',
  ]
  const [year, month] = iso.split('-')
  return `${names[Number(month) - 1] ?? month} ${year}`
}
