/**
 * "Vidjetlar" — bosh sahifada qaysi kartalar ko'rinishini tanlash paneli.
 *
 * O'ng tomondan chiqadi, dashboard orqada ko'rinib turadi: kalitni bosish
 * bilan karta darhol paydo bo'ladi yoki yo'qoladi, "Saqlash" tugmasi yo'q.
 * Guruh bo'yicha "Hammasini yoqish" va "Standart holatga qaytarish" —
 * EduSchool'dagi funksiya, ko'rinishi esa bizniki.
 */
import { useEffect, useState } from 'react'
import { RotateCcw, Search, X } from 'lucide-react'
import { cn } from '@/lib/utils'
import { WIDGET_GROUPS, type DashboardWidgets } from './dashboardWidgets'

interface Props {
  widgets: DashboardWidgets
  onClose: () => void
}

export function WidgetPickerDrawer({ widgets, onClose }: Props) {
  const [q, setQ] = useState('')

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const needle = q.trim().toLocaleLowerCase('uz')
  const matches = (label: string) => !needle || label.toLocaleLowerCase('uz').includes(needle)

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      <div className="absolute inset-0 bg-slate-900/20" onClick={onClose} />

      <aside
        role="dialog"
        aria-modal="true"
        aria-label="Vidjetlar"
        className="relative z-10 flex h-full w-full max-w-sm flex-col border-l border-slate-200 bg-white shadow-xl"
      >
        <header className="border-b border-slate-100 px-5 pb-3 pt-4">
          <div className="flex items-start justify-between gap-3">
            <div>
              <h2 className="font-semibold text-slate-800">Vidjetlar</h2>
              <p className="text-xs text-slate-400">
                {widgets.visible.length} tadan {widgets.shownCount} tasi ko'rsatilgan
              </p>
            </div>
            <button
              type="button"
              onClick={onClose}
              aria-label="Yopish"
              className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-600"
            >
              <X className="h-5 w-5" />
            </button>
          </div>
          <label className="mt-3 flex items-center gap-2 rounded-xl bg-slate-100 px-3 py-2">
            <Search className="h-4 w-4 shrink-0 text-slate-400" />
            <input
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder="Qidirish"
              className="w-full bg-transparent text-sm text-slate-700 outline-none placeholder:text-slate-400"
            />
          </label>
        </header>

        <div className="flex-1 overflow-y-auto px-5 py-2">
          {WIDGET_GROUPS.map((g) => {
            const all = widgets.visible.filter((w) => w.group === g.key)
            const shown = all.filter((w) => matches(w.label))
            if (shown.length === 0) return null
            const usable = all.filter((w) => !w.unavailable)
            const onCount = usable.filter((w) => widgets.isOn(w.id)).length
            const allOn = onCount === usable.length
            return (
              <section key={g.key} className="py-2">
                <div className="flex items-center justify-between gap-2 py-1.5">
                  <h3 className="text-[11px] font-semibold uppercase tracking-wide text-slate-400">
                    {g.label} · {onCount}/{usable.length}
                  </h3>
                  {usable.length > 0 && (
                  <button
                    type="button"
                    onClick={() => widgets.set(usable.map((w) => w.id), !allOn)}
                    className="rounded-md px-2 py-0.5 text-xs font-medium text-brand-600 hover:bg-brand-50"
                  >
                    {allOn ? "Hammasini o'chirish" : 'Hammasini yoqish'}
                  </button>
                  )}
                </div>
                <ul>
                  {shown.map((w) => {
                    const on = widgets.isOn(w.id)
                    const off = Boolean(w.unavailable)
                    return (
                      <li key={w.id}>
                        <button
                          type="button"
                          role="switch"
                          aria-checked={on}
                          disabled={off}
                          title={w.unavailable}
                          onClick={() => widgets.set([w.id], !on)}
                          className={cn(
                            'flex w-full items-center justify-between gap-3 rounded-lg px-1 py-2 text-left text-sm',
                            off ? 'cursor-not-allowed text-slate-400' : 'text-slate-700 hover:bg-slate-50',
                          )}
                        >
                          <span className="min-w-0">
                            <span className="block truncate">{w.label}</span>
                            {off && <span className="block truncate text-xs">{w.unavailable}</span>}
                          </span>
                          <Switch on={on} disabled={off} />
                        </button>
                      </li>
                    )
                  })}
                </ul>
              </section>
            )
          })}
          {widgets.visible.every((w) => !matches(w.label)) && (
            <p className="py-8 text-center text-sm text-slate-400">Hech narsa topilmadi.</p>
          )}
        </div>

        <footer className="border-t border-slate-100 px-5 py-3">
          <button
            type="button"
            onClick={widgets.reset}
            className="flex w-full items-center justify-center gap-2 rounded-xl border border-slate-200 px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50"
          >
            <RotateCcw className="h-4 w-4" />
            Standart holatga qaytarish
          </button>
        </footer>
      </aside>
    </div>
  )
}

/** iOS uslubidagi kalit — faqat ko'rinish; bosish ota tugmada. */
function Switch({ on, disabled }: { on: boolean; disabled?: boolean }) {
  return (
    <span
      aria-hidden
      className={cn(
        'relative inline-flex h-6 w-10 shrink-0 rounded-full transition-colors',
        on ? 'bg-emerald-500' : 'bg-slate-200',
        disabled && 'opacity-50',
      )}
    >
      <span
        className={cn(
          'absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition-transform',
          on ? 'translate-x-[18px]' : 'translate-x-0.5',
        )}
      />
    </span>
  )
}
