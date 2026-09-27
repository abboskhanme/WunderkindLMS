/**
 * HR topshiriqlari ekranlari uchun umumiy mayda qismlar.
 *
 * Bu YANGI dizayn tizimi EMAS — `components/ui.jsx` ning ranglari va shakllari
 * (sariq brend, oq kartochka, `rounded-card`) ustida faqat eski xodim ilovasida
 * bo'lgan, lekin bizda hali yo'q bo'lgan to'rtta narsa: pastdan chiqadigan oyna,
 * ekran ustidagi xabar, oq fondagi filtr va progress chizig'i.
 */
import { useEffect, useState } from 'react'
import { CircleAlert, CircleCheck, Loader2, WifiOff, X } from 'lucide-react'
import { hapticSelect, showBackButton, useKeyboardOpen } from '../../lib/telegram'
import { t } from './texts'

/* ------------------------------------------------------------------ tugma */

const BTN_VARIANT = {
  primary: 'bg-brand text-brand-ink',
  dark: 'bg-brand-ink text-white',
  danger: 'bg-red-50 text-red-600',
  quiet: 'bg-slate-100 text-slate-700',
  hero: 'bg-brand-ink text-brand',
  light: 'bg-white text-brand-ink',
}

const BTN_SIZE = {
  md: 'rounded-2xl px-4 py-3 text-[15px]',
  sm: 'rounded-xl px-3 py-2.5 text-[14px]',
}

export function Btn({
  children, onClick, variant = 'quiet', size = 'md', block = false, grow = false,
  loading = false, disabled = false, className = '', ...rest
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled || loading}
      className={
        'inline-flex min-w-0 items-center justify-center gap-1.5 font-bold transition active:opacity-80 disabled:opacity-50 ' +
        BTN_VARIANT[variant] + ' ' + BTN_SIZE[size] + ' ' +
        (block ? 'w-full ' : '') + (grow ? 'flex-auto ' : '') + className
      }
      {...rest}
    >
      {loading && <Loader2 className="h-4 w-4 shrink-0 animate-spin" />}
      <span className="truncate">{children}</span>
    </button>
  )
}

/** Matn maydoni. 16 px: kichikroq shriftda iOS fokusda sahifani kattalashtiradi. */
export const INPUT_CLASS =
  'block w-full min-w-0 rounded-xl bg-slate-100 px-3.5 py-2.5 text-[16px] text-brand-ink outline-none ' +
  'placeholder:text-slate-400 focus:ring-2 focus:ring-brand disabled:opacity-60'

/* ---------------------------------------------------------------- xabar */

const NOTICE_TONE = {
  danger: { box: 'bg-red-50 text-red-700', Icon: CircleAlert },
  ok: { box: 'bg-emerald-50 text-emerald-700', Icon: CircleCheck },
  warn: { box: 'bg-amber-50 text-amber-800', Icon: WifiOff },
}

/** Ekran yoki oyna ustidagi qisqa xabar. Har ohangning o'z ikonkasi bor (faqat rang emas). */
export function Notice({ tone = 'danger', children, onDismiss, className = 'mx-4 mt-3' }) {
  const { box, Icon } = NOTICE_TONE[tone]
  return (
    <div role="status" className={'flex items-start gap-2 rounded-2xl px-3.5 py-3 text-[14px] leading-snug ' + box + ' ' + className}>
      <Icon className="mt-px h-[18px] w-[18px] shrink-0" />
      <p className="min-w-0 flex-1 break-words">{children}</p>
      {onDismiss && (
        <button type="button" onClick={onDismiss} aria-label={t('common.close')} className="-m-1 shrink-0 p-1 opacity-70">
          <X className="h-4 w-4" />
        </button>
      )}
    </div>
  )
}

/* ------------------------------------------------------ oq fondagi filtr */

/** `components/ui` dagi `Segmented` sariq hero uchun; bu — kulrang fondagi oq varianti. */
export function LightSegmented({ value, options, onChange, ariaLabel }) {
  return (
    <div role="group" aria-label={ariaLabel} className="mx-4 mt-3 flex rounded-2xl bg-white p-1">
      {options.map((o) => {
        const active = o.value === value
        return (
          <button
            key={o.value}
            type="button"
            aria-pressed={active}
            onClick={() => {
              if (active) return
              hapticSelect()
              onChange(o.value)
            }}
            className={
              'min-w-0 flex-1 truncate rounded-xl px-2 py-2 text-[14px] font-semibold transition ' +
              (active ? 'bg-brand text-brand-ink' : 'text-slate-500')
            }
          >
            {o.label}
          </button>
        )
      })}
    </div>
  )
}

/* -------------------------------------------------------- progress chizig'i */

