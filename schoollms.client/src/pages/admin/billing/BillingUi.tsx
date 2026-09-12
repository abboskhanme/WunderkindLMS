/**
 * Moliya ma'lumotnomasi sahifalarining umumiy bo'laklari (P1-17).
 *
 * To'rtta sahifa bir xil to'rt holatni ko'rsatadi: yuklanmoqda, xato, bo'sh,
 * ma'lumot. Ular shu yerda BIR MARTA yozilgan — har sahifada qayta yozilsa,
 * ertami-kechmi bittasida "xato" holati unutiladi va sahifa jimgina bo'sh
 * ko'rinadi.
 *
 * Bu yerda YANGI dizayn tizimi qurilmaydi: `Card`, `Button`, `Loader`
 * `components/ui/` dan olinadi, bu fayl faqat ularni birlashtiradi.
 */
import type { ReactNode } from 'react'
import { AlertTriangle, Lock, PlugZap, RefreshCw } from 'lucide-react'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { useBillingAccess } from './access'

/* ------------------------------------------------------------------ */
/*  Rol darvozasi                                                      */
/* ------------------------------------------------------------------ */

/**
 * Sahifaning O'Z rol tekshiruvi (P1-17 qabul mezoni).
 *
 * Marshrut darajasidagi himoya P1-20 da qo'shiladi; bu esa undan mustaqil
 * ikkinchi qavat. Kassir bu bo'limga umuman kirmaydi — SPEC §4.3 da unga
 * "Change monthly fee" ham, "Grant a discount" ham, "Record an expense" ham
 * berilmagan, ya'ni o'qish uchun ham asos yo'q.
 */
export function BillingGuard({ children }: { children: ReactNode }) {
  const { canOpen, isCashier } = useBillingAccess()

  if (canOpen) return <>{children}</>

  return (
    <Card className="mx-auto max-w-lg text-center">
      <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-xl bg-slate-100 text-slate-400">
        <Lock className="h-6 w-6" />
      </div>
      <h2 className="text-base font-semibold text-slate-800">Bu bo'lim sizga yopiq</h2>
      <p className="mt-2 text-sm text-slate-500">
        {isCashier
          ? "Kassir moliya ma'lumotnomasini ko'ra olmaydi: toifa, obuna, chegirma va chiqim — administrator va direktor ishi."
          : "Moliya ma'lumotnomasi faqat administrator va direktorga ochiq."}
      </p>
    </Card>
  )
}

/* ------------------------------------------------------------------ */
/*  To'rt holat                                                        */
/* ------------------------------------------------------------------ */

interface AsyncBlockProps {
  loading: boolean
  error: string | null
  /** true = so'rov muvaffaqiyatli, lekin natija bo'sh. */
  empty: boolean
  emptyText: string
  /** Bo'sh holatda ko'rsatiladigan qo'shimcha izoh yoki tugma. */
  emptyAction?: ReactNode
  loadingText?: string
  onRetry?: () => void
  /** true = xato sababi "endpoint hali ulanmagan" (404), server nosozligi emas. */
  notWired?: boolean
  /**
   * true = yuklanish/xato/bo'sh holatlari `Card` ichida chiziladi. Ma'lumot
   * o'zi kartochkalardan iborat bo'lgan sahifalar uchun (obunalar), aks holda
   * bu uch holat sahifa fonida osilib qoladi.
   */
  framed?: boolean
  children: ReactNode
}

/**
 * Yuklanmoqda / xato / bo'sh / ma'lumot — to'rttasi ham majburiy.
 * Hech biri "keyinroq qo'shamiz" emas: sekin tarmoq, bo'sh natija va
 * yiqilgan so'rov — foydalanuvchi eng ko'p uchratadigan holatlar.
 */
