/**
 * Mini App dizayn tizimi — mijoz bergan namunadagi shakl.
 *
 * Namunaning uch qatlami bor va butun ilova shunga tayanadi:
 *   1) tepada SARIQ hero kartochka (pastki burchaklari yumaloq),
 *   2) ostida KULRANG fonda OQ kartochkalar,
 *   3) pastda OQ tab paneli, faol element — sariq tabletka.
 *
 * Ranglar `tailwind.config.js` da: `brand` (#FFD006), `brand.dark` (#E8BD02),
 * `brand.ink` (#0E1520), `ground` (#F0F0F0).
 */
import { ChevronLeft, ChevronRight } from 'lucide-react'

/* ------------------------------------------------------------------ karkas */

/** Sahifa: kulrang fon + pastdagi tab paneli uchun joy. */
export function Screen({ children }) {
  return (
    <div className="min-h-full bg-ground pb-[calc(76px+var(--tg-safe-bottom))]">{children}</div>
  )
}

/**
 * Sariq sarlavha bloki. Namunadagidek: pastki burchaklari yumaloq, ichida
 * logo, nom va (ixtiyoriy) qo'shimcha qatorlar.
 */
export function Hero({ title, subtitle, right, children }) {
  return (
    <header className="rounded-b-[26px] bg-brand px-4 pb-5 pt-4 text-brand-ink">
      <div className="flex items-center gap-3">
        <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-[14px] bg-brand-ink text-lg font-bold text-brand">
          W
        </div>
        <div className="min-w-0 flex-1">
          <p className="truncate text-[19px] font-bold leading-tight">{title}</p>
          {subtitle && <p className="truncate text-[13px] opacity-70">{subtitle}</p>}
        </div>
        {right}
      </div>
      {children && <div className="mt-4">{children}</div>}
    </header>
  )
}

/** Sariq fonda ikki tugmali o'tkagich — namunadagi "Markaz / Maktab". */
export function Segmented({ value, options, onChange }) {
  return (
    <div className="flex rounded-2xl bg-brand-dark/60 p-1">
      {options.map((o) => {
        const active = o.value === value
        return (
          <button
            key={o.value}
            type="button"
            onClick={() => onChange(o.value)}
            className={
              'flex-1 rounded-xl px-3 py-2.5 text-[15px] font-semibold transition ' +
              (active ? 'bg-brand-ink text-brand' : 'text-brand-ink/70')
            }
          >
            {o.label}
          </button>
        )
      })}
    </div>
  )
}

/** Chap/o'ng strelkali davr o'tkagichi. */
export function Stepper({ label, onPrev, onNext, disabledNext }) {
  return (
    <div className="flex items-center justify-between">
      <button type="button" onClick={onPrev} aria-label="Oldingi" className="p-2">
        <ChevronLeft className="h-6 w-6" />
      </button>
      <p className="text-[17px] font-bold">{label}</p>
      <button
        type="button"
        onClick={onNext}
        disabled={disabledNext}
        aria-label="Keyingi"
        className="p-2 disabled:opacity-30"
      >
        <ChevronRight className="h-6 w-6" />
      </button>
    </div>
  )
}

/** Sariq fondagi progress chizig'i. */
export function HeroProgress({ value, max, lead, note }) {
  const pct = max > 0 ? Math.min(100, Math.max(0, (value / max) * 100)) : 0
  return (
    <div>
      <div className="flex items-baseline gap-2">
        <span className="text-[34px] font-extrabold leading-none">{lead}</span>
        {note && <span className="text-[14px] opacity-70">{note}</span>}
      </div>
      <div className="mt-3 h-2.5 overflow-hidden rounded-full bg-brand-dark">
        <div className="h-full rounded-full bg-brand-ink/85" style={{ width: `${pct}%` }} />
      </div>
    </div>
  )
}

/* --------------------------------------------------------------- kartochka */

export function Card({ title, action, children, className = '' }) {
  return (
    <section className={'mx-4 mt-3 overflow-hidden rounded-card bg-white ' + className}>
      {(title || action) && (
        <div className="flex items-center justify-between gap-3 px-4 pb-1 pt-4">
          {title && <h2 className="text-[17px] font-bold">{title}</h2>}
          {action}
        </div>
      )}
      {children}
    </section>
  )
}

/**
 * Namunadagi 2×2 raqamlar to'ri: bitta oq kartochka, ichida ingichka
 * ajratgichlar bilan bo'lingan kataklar.
 */
