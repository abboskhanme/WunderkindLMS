import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { ChevronDown, ChevronLeft, ChevronRight, X } from 'lucide-react'
import { cn } from '@/lib/utils'

/* ==========================================================================
   Sana tanlash — butun dastur uchun YAGONA ko'rinish

   Mijoz, 2026-09-18: "dasturda barcha qismlarda date picker mana shunday
   stilda bo'lsin" (yuborilgan namuna: oy/yil ochiladigan ro'yxat, o'ng
   tomonda strelkalar, hafta kunlari sarlavhasi va yumaloq tanlangan kun).

   NEGA BRAUZERNIKI EMAS: `<input type="date">` har brauzerda boshqacha
   chiziladi (Chrome — kalendar ikonkasi, Safari — o'z oynasi, Firefox —
   uchinchi ko'rinish), va uni CSS bilan bir xil qilib bo'lmaydi. Shuning
   uchun maydon ham, kalendar ham shu yerda o'zimizniki.

   QIYMAT FORMATI O'ZGARMADI: `value`/`onChange` hamon "YYYY-MM-DD" —
   `input type="date"` bilan AYNAN bir xil. Shu sababli ekranlarni ko'chirish
   mexanik: `<input type="date" value={x} onChange={(e) => set(e.target.value)} />`
   → `<DatePicker value={x} onChange={set} />`, qolgan mantiq tegilmaydi.

   KALENDAR PORTAL ORQALI CHIZILADI: modal ichidagi maydon uchun oddiy
   `absolute` panel modalning `overflow-y-auto` chetiga urilib qirqilardi.
   Panel `document.body` ga chiqariladi va maydon o'rniga qarab joylashadi
   (pastda joy bo'lmasa — tepaga).
   ========================================================================== */

/** Qisqa oy nomlari — sarlavhadagi ro'yxat uchun. */
const MONTHS_SHORT = [
  'Yan', 'Fev', 'Mar', 'Apr', 'May', 'Iyn',
  'Iyl', 'Avg', 'Sen', 'Okt', 'Noy', 'Dek',
]

/** Hafta DUSHANBADAN boshlanadi — maktab jadvali ham shunday. */
const WEEKDAYS = ['DU', 'SE', 'CHO', 'PA', 'JU', 'SHA', 'YAK']

/** "YYYY-MM-DD" → "DD.MM.YYYY" (jadval va chek bilan bir xil ko'rinish). */
function display(iso: string): string {
  const [y, m, d] = iso.split('-')
  return y && m && d ? `${d}.${m}.${y}` : ''
}

