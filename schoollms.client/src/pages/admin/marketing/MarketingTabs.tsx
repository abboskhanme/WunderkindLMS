import { NavLink } from 'react-router-dom'
import { cn } from '@/lib/utils'

const tabs = [
  { to: '/admin/marketing/arizalar', label: 'Arizalar' },
  { to: '/admin/marketing/topshirilganlar', label: 'Topshirilgan arizalar' },
  { to: '/admin/marketing/yangiliklar', label: 'Yangiliklar' },
] as const

/**
 * Section strip shared by the three `Sotuv va marketing` screens.
 *
 * The sidebar carries a single `Sotuv va marketing` row, as EduSchool's
 * Sozlamalar flyout does (sales-marketing.md §6.4) — so the other two screens
 * are reached from here. Same pill styling as the `Umumiy sozlamalar` hub.
 */
export function MarketingTabs() {
  return (
    <nav className="flex gap-1 overflow-x-auto pb-1">
      {tabs.map((t) => (
        <NavLink
          key={t.to}
          to={t.to}
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
  )
}