export function AsyncBlock({
  loading,
  error,
  empty,
  emptyText,
  emptyAction,
  loadingText = 'Yuklanmoqda...',
  onRetry,
  notWired,
  framed = false,
  children,
}: AsyncBlockProps) {
  const frame = (node: ReactNode) => (framed ? <Card className="p-0">{node}</Card> : <>{node}</>)

  if (loading) return frame(<Loader label={loadingText} />)

  if (error) {
    return frame(
      <div className="flex flex-col items-center gap-3 px-4 py-12 text-center">
        <div
          className={cn(
            'flex h-12 w-12 items-center justify-center rounded-xl',
            notWired ? 'bg-amber-50 text-amber-600' : 'bg-red-50 text-red-600',
          )}
        >
          {notWired ? <PlugZap className="h-6 w-6" /> : <AlertTriangle className="h-6 w-6" />}
        </div>
        <div>
          <p className="font-medium text-slate-800">
            {notWired ? 'Bu bo\'lim hali serverga ulanmagan' : "Ma'lumotni yuklab bo'lmadi"}
          </p>
          <p className="mt-1 max-w-md text-sm text-slate-500">{error}</p>
        </div>
        {onRetry && (
          <Button variant="secondary" onClick={onRetry}>
            <RefreshCw className="h-4 w-4" /> Qayta urinish
          </Button>
        )}
      </div>,
    )
  }

  if (empty) {
    return frame(
      <div className="flex flex-col items-center gap-3 px-4 py-12 text-center">
        <p className="text-sm text-slate-400">{emptyText}</p>
        {emptyAction}
      </div>,
    )
  }

  return <>{children}</>
}

/* ------------------------------------------------------------------ */
/*  Kichik bo'laklar                                                   */
/* ------------------------------------------------------------------ */

type PillTone = 'neutral' | 'success' | 'warning' | 'danger' | 'info'

const pillTones: Record<PillTone, string> = {
  neutral: 'bg-slate-100 text-slate-600',
  success: 'bg-emerald-50 text-emerald-700',
  warning: 'bg-amber-50 text-amber-700',
  danger: 'bg-red-50 text-red-700',
  info: 'bg-brand-50 text-brand-700',
}

export function StatusPill({
  tone = 'neutral',
  children,
}: {
  tone?: PillTone
  children: ReactNode
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium',
        pillTones[tone],
      )}
    >
      {children}
    </span>
  )
}

/**
 * Tasdiq navbati uchun ko'zga tashlanadigan quti (P1-17 qabul mezoni:
 * "kutayotganlar alohida, ko'zga tashlanadigan ro'yxatda").
 */
export function PendingQueue({
  title,
  hint,
  count,
  children,
}: {
  title: string
  hint: string
  count: number
  children: ReactNode
}) {
  return (
    <section className="overflow-hidden rounded-2xl border-2 border-amber-300 bg-amber-50/60 shadow-sm">
      <header className="flex flex-wrap items-center justify-between gap-2 border-b border-amber-200 px-5 py-3">
        <div className="flex items-center gap-2">
          <AlertTriangle className="h-5 w-5 text-amber-600" />
          <h2 className="font-semibold text-amber-900">{title}</h2>
          <span className="rounded-full bg-amber-600 px-2 py-0.5 text-xs font-semibold text-white">
            {count}
          </span>
        </div>
        <p className="text-xs text-amber-800">{hint}</p>
      </header>
      <div className="bg-white/70">{children}</div>
    </section>
  )
}

/** Amal bajarilgandan keyingi xato/xabar chizig'i (modal ichida ham ishlatiladi). */
export function Notice({
  tone = 'danger',
  children,
}: {
  tone?: 'danger' | 'info' | 'success'
  children: ReactNode
}) {
  const tones = {
    danger: 'border-red-200 bg-red-50 text-red-700',
    info: 'border-brand-200 bg-brand-50 text-brand-700',
    success: 'border-emerald-200 bg-emerald-50 text-emerald-700',
  }
  return (
    <p className={cn('rounded-lg border px-3 py-2 text-sm', tones[tone])}>{children}</p>
  )
}

/** Yumaloq ikon tugmasi — `SubjectsPage` dagi bilan bir xil ko'rinish. */
export function IconBtn({
  icon: Icon,
  title,
  onClick,
  tone = 'neutral',
  disabled,
}: {
  icon: typeof AlertTriangle
  title: string
  onClick: () => void
  tone?: 'neutral' | 'danger'
  disabled?: boolean
}) {
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      disabled={disabled}
      onClick={onClick}
      className={cn(
        'rounded-lg p-1.5 transition-colors disabled:cursor-not-allowed disabled:opacity-40',
        tone === 'danger'
          ? 'text-slate-400 hover:bg-red-50 hover:text-red-600'
          : 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
      )}
    >
      <Icon className="h-4 w-4" />
    </button>
  )
}