/** "YYYY-MM-DD" — mahalliy vaqt bo'yicha (UTC'ga o'girilmaydi: kun surilib ketmasin). */
function toIso(year: number, month: number, day: number): string {
  return `${year}-${String(month + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}

function todayIso(): string {
  const now = new Date()
  return toIso(now.getFullYear(), now.getMonth(), now.getDate())
}

/** Oyning birinchi kuni haftaning nechanchi kuni (0 = dushanba). */
function leadingBlanks(year: number, month: number): number {
  return (new Date(year, month, 1).getDay() + 6) % 7
}

function daysInMonth(year: number, month: number): number {
  return new Date(year, month + 1, 0).getDate()
}

export interface DatePickerProps {
  /** "YYYY-MM-DD" yoki bo'sh satr. */
  value: string
  onChange: (value: string) => void
  /** Eng erta va eng kech tanlanadigan kun — "YYYY-MM-DD". */
  min?: string
  max?: string
  label?: string
  required?: boolean
  disabled?: boolean
  placeholder?: string
  /** Maydonda "×" — qiymatni bo'shatish (filtrlar uchun). */
  clearable?: boolean
  /** Maydonning o'ziga qo'shiladigan sinflar (masalan eni). */
  className?: string
  id?: string
  /** Qiymat noto'g'ri — maydon qizil chiziq bilan chiziladi. */
  invalid?: boolean
  /** Yorliqsiz maydonlar uchun (filtr qatorlari) — skrinrider o'qiydigan nom. */
  ariaLabel?: string
  /** Sichqoncha ustiga kelganda chiqadigan izoh (eski `title` atributi o'rnida). */
  title?: string
}

export function DatePicker({
  value,
  onChange,
  min,
  max,
  label,
  required,
  disabled,
  placeholder = 'kk.oo.yyyy',
  clearable,
  className,
  id,
  invalid,
  ariaLabel,
  title,
}: DatePickerProps) {
  const [open, setOpen] = useState(false)
  const fieldRef = useRef<HTMLDivElement>(null)
  const panelRef = useRef<HTMLDivElement>(null)

  // Ko'rinib turgan oy — tanlangan kun (yoki bugun) turgan oy.
  const anchor = value || todayIso()
  const [viewYear, setViewYear] = useState(() => Number(anchor.slice(0, 4)))
  const [viewMonth, setViewMonth] = useState(() => Number(anchor.slice(5, 7)) - 1)

  /**
   * Oynani ochish/yopish. Ochilayotganda ko'rinish tanlangan kunga
   * qaytariladi: kassir kecha martni varaqlagan bo'lsa ham, keyingi
   * ochilishda o'z kuni ko'rinsin. (Effekt emas — holat aynan shu
   * hodisada o'zgaradi, `react-hooks/set-state-in-effect` qoidasi.)
   */
  const toggle = () => {
    setOpen((wasOpen) => {
      if (!wasOpen) {
        const base = value || todayIso()
        setViewYear(Number(base.slice(0, 4)))
        setViewMonth(Number(base.slice(5, 7)) - 1)
      }
      return !wasOpen
    })
  }

  /* ---- Panelning joyi: maydon ostida, joy yetmasa — tepasida ---- */
  const [pos, setPos] = useState<{ top: number; left: number; width: number } | null>(null)

  const place = useCallback(() => {
    const field = fieldRef.current
    if (!field) return
    const rect = field.getBoundingClientRect()
    const panelHeight = panelRef.current?.offsetHeight ?? 340
    const below = window.innerHeight - rect.bottom
    const top = below < panelHeight + 12 && rect.top > panelHeight + 12
      ? rect.top - panelHeight - 8
      : rect.bottom + 8
    // Panel eni maydondan tor bo'lmasin, lekin ekrandan ham chiqmasin.
    const width = Math.max(rect.width, 288)
    const left = Math.min(Math.max(8, rect.left), Math.max(8, window.innerWidth - width - 8))
    setPos({ top, left, width })
  }, [])

  useLayoutEffect(() => {
    if (!open) return
    place()
  }, [open, place, viewMonth, viewYear])

  useEffect(() => {
    if (!open) return

    const onScrollOrResize = () => place()
    // `true` — ichki skrollar (modal tanasi) ham eshitiladi.
    window.addEventListener('scroll', onScrollOrResize, true)
    window.addEventListener('resize', onScrollOrResize)

    const onPointerDown = (e: MouseEvent) => {
      const target = e.target as Node
      if (fieldRef.current?.contains(target)) return
      if (panelRef.current?.contains(target)) return
      setOpen(false)
    }
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)

    return () => {
      window.removeEventListener('scroll', onScrollOrResize, true)
      window.removeEventListener('resize', onScrollOrResize)
      document.removeEventListener('mousedown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open, place])

  /* ---- Chegaralar ---- */
  const isDisabledDay = (iso: string) => (min !== undefined && iso < min) || (max !== undefined && iso > max)

  /**
   * Yillar ro'yxati. `min`/`max` berilgan bo'lsa — aynan o'sha oraliq;
   * berilmasa tug'ilgan sana ham tanlanadigan keng oraliq (1940 — joriy + 5).
   */
  const years = useMemo(() => {
    const now = new Date().getFullYear()
    const first = min ? Number(min.slice(0, 4)) : Math.min(1940, now - 85)
    const last = max ? Number(max.slice(0, 4)) : now + 5
    const from = Math.min(first, viewYear)
    const to = Math.max(last, viewYear)
    return Array.from({ length: to - from + 1 }, (_, i) => from + i)
  }, [min, max, viewYear])

  const step = (delta: number) => {
    const next = new Date(viewYear, viewMonth + delta, 1)
    setViewYear(next.getFullYear())
    setViewMonth(next.getMonth())
  }

  const pick = (day: number) => {
    const iso = toIso(viewYear, viewMonth, day)
    if (isDisabledDay(iso)) return
    onChange(iso)
    setOpen(false)
  }

  const blanks = leadingBlanks(viewYear, viewMonth)
  const total = daysInMonth(viewYear, viewMonth)
  const today = todayIso()

  /**
   * ENI: sana sig'adigan darajada (mijoz, 2026-09-18: "ekranda ko'rinadigan
   * joylari bunchalik keng bo'lmasin"). Chaqiruvchi o'z enini bersa
   * (`w-full`, `w-56`, `flex-1`), bu yerda umuman en sinfi qo'yilmaydi —
   * `cn` oddiy birlashtirish bo'lgani uchun ikkita `w-*` bir-biriga
   * qarshi turmasin.
   */
  const hasOwnWidth = /(^|\s)(w-|min-w-|max-w-|flex-|grow|basis-)/.test(className ?? '')

  const field = (
    <div
      ref={fieldRef}
      id={id}
      role="button"
      tabIndex={disabled ? -1 : 0}
      aria-label={ariaLabel ?? label}
      title={title}
      aria-haspopup="dialog"
      aria-expanded={open}
      aria-disabled={disabled}
      onClick={() => !disabled && toggle()}
      onKeyDown={(e) => {
        if (disabled) return
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault()
          toggle()
        }
      }}
      className={cn(
        'flex cursor-pointer items-center justify-between gap-2 rounded-lg border bg-white px-3 py-2 text-sm outline-none transition-colors',
        !hasOwnWidth && 'w-40',
        invalid
          ? 'border-red-300 focus:border-red-400 focus:ring-2 focus:ring-red-100'
          : 'border-slate-200 focus:border-brand-400 focus:ring-2 focus:ring-brand-100',
        open && !invalid && 'border-brand-400 ring-2 ring-brand-100',
        disabled && 'cursor-not-allowed bg-slate-50 text-slate-400',
        className,
      )}
    >
      <span className={cn('truncate tabular-nums', value ? 'text-slate-800' : 'text-slate-400')}>
        {value ? display(value) : placeholder}
      </span>

      {clearable && value && !disabled ? (
        <button
          type="button"
          aria-label="Sanani tozalash"
          onClick={(e) => {
            e.stopPropagation()
            onChange('')
          }}
          className="rounded-md p-0.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
        >
          <X className="h-3.5 w-3.5" />
        </button>
      ) : (
        <CalendarGlyph className={cn('h-4 w-4 shrink-0', disabled ? 'text-slate-300' : 'text-slate-400')} />
      )}
    </div>
  )

  const panel = open && pos !== null
    ? createPortal(
        <div
          ref={panelRef}
          role="dialog"
          style={{ top: pos.top, left: pos.left, width: pos.width }}
          className="fixed z-[70] rounded-2xl border border-slate-200 bg-white p-3 shadow-xl shadow-slate-900/10"
        >
          {/* --- Sarlavha: oy · yil · strelkalar --- */}
          <div className="mb-2 flex items-center justify-between gap-2">
            <div className="flex items-center gap-1">
              <HeaderSelect
                value={String(viewMonth)}
                onChange={(v) => setViewMonth(Number(v))}
                options={MONTHS_SHORT.map((name, i) => ({ value: String(i), label: name }))}
              />
              <HeaderSelect
                value={String(viewYear)}
                onChange={(v) => setViewYear(Number(v))}
                options={years.map((y) => ({ value: String(y), label: String(y) }))}
              />
            </div>

            <div className="flex items-center gap-1">
              <button
                type="button"
                aria-label="Oldingi oy"
                onClick={() => step(-1)}
                className="rounded-lg p-1.5 text-brand-600 transition-colors hover:bg-brand-50"
              >
                <ChevronLeft className="h-4 w-4" />
              </button>
              <button
                type="button"
                aria-label="Keyingi oy"
                onClick={() => step(1)}
                className="rounded-lg p-1.5 text-brand-600 transition-colors hover:bg-brand-50"
              >
                <ChevronRight className="h-4 w-4" />
              </button>
            </div>
          </div>

          {/* --- Hafta kunlari --- */}
          <div className="grid grid-cols-7 gap-1 pb-1">
            {WEEKDAYS.map((day) => (
              <span key={day} className="py-1 text-center text-[10px] font-medium tracking-wide text-slate-400">
                {day}
              </span>
            ))}
          </div>

          {/* --- Kunlar --- */}
          <div className="grid grid-cols-7 gap-1">
            {Array.from({ length: blanks }, (_, i) => <span key={`blank-${i}`} />)}
            {Array.from({ length: total }, (_, i) => {
              const day = i + 1
              const iso = toIso(viewYear, viewMonth, day)
              const unavailable = isDisabledDay(iso)
              const selected = iso === value
              return (
                <button
                  key={iso}
                  type="button"
                  disabled={unavailable}
                  onClick={() => pick(day)}
                  className={cn(
                    'flex h-9 items-center justify-center rounded-xl text-sm tabular-nums transition-colors',
                    unavailable && 'cursor-not-allowed text-slate-300',
                    !unavailable && !selected && 'text-slate-700 hover:bg-slate-100',
                    selected && 'bg-brand-600 font-semibold text-white hover:bg-brand-600',
                    !selected && !unavailable && iso === today && 'font-semibold text-brand-600',
                  )}
                >
                  {day}
                </button>
              )
            })}
          </div>

          {/* --- Bugun: bir bosishda joriy kunga qaytish --- */}
          {!isDisabledDay(today) && (
            <button
              type="button"
              onClick={() => {
                onChange(today)
                setOpen(false)
              }}
              className="mt-2 w-full rounded-lg py-1.5 text-xs font-medium text-brand-600 transition-colors hover:bg-brand-50"
            >
              Bugun
            </button>
          )}
        </div>,
        document.body,
      )
    : null

  if (!label) {
    return (
      <>
        {field}
        {panel}
      </>
    )
  }

  return (
    <div className={cn('block', hasOwnWidth && 'w-full')}>
      <span className="mb-1 block text-sm font-medium text-slate-600">
        {label}
        {required && <span className="text-red-500"> *</span>}
      </span>
      {field}
      {panel}
    </div>
  )
}

/* ==========================================================================
   Oy tanlash — sana tanlashning ukasi

   Hisobotlarda ("Qarzdorlar", "Abonement tranzaksiyalari", "Ish haqi", ...)
   kun emas, OY tanlanadi. Brauzerning `<input type="month">` i bo'sh holatda
   "--------- ----" bo'lib ko'rinadi va Safari'da umuman oddiy matn maydoni —
   shuning uchun u ham shu yerda o'zimizniki, `DatePicker` bilan bir xil
   ko'rinishda (mijoz: "dasturda barcha qismlarda ... mana shunday stilda").

   Qiymat "YYYY-MM" — `<input type="month">` bilan aynan bir xil.
   ========================================================================== */

const MONTHS_LONG = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

export interface MonthPickerProps {
  /** "YYYY-MM" yoki bo'sh satr. */
  value: string
  onChange: (value: string) => void
  /** Eng erta va eng kech oy — "YYYY-MM". */
  min?: string
  max?: string
  label?: string
  required?: boolean
  disabled?: boolean
  placeholder?: string
  clearable?: boolean
  className?: string
  ariaLabel?: string
  title?: string
  invalid?: boolean
}

export function MonthPicker({
  value,
  onChange,
  min,
  max,
  label,
  required,
  disabled,
  placeholder = 'Oyni tanlang',
  clearable,
  className,
  ariaLabel,
  title,
  invalid,
}: MonthPickerProps) {
  const [open, setOpen] = useState(false)
  const fieldRef = useRef<HTMLDivElement>(null)
  const panelRef = useRef<HTMLDivElement>(null)

  const thisYear = new Date().getFullYear()
  const [viewYear, setViewYear] = useState(() => (value ? Number(value.slice(0, 4)) : thisYear))

  const toggle = () => {
    setOpen((wasOpen) => {
      if (!wasOpen) setViewYear(value ? Number(value.slice(0, 4)) : thisYear)
      return !wasOpen
    })
  }

  const [pos, setPos] = useState<{ top: number; left: number; width: number } | null>(null)

  const place = useCallback(() => {
    const field = fieldRef.current
    if (!field) return
    const rect = field.getBoundingClientRect()
    const panelHeight = panelRef.current?.offsetHeight ?? 260
    const below = window.innerHeight - rect.bottom
    const top = below < panelHeight + 12 && rect.top > panelHeight + 12
      ? rect.top - panelHeight - 8
      : rect.bottom + 8
    const width = Math.max(rect.width, 260)
    const left = Math.min(Math.max(8, rect.left), Math.max(8, window.innerWidth - width - 8))
    setPos({ top, left, width })
  }, [])

  useLayoutEffect(() => {
    if (!open) return
    place()
  }, [open, place, viewYear])

  useEffect(() => {
    if (!open) return
    const onScrollOrResize = () => place()
    window.addEventListener('scroll', onScrollOrResize, true)
    window.addEventListener('resize', onScrollOrResize)
    const onPointerDown = (e: MouseEvent) => {
      const target = e.target as Node
      if (fieldRef.current?.contains(target)) return
      if (panelRef.current?.contains(target)) return
      setOpen(false)
    }
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      window.removeEventListener('scroll', onScrollOrResize, true)
      window.removeEventListener('resize', onScrollOrResize)
      document.removeEventListener('mousedown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open, place])

  const isDisabledMonth = (iso: string) =>
    (min !== undefined && iso < min) || (max !== undefined && iso > max)

  const pick = (monthIndex: number) => {
    const iso = `${viewYear}-${String(monthIndex + 1).padStart(2, '0')}`
    if (isDisabledMonth(iso)) return
    onChange(iso)
    setOpen(false)
  }

  const shown = value
    ? `${MONTHS_LONG[Number(value.slice(5, 7)) - 1] ?? ''} ${value.slice(0, 4)}`
    : ''

  const hasOwnWidth = /(^|\s)(w-|min-w-|max-w-|flex-|grow|basis-)/.test(className ?? '')

  const field = (
    <div
      ref={fieldRef}
      role="button"
      tabIndex={disabled ? -1 : 0}
      aria-label={ariaLabel ?? label}
      title={title}
      aria-haspopup="dialog"
      aria-expanded={open}
      aria-disabled={disabled}
      onClick={() => !disabled && toggle()}
      onKeyDown={(e) => {
        if (disabled) return
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault()
          toggle()
        }
      }}
      className={cn(
        'flex cursor-pointer items-center justify-between gap-2 rounded-lg border bg-white px-3 py-2 text-sm outline-none transition-colors',
        !hasOwnWidth && 'w-44',
        invalid
          ? 'border-red-300 focus:border-red-400 focus:ring-2 focus:ring-red-100'
          : 'border-slate-200 focus:border-brand-400 focus:ring-2 focus:ring-brand-100',
        open && !invalid && 'border-brand-400 ring-2 ring-brand-100',
        disabled && 'cursor-not-allowed bg-slate-50 text-slate-400',
        className,
      )}
    >
      <span className={cn('truncate', shown ? 'text-slate-800' : 'text-slate-400')}>
        {shown || placeholder}
      </span>

      {clearable && value && !disabled ? (
        <button
          type="button"
          aria-label="Oyni tozalash"
          onClick={(e) => {
            e.stopPropagation()
            onChange('')
          }}
          className="rounded-md p-0.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
        >
          <X className="h-3.5 w-3.5" />
        </button>
      ) : (
        <CalendarGlyph className={cn('h-4 w-4 shrink-0', disabled ? 'text-slate-300' : 'text-slate-400')} />
      )}
    </div>
  )

  const panel = open && pos !== null
    ? createPortal(
        <div
          ref={panelRef}
          role="dialog"
          style={{ top: pos.top, left: pos.left, width: pos.width }}
          className="fixed z-[70] rounded-2xl border border-slate-200 bg-white p-3 shadow-xl shadow-slate-900/10"
        >
          <div className="mb-2 flex items-center justify-between">
            <button
              type="button"
              aria-label="Oldingi yil"
              onClick={() => setViewYear((y) => y - 1)}
              className="rounded-lg p-1.5 text-brand-600 transition-colors hover:bg-brand-50"
            >
              <ChevronLeft className="h-4 w-4" />
            </button>
            <span className="text-sm font-medium tabular-nums text-slate-800">{viewYear}</span>
            <button
              type="button"
              aria-label="Keyingi yil"
              onClick={() => setViewYear((y) => y + 1)}
              className="rounded-lg p-1.5 text-brand-600 transition-colors hover:bg-brand-50"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>

          <div className="grid grid-cols-3 gap-1">
            {MONTHS_LONG.map((name, i) => {
              const iso = `${viewYear}-${String(i + 1).padStart(2, '0')}`
              const unavailable = isDisabledMonth(iso)
              const selected = iso === value
              return (
                <button
                  key={iso}
                  type="button"
                  disabled={unavailable}
                  onClick={() => pick(i)}
                  className={cn(
                    'rounded-xl px-2 py-2 text-sm transition-colors',
                    unavailable && 'cursor-not-allowed text-slate-300',
                    !unavailable && !selected && 'text-slate-700 hover:bg-slate-100',
                    selected && 'bg-brand-600 font-semibold text-white hover:bg-brand-600',
                  )}
                >
                  {name.slice(0, 3)}
                </button>
              )
            })}
          </div>
        </div>,
        document.body,
      )
    : null

  if (!label) {
    return (
      <>
        {field}
        {panel}
      </>
    )
  }

  return (
    <div className={cn('block', hasOwnWidth && 'w-full')}>
      <span className="mb-1 block text-sm font-medium text-slate-600">
        {label}
        {required && <span className="text-red-500"> *</span>}
      </span>
      {field}
      {panel}
    </div>
  )
}

/** Sarlavhadagi oy/yil ro'yxati — matn + chevron, ramkasiz (namunadagidek). */
function HeaderSelect({
  value,
  onChange,
  options,
}: {
  value: string
  onChange: (value: string) => void
  options: { value: string; label: string }[]
}) {
  return (
    <span className="relative inline-flex items-center">
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="cursor-pointer appearance-none rounded-lg bg-transparent py-1 pl-2 pr-6 text-sm font-medium text-slate-800 outline-none transition-colors hover:bg-slate-100 focus:bg-slate-100"
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
      <ChevronDown className="pointer-events-none absolute right-1.5 h-3.5 w-3.5 text-brand-600" />
    </span>
  )
}

/** Kichik kalendar belgisi — maydonning o'ng chekkasida. */
function CalendarGlyph({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className={className}>
      <rect x="3" y="5" width="18" height="16" rx="3" />
      <path d="M3 10h18M8 3v4M16 3v4" strokeLinecap="round" />
    </svg>
  )
}
