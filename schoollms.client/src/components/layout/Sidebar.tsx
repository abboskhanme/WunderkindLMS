import { useEffect, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { GraduationCap, ChevronDown } from 'lucide-react'
import type { Role } from '@/types'
import { useAuth } from '@/context/auth-context'
import { useUnread } from '@/context/unread-context'
import { getSchoolName } from '@/api/services/settings'
import { navByRole, roleLabels, homeByRole, type NavItem } from '@/config/navigation'
import { cn } from '@/lib/utils'

interface SidebarProps {
  open: boolean
  onNavigate: () => void
}

export function Sidebar({ open, onNavigate }: SidebarProps) {
  const { user } = useAuth()
  const { unreadChannels } = useUnread()
  const totalUnread = unreadChannels.size
  const [schoolName, setSchoolName] = useState('')

  // Maktab nomini yuklaymiz; sozlamada saqlangach 'school:updated' hodisasi bilan yangilanadi.
  useEffect(() => {
    const load = () => {
      getSchoolName()
        .then(setSchoolName)
        .catch(() => {})
    }
    load()
    window.addEventListener('school:updated', load)
    return () => window.removeEventListener('school:updated', load)
  }, [])

  if (!user) return null
  const role = user.role
  // Element ko'rinadi: roli mos (yoki roles yo'q) VA ruxsati bor (yoki perm yo'q / permissions yo'q).
  const canSee = (x: { roles?: Role[]; perm?: string }) =>
    (!x.roles || x.roles.includes(role)) &&
    (!x.perm || !user.permissions || user.permissions.includes(x.perm))

  // Guruh bolalarini ham filtrlaymiz; barcha bolalari yashirilgan guruhni ko'rsatmaymiz.
  const items = navByRole[role]
    .filter(canSee)
    .map((i) => (i.children ? { ...i, children: i.children.filter(canSee) } : i))
    .filter((i) => !i.children || i.children.length > 0)

  return (
    <aside
      className={cn(
        'fixed inset-y-0 left-0 z-40 flex w-64 shrink-0 flex-col border-r border-slate-200 bg-white transition-transform duration-200',
        open ? 'translate-x-0 lg:static lg:translate-x-0' : '-translate-x-full lg:hidden',
      )}
    >
      {/* Logo */}
      <div className="flex h-16 items-center gap-2.5 border-b border-slate-200 px-6">
        <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-brand-600 text-white">
          <GraduationCap className="h-5 w-5" />
        </div>
        <div className="leading-tight">
          <p className="font-semibold text-slate-800">{schoolName || 'Maktab LMS'}</p>
          <p className="text-xs text-slate-400">{roleLabels[role]}</p>
        </div>
      </div>

      {/* Menyu */}
      <nav className="flex-1 space-y-1 overflow-y-auto p-3">
        {items.map((item) =>
          item.children ? (
            <NavGroup key={item.to} item={item} onNavigate={onNavigate} />
          ) : (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === homeByRole[role]}
              onClick={onNavigate}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition-colors',
                  isActive
                    ? 'bg-brand-50 text-brand-700'
                    : 'text-slate-600 hover:bg-slate-50 hover:text-slate-900',
                )
              }
            >
              <item.icon className="h-5 w-5" />
              {item.label}
              {item.to.endsWith('/messages') && totalUnread > 0 && (
                <span className="ml-auto flex h-5 min-w-[1.25rem] items-center justify-center rounded-full bg-red-500 px-1 text-[10px] font-bold text-white">
                  {totalUnread > 9 ? '9+' : totalUnread}
                </span>
              )}
            </NavLink>
          ),
        )}
      </nav>
    </aside>
  )
}

function NavGroup({ item, onNavigate }: { item: NavItem; onNavigate: () => void }) {
  const location = useLocation()
  // Guruh — o'z manzili ostida YOKI bolalaridan biri faol bo'lsa belgilanadi/ochiladi
  // (bolalar boshqa yo'l ostida bo'lishi mumkin, masalan Fanlar yoki Sozlamalar).
  const isUnder =
    location.pathname.startsWith(item.to) ||
    (item.children?.some(
      (c) => location.pathname === c.to || location.pathname.startsWith(c.to + '/'),
    ) ??
      false)
  const [openGroup, setOpenGroup] = useState(isUnder)

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- marshrut shu guruh ostida bo'lsa, uni avtomatik ochamiz (maqsadli)
    if (isUnder) setOpenGroup(true)
  }, [isUnder])

  return (
    <div>
      <button
        type="button"
        onClick={() => setOpenGroup((o) => !o)}
        className={cn(
          'flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition-colors',
          isUnder
            ? 'bg-brand-50 text-brand-700'
            : 'text-slate-600 hover:bg-slate-50 hover:text-slate-900',
        )}
      >
        <item.icon className="h-5 w-5" />
        {item.label}
        <ChevronDown
          className={cn('ml-auto h-4 w-4 transition-transform', openGroup && 'rotate-180')}
        />
      </button>

      {openGroup && (
        <div className="mt-1 space-y-1 pl-9">
          {item.children?.map((child) => (
            <NavLink
              key={child.to}
              to={child.to}
              end={child.end}
              onClick={onNavigate}
              className={({ isActive }) =>
                cn(
                  'block rounded-lg px-3 py-2 text-sm transition-colors',
                  isActive
                    ? 'bg-brand-50 font-medium text-brand-700'
                    : 'text-slate-500 hover:bg-slate-50 hover:text-slate-800',
                )
              }
            >
              {child.label}
            </NavLink>
          ))}
        </div>
      )}
    </div>
  )
}