export function StatGrid({ items }) {
  return (
    <section className="mx-4 mt-3 grid grid-cols-2 overflow-hidden rounded-card bg-white">
      {items.map((it, i) => (
        <div
          key={it.label}
          className={
            'p-4 ' +
            (i % 2 === 0 ? 'border-r border-slate-100 ' : '') +
            (i >= 2 ? 'border-t border-slate-100' : '')
          }
        >
          <p className="text-[26px] font-extrabold leading-none">{it.value}</p>
          <p className="mt-1.5 text-[13px] text-slate-500">{it.label}</p>
          {it.note && <p className="text-[12px] text-slate-400">{it.note}</p>}
        </div>
      ))}
    </section>
  )
}

/** Ro'yxat qatori: chapda kun/belgi, o'rtada matn, o'ngda holat. */
export function Row({ lead, title, subtitle, right, onClick }) {
  const Tag = onClick ? 'button' : 'div'
  return (
    <Tag
      type={onClick ? 'button' : undefined}
      onClick={onClick}
      className="flex w-full items-center gap-3 border-t border-slate-100 px-4 py-3 text-left first:border-t-0"
    >
      {lead}
      <div className="min-w-0 flex-1">
        <p className="text-[15px] font-semibold leading-snug">{title}</p>
        {subtitle && <p className="mt-0.5 text-[13px] text-slate-500">{subtitle}</p>}
      </div>
      {right}
    </Tag>
  )
}

/** Sana kvadrati — namunadagi qizil "1 Se" kabi. */
export function DateChip({ day, month, tone = 'neutral' }) {
  const tones = {
    neutral: 'bg-slate-100 text-slate-600',
    danger: 'bg-red-50 text-red-600',
    brand: 'bg-brand/25 text-brand-ink',
  }
  return (
    <div className={'flex h-11 w-11 shrink-0 flex-col items-center justify-center rounded-xl ' + tones[tone]}>
      <span className="text-[17px] font-bold leading-none">{day}</span>
      <span className="text-[11px] leading-tight opacity-70">{month}</span>
    </div>
  )
}

export function Badge({ children, tone = 'neutral' }) {
  const tones = {
    neutral: 'bg-slate-100 text-slate-600',
    danger: 'bg-red-50 text-red-600',
    success: 'bg-emerald-50 text-emerald-700',
    brand: 'bg-brand/30 text-brand-ink',
  }
  return (
    <span className={'shrink-0 rounded-full px-2.5 py-1 text-[12px] font-medium ' + tones[tone]}>
      {children}
    </span>
  )
}

/* -------------------------------------------------------------- holatlar */

export function Loader({ label = 'Yuklanmoqda…' }) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 py-16 text-slate-400">
      <div className="h-8 w-8 animate-spin rounded-full border-[3px] border-slate-200 border-t-brand" />
      <p className="text-[14px]">{label}</p>
    </div>
  )
}

export function EmptyState({ icon, title, note }) {
  return (
    <div className="flex flex-col items-center gap-2 px-8 py-12 text-center">
      {icon && <div className="text-slate-300">{icon}</div>}
      <p className="text-[15px] font-semibold text-slate-600">{title}</p>
      {note && <p className="text-[13px] text-slate-400">{note}</p>}
    </div>
  )
}

export function ErrorState({ message, onRetry }) {
  return (
    <div className="flex flex-col items-center gap-3 px-8 py-12 text-center">
      <p className="text-[15px] font-semibold text-slate-700">Ma'lumot olinmadi</p>
      <p className="text-[13px] text-slate-500">{message}</p>
      {onRetry && (
        <button
          type="button"
          onClick={onRetry}
          className="mt-1 rounded-xl bg-brand px-4 py-2 text-[14px] font-semibold text-brand-ink"
        >
          Qayta urinish
        </button>
      )}
    </div>
  )
}

/* ------------------------------------------------------------ tab paneli */

/** Pastdagi panel. Faol element — namunadagidek sariq tabletka. */
export function TabBar({ tabs, value, onChange }) {
  return (
    <nav className="fixed inset-x-0 bottom-0 z-20 border-t border-slate-200 bg-white pb-[var(--tg-safe-bottom)]">
      <div className="mx-auto flex max-w-md items-stretch justify-around px-2 py-2">
        {tabs.map((t) => {
          const active = t.value === value
          const Icon = t.icon
          return (
            <button
              key={t.value}
              type="button"
              onClick={() => onChange(t.value)}
              className="flex min-w-0 flex-1 flex-col items-center gap-1 py-1"
            >
              <span
                className={
                  'flex h-9 w-14 items-center justify-center rounded-full transition ' +
                  (active ? 'bg-brand text-brand-ink' : 'text-slate-400')
                }
              >
                <Icon className="h-[21px] w-[21px]" />
              </span>
              <span
                className={
                  'truncate text-[11px] ' +
                  (active ? 'font-semibold text-brand-ink' : 'text-slate-400')
                }
              >
                {t.label}
              </span>
            </button>
          )
        })}
      </div>
    </nav>
  )
}