/** KPI chizig'i: `147 / 200 lead` + foiz. Maqsadga yetilganda — yashil. */
export function ProgressBar({ percent, label, reached = false }) {
  const safe = Math.min(100, Math.max(0, Math.round(percent)))
  return (
    <div className="mt-3">
      {label !== undefined && (
        <div className="mb-1.5 flex items-baseline justify-between gap-2 text-[13px]">
          <span className="min-w-0 break-words font-semibold text-brand-ink">{label}</span>
          <span className="shrink-0 font-bold text-slate-500">{safe}%</span>
        </div>
      )}
      <div
        className="h-2 overflow-hidden rounded-full bg-slate-100"
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={safe}
      >
        <div
          className={'h-full rounded-full transition-[width] ' + (reached ? 'bg-emerald-500' : 'bg-brand')}
          style={{ width: `${safe}%` }}
        />
      </div>
    </div>
  )
}

/* ------------------------------------------------------------- skelet */

/**
 * Ro'yxat skeleti — aylanma ko'rsatkich O'RNIGA: kontent kelganda joyi
 * allaqachon band bo'ladi va sahifa sakramaydi (eski ilovadagi qaror).
 */
export function ListSkeleton({ label, rows = 3 }) {
  return (
    <div role="status" aria-busy="true" aria-label={label}>
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} aria-hidden="true" className="mx-4 mt-3 animate-pulse rounded-card bg-white p-4">
          <div className="h-4 w-2/3 rounded-full bg-slate-100" />
          <div className="mt-2.5 h-3 w-1/3 rounded-full bg-slate-100" />
          <div className="mt-3 h-3 w-full rounded-full bg-slate-100" />
          <div className="mt-4 h-10 w-full rounded-2xl bg-slate-100" />
        </div>
      ))}
    </div>
  )
}

/* ------------------------------------------------------------- tarmoq */

export function useOnline() {
  const [online, setOnline] = useState(() => (typeof navigator === 'undefined' ? true : navigator.onLine !== false))
  useEffect(() => {
    const on = () => setOnline(true)
    const off = () => setOnline(false)
    window.addEventListener('online', on)
    window.addEventListener('offline', off)
    return () => {
      window.removeEventListener('online', on)
      window.removeEventListener('offline', off)
    }
  }, [])
  return online
}

/* ------------------------------------------------------ pastdan chiqadigan oyna */

/**
 * Pastdan chiqadigan oyna (eski `ui/Sheet.tsx`).
 *
 * - Deyarli butun balandlik: matn maydonlari bor, klaviatura ochilganda yarim
 *   balandlikdagi oyna sakrab, tugma ekrandan chiqib ketardi.
 * - Xira fonni bosganda YOPILMAYDI: yozilgan izoh va tanlangan fayllar yo'qolardi.
 *   Yopish — "×" yoki Telegramning "orqaga" tugmasi.
 * - `busy` (fayl yuklanmoqda) paytida yopib bo'lmaydi.
 * - Klaviatura ochiq bo'lsa pastki xavfsiz zona qo'shilmaydi — klaviatura uni yopgan.
 */
export function BottomSheet({ title, busy = false, onClose, footer, children }) {
  const keyboardOpen = useKeyboardOpen()

  useEffect(() => {
    if (busy) return undefined
    return showBackButton(onClose)
  }, [busy, onClose])

  // Oyna ochiq ekan orqadagi ro'yxat surilib ketmasin.
  useEffect(() => {
    const previous = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.body.style.overflow = previous
    }
  }, [])

  return (
    <div role="dialog" aria-modal="true" aria-label={title} className="fixed inset-0 z-50 flex items-end bg-black/40">
      <div className="mx-auto flex h-[calc(100%-24px)] w-full max-w-md flex-col overflow-hidden rounded-t-[26px] bg-white text-brand-ink">
        <div className="shrink-0 px-4 pt-2">
          <span aria-hidden="true" className="mx-auto block h-1 w-10 rounded-full bg-slate-200" />
          <div className="mt-2 flex items-center gap-3 pb-2">
            <h2 className="min-w-0 flex-1 truncate text-[18px] font-bold">{title}</h2>
            <button
              type="button"
              onClick={onClose}
              disabled={busy}
              aria-label={t('common.close')}
              className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-slate-100 text-slate-600 disabled:opacity-40"
            >
              <X className="h-[18px] w-[18px]" />
            </button>
          </div>
        </div>

        {/* overscroll-contain — oxirigacha surilganda orqadagi sahifa qimirlamasin. */}
        <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-4 pb-4">{children}</div>

        {footer && (
          <div
            className={
              'shrink-0 border-t border-slate-100 bg-white px-4 pt-3 ' +
              (keyboardOpen ? 'pb-3' : 'pb-[calc(12px+var(--tg-safe-bottom))]')
            }
          >
            {footer}
          </div>
        )}
      </div>
    </div>
  )
}

/** Oyna ichidagi maydon sarlavhasi. */
export function FieldLabel({ htmlFor, children }) {
  const Tag = htmlFor ? 'label' : 'span'
  return (
    <Tag htmlFor={htmlFor} className="mb-1.5 mt-4 block text-[13px] font-semibold text-slate-500">
      {children}
    </Tag>
  )
}

export function Hint({ children, className = '' }) {
  return <p className={'mt-1.5 text-[12px] leading-relaxed text-slate-400 ' + className}>{children}</p>
}
