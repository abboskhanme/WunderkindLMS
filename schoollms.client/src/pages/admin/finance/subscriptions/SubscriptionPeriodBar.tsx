import { DatePicker } from '@/components/ui/DatePicker'
import { cn } from '@/lib/utils'
import { SUBSCRIPTION_PRESETS, type useSubscriptionPeriod } from './subscriptionPeriod'

/**
 * "Bu oy · O'tgan oy · O'quv yili" va sana oralig'i. Sana maydonlari doim faol davrni ko'rsatadi;
 * ulardan birini o'zgartirish davrni "qo'lda" holatiga o'tkazadi.
 */
export function SubscriptionPeriodBar({ state }: { state: ReturnType<typeof useSubscriptionPeriod> }) {
  const { period, setPreset, setRange, fromBounds, toBounds } = state

  return (
    <div className="flex flex-wrap items-center gap-2">
      <div role="group" aria-label="Davr" className="flex rounded-xl bg-slate-100 p-1">
        {SUBSCRIPTION_PRESETS.map((p) => {
          const active = period.kind === p.key
          return (
            <button
              key={p.key}
              type="button"
              aria-pressed={active}
              onClick={() => setPreset(p.key)}
              className={cn(
                'rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
                active ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              {p.label}
            </button>
          )
        })}
      </div>

      <div className="flex items-center gap-1.5">
        <DatePicker
          value={period.from}
          onChange={(v) => setRange(v, period.to)}
          min={fromBounds.min}
          max={fromBounds.max}
          ariaLabel="Davr boshi"
          className="w-36"
        />
        <span className="text-slate-400">—</span>
        <DatePicker
          value={period.to}
          onChange={(v) => setRange(period.from, v)}
          min={toBounds.min}
          max={toBounds.max}
          ariaLabel="Davr oxiri"
          className="w-36"
        />
      </div>
    </div>
  )
}
