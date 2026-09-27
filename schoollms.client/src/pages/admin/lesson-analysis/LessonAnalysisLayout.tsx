import { NavLink, Outlet } from 'react-router-dom'
import { cn } from '@/lib/utils'
import { LA_BASE } from './paths'

const tabs = [
  { to: LA_BASE, label: 'Umumiy', end: true },
  { to: `${LA_BASE}/tahlillar`, label: 'Tahlillar', end: false },
  { to: `${LA_BASE}/reyting`, label: 'Reyting', end: false },
  { to: `${LA_BASE}/mezonlar`, label: 'Mezonlar', end: false },
] as const

/**
 * Boshqaruv → Dars tahlili (docs/modules/lesson-analysis.md §7): bitta menyu yozuvi,
 * ichida to'rt tab. Tab uslubi `MarketingTabs` bilan bir xil.
 */
export function LessonAnalysisLayout() {
  return (
    <div className="min-w-0 space-y-5">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Dars tahlili</h1>
        <p className="text-sm text-slate-400">Dars kuzatuvi, mezonlar bo‘yicha baholash va o‘qituvchilar reytingi</p>
      </div>
      <nav className="-mx-1 flex gap-1 overflow-x-auto px-1 pb-1">
        {tabs.map((t) => (
          <NavLink
            key={t.to}
            to={t.to}
            end={t.end}
            className={({ isActive }) =>
              cn(
                'shrink-0 whitespace-nowrap rounded-xl px-3 py-2.5 text-sm font-medium transition-colors',
                isActive ? 'bg-brand-50 text-brand-700' : 'text-slate-500 hover:bg-slate-50 hover:text-slate-800',
              )
            }
          >
            {t.label}
          </NavLink>
        ))}
      </nav>
      <Outlet />
    </div>
  )
}
