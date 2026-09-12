/**
 * HAL QILINMAGAN NOMUVOFIQLIK HISOBLAGICHI (SPEC §4.6) — P1-18.
 *
 * ═══════════════════════════════════════════════════════════════════════
 *  BU BANNERDA "x" TUGMASI YO'Q VA HECH QACHON BO'LMAYDI.
 * ═══════════════════════════════════════════════════════════════════════
 *
 * SPEC §4.6: "The director's dashboard shows an unresolved-variance counter
 * that cannot be dismissed, only resolved with a written reason."
 *
 * Shuning uchun bu komponentda:
 *   · `onClose` / `onDismiss` propi YO'Q,
 *   · yopilgan holatni eslab qoladigan `useState` yoki `localStorage` YO'Q,
 *   · `hidden` qiladigan shart YO'Q.
 *
 * Hisoblagich sahifa ochilishi bilan ko'rinadi va faqat SERVERDAGI raqam
 * nolga tushganda so'nadi. Nolda ham u yo'qolmaydi — "hammasi joyida"
 * degan xotirjam qator qoladi: bannerning yo'qligi "tekshirilmadi" degan
 * ma'noni ham berishi mumkin, borligi esa aniq javob.
 *
 * Yuklanish va xato holatlari ham SHU YERDA ko'rinadi. Nomuvofiqlik
 * hisoblagichini "jimgina" yiqitib qo'yish — eng yomon variant: direktor
 * hech narsa ko'rmaydi va hammasi joyida deb o'ylaydi.
 */
import { AlertTriangle, CheckCircle2, Loader2, RefreshCw, ShieldAlert } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { formatMoney } from '@/lib/utils'
import type { VarianceWatch } from './useVarianceWatch'

interface Props {
  watch: VarianceWatch
  /** "Ko'rish" — nomuvofiqlik tabiga o'tish. */
  onOpen: () => void
  /** Nomuvofiqlik tabi allaqachon ochiq bo'lsa — "Ko'rish" tugmasi keraksiz. */
  hideOpenAction?: boolean
}

export function VarianceBanner({ watch, onOpen, hideOpenAction = false }: Props) {
  if (watch.loading) {
    return (
      <div className="flex items-center gap-3 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-500">
        <Loader2 className="h-4 w-4 shrink-0 animate-spin" />
        Kassa nomuvofiqliklari tekshirilmoqda...
      </div>
    )
  }

  if (watch.error) {
    return (
      <div className="flex flex-wrap items-center gap-3 rounded-2xl border border-amber-300 bg-amber-50 px-4 py-3">
        <ShieldAlert className="h-5 w-5 shrink-0 text-amber-600" />
        <div className="min-w-0 flex-1">
          <p className="text-sm font-semibold text-amber-900">
            Nomuvofiqlik hisoblagichini yuklab bo'lmadi
          </p>
          <p className="text-sm text-amber-800/80">{watch.error}</p>
        </div>
        <Button variant="secondary" onClick={watch.refetch}>
          <RefreshCw className="h-4 w-4" /> Qayta urinish
        </Button>
      </div>
    )
  }

  if (watch.count === 0) {
    return (
      <div className="flex flex-wrap items-center gap-3 rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3">
        <CheckCircle2 className="h-5 w-5 shrink-0 text-emerald-600" />
        <p className="min-w-0 flex-1 text-sm text-emerald-900">
          <span className="font-semibold">Hal qilinmagan nomuvofiqlik yo'q.</span>{' '}
          {watch.flagsNote ?? "Yopilgan smenalarning barchasi kutilgan naqd bilan mos."}
        </p>
      </div>
    )
  }

  const totalVariance = watch.shifts.reduce((sum, s) => sum + (s.variance ?? 0), 0)

  return (
    <div className="flex flex-wrap items-center gap-3 rounded-2xl border-2 border-red-300 bg-red-50 px-4 py-3">
      <AlertTriangle className="h-6 w-6 shrink-0 text-red-600" />
      <div className="min-w-0 flex-1">
        <p className="text-sm font-semibold text-red-900">
          {watch.count} ta hal qilinmagan nomuvofiqlik
          {watch.shifts.length > 0 && (
            <span className="font-normal"> · jami farq {formatMoney(totalVariance)}</span>
          )}
        </p>
        <p className="text-sm text-red-800/80">
          Bu ogohlantirishni yopib bo'lmaydi — u faqat sabab yozib hal qilinadi (SPEC §4.6).
          {watch.flagsNote ? ` ${watch.flagsNote}` : ''}
        </p>
      </div>
      {!hideOpenAction && (
        <Button variant="danger" onClick={onOpen}>
          Ko'rish
        </Button>
      )}
    </div>
  )
}
