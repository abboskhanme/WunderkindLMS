import { useEffect, useMemo, useState } from 'react'
import { CalendarOff, Trash2 } from 'lucide-react'
import type { Holiday, SchoolSettings } from '@/types'
import { getHolidays, saveHoliday, deleteHoliday } from '@/api/services/holidays'
import { getSettings } from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]
const DOW = ['Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh', 'Ya']

const iso = (y: number, m: number, d: number) =>
  `${y}-${String(m + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`

/** Oyning kunlarini dushanba-boshli haftalarga bo'ladi (bo'sh kataklar null). */
function monthGrid(year: number, month: number): (number | null)[][] {
  const first = new Date(year, month, 1)
  const startDow = (first.getDay() + 6) % 7 // 0=Du
  const days = new Date(year, month + 1, 0).getDate()
  const cells: (number | null)[] = Array(startDow).fill(null)
  for (let d = 1; d <= days; d++) cells.push(d)
  while (cells.length % 7 !== 0) cells.push(null)
  const weeks: (number | null)[][] = []
  for (let i = 0; i < cells.length; i += 7) weeks.push(cells.slice(i, i + 7))
  return weeks
}

/**
 * Bayram kunlari — yillik kalendar. Kunni bosib bayram qilib belgilash/olib tashlash.
 * Belgilangan kunlarda hech bir sinfda dars bo'lmaydi (jadval + jurnal).
 */
export function HolidaysPage() {
  const [holidays, setHolidays] = useState<Holiday[]>([])
  const [settings, setSettings] = useState<SchoolSettings | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState<string | null>(null)

  useEffect(() => {
    Promise.all([getHolidays(), getSettings()])
      .then(([h, s]) => {
        setHolidays(h)
        setSettings(s)
      })
      .finally(() => setLoading(false))
  }, [])

  // O'quv yili sanalaridan yillarni aniqlaymiz (bo'lmasa joriy yil).
  const years = useMemo(() => {
    const set = new Set<number>()
    settings?.quarters?.forEach((q) => {
      if (q.startDate) set.add(Number(q.startDate.slice(0, 4)))
      if (q.endDate) set.add(Number(q.endDate.slice(0, 4)))
    })
    if (set.size === 0) set.add(new Date().getFullYear())
    return [...set].filter((y) => y > 1970).sort((a, b) => a - b)
  }, [settings])

  const [year, setYear] = useState<number>(() => new Date().getFullYear())
  useEffect(() => {
    if (years.length && !years.includes(year)) setYear(years[0])
  }, [years, year])

  const holidayMap = useMemo(() => {
    const m = new Map<string, string>()
    holidays.forEach((h) => m.set(h.date, h.name))
    return m
  }, [holidays])

  const toggle = async (date: string) => {
    if (busy) return
    setBusy(date)
    try {
      if (holidayMap.has(date)) {
        await deleteHoliday(date)
        setHolidays((p) => p.filter((h) => h.date !== date))
      } else {
        const h = await saveHoliday(date, '')
        setHolidays((p) => [...p, h].sort((a, b) => a.date.localeCompare(b.date)))
      }
    } finally {
      setBusy(null)
    }
  }

  const rename = (date: string, name: string) => {
    setHolidays((p) => p.map((h) => (h.date === date ? { ...h, name } : h)))
  }
  const commitName = (date: string, name: string) => saveHoliday(date, name)

  const yearHolidays = holidays
    .filter((h) => h.date.startsWith(`${year}-`))
    .sort((a, b) => a.date.localeCompare(b.date))

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Bayram kunlari</h1>
          <p className="text-sm text-slate-400">
            Kunni bosib bayram/dam olish kuni qilib belgilang — o'sha kunlari hech bir sinfda dars bo'lmaydi
          </p>
        </div>
        <div className="flex gap-1 rounded-lg bg-slate-100 p-1">
          {years.map((y) => (
            <button
              key={y}
              type="button"
              onClick={() => setYear(y)}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                y === year ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              {y}
            </button>
          ))}
        </div>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1fr_280px]">
          {/* Kalendar */}
          <Card>
            <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 xl:grid-cols-3">
              {MONTHS.map((mName, month) => (
                <div key={month}>
                  <p className="mb-1.5 text-sm font-semibold text-slate-700">{mName}</p>
                  <div className="grid grid-cols-7 gap-0.5 text-center">
                    {DOW.map((d, i) => (
                      <div
                        key={d}
                        className={cn('py-0.5 text-[10px] font-medium', i === 6 ? 'text-red-400' : 'text-slate-400')}
                      >
                        {d}
                      </div>
                    ))}
                    {monthGrid(year, month).flat().map((day, i) => {
                      if (day === null) return <div key={i} />
                      const date = iso(year, month, day)
                      const isHoliday = holidayMap.has(date)
                      const isSunday = i % 7 === 6
                      return (
                        <button
                          key={i}
                          type="button"
                          onClick={() => toggle(date)}
                          disabled={busy === date}
                          title={isHoliday ? (holidayMap.get(date) || 'Bayram') : 'Bayram qilib belgilash'}
                          className={cn(
                            'aspect-square rounded-md text-xs transition-colors',
                            isHoliday
                              ? 'bg-red-500 font-semibold text-white hover:bg-red-600'
                              : isSunday
                                ? 'text-red-400 hover:bg-slate-100'
                                : 'text-slate-600 hover:bg-slate-100',
                          )}
                        >
                          {day}
                        </button>
                      )
                    })}
                  </div>
                </div>
              ))}
            </div>
          </Card>

          {/* Belgilangan kunlar + nom */}
          <Card className="h-fit">
            <div className="mb-3 flex items-center gap-2">
              <CalendarOff className="h-4 w-4 text-red-500" />
              <p className="font-semibold text-slate-800">{year}: {yearHolidays.length} ta kun</p>
            </div>
            {yearHolidays.length === 0 ? (
              <p className="py-6 text-center text-sm text-slate-400">
                Bayram kuni belgilanmagan. Kalendardan kun tanlang.
              </p>
            ) : (
              <div className="space-y-2">
                {yearHolidays.map((h) => (
                  <div key={h.date} className="flex items-center gap-2">
                    <span className="w-[88px] shrink-0 text-xs font-medium text-slate-600">{h.date}</span>
                    <input
                      value={h.name}
                      onChange={(e) => rename(h.date, e.target.value)}
                      onBlur={(e) => commitName(h.date, e.target.value)}
                      placeholder="Nomi (ixtiyoriy)"
                      className="min-w-0 flex-1 rounded-md border border-slate-200 px-2 py-1 text-xs outline-none focus:border-brand-400"
                    />
                    <button
                      type="button"
                      onClick={() => toggle(h.date)}
                      title="O'chirish"
                      className="shrink-0 rounded-md p-1 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </div>
                ))}
              </div>
            )}
          </Card>
        </div>
      )}
    </div>
  )
}
