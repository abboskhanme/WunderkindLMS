import { useEffect, useRef, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { ChevronDown, ChevronRight } from 'lucide-react'
import type { Role } from '@/types'
import { useAuth } from '@/context/auth-context'
import { useUnread } from '@/context/unread-context'
import { getSchoolName } from '@/api/services/settings'
import { navByRole, homeByRole, type NavChild, type NavItem } from '@/config/navigation'
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
  // Element ko'rinadi: roli mos (yoki roles yo'q) VA xodim ruxsati bor (yoki perm yo'q / permissions yo'q).
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
        // Silliq kirib-chiqish (mijoz, 2026-09-22): kompyuterda panel `display:none`
        // bilan keskin yo'qolmaydi — chapga suriladi va manfiy margin bilan joyini
        // bo'shatadi, asosiy qism ham shu bilan birga kengayadi. Telefonda — ustma-ust.
        'fixed inset-y-0 left-0 z-40 flex w-64 shrink-0 flex-col border-r border-slate-200 bg-white lg:static',
        'transition-[transform,margin] duration-300 ease-in-out motion-reduce:transition-none',
        open ? 'translate-x-0 lg:ml-0' : '-translate-x-full lg:-ml-64',
      )}
    >
      {/* Logo */}
      <div className="flex h-16 items-center gap-2.5 border-b border-slate-200 px-6">
        <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-[#FFD006]">
          <img src="/logo.png" alt="Wunderkind International School" className="h-6 w-6 object-contain" />
        </div>
        <div className="leading-tight">
          <p className="font-semibold text-slate-800">{schoolName || 'Wunderkind School'}</p>
        </div>
      </div>

      {/* Menyu.
          `overflow-y-auto` faqat KENGLIKDA kesmasin: yonga ochiladigan panel
          shu konteynerdan tashqariga chiqadi, shuning uchun `overflow-x`
          ochiq qoldirilgan (`overflow-y-auto` o'zi `overflow-x: hidden` ni
          keltirib chiqarmaydi, ammo `overflow-visible` bilan aralashsa
          brauzer y ni ham kesadi — panel `fixed` bilan chiziladi). */}
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

/** Bolalarni `group` bo'yicha, e'lon qilingan tartibni saqlab ajratadi. */
function byGroup(children: NavChild[]): { name: string | null; items: NavChild[] }[] {
  const out: { name: string | null; items: NavChild[] }[] = []
  for (const c of children) {
    const name = c.group ?? null
    const last = out[out.length - 1]
    if (last && last.name === name) last.items.push(c)
    else out.push({ name, items: [c] })
  }
  return out
}

/**
 * Bolali menyu elementi.
 *
 * <b>KENG EKRANDA — YONGA OCHILADI</b> (mijoz talabi, 2026-09-13: EduSchool
 * shunday ishlaydi). Panel elementning o'ng yonida chiziladi va bolalarni
 * `group` sarlavhalari bo'yicha ustunlarga ajratadi.
 *
 * <b>TELEFONDA — PASTGA OCHILADI.</b> Yon panel u yerda ekrandan chiqib
 * ketardi: yon menyu 256px, telefon esa ~375px. Shuning uchun kichik ekranda
 * eski akkordeon qoladi — bitta holat, ikki xil ko'rinish.
 *
 * Panel `fixed` bilan chiziladi, chunki menyu konteyneri `overflow-y-auto`:
 * `absolute` bo'lsa u kesilib qolardi.
 */
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
  const [flyoutTop, setFlyoutTop] = useState<number | null>(null)
  const rowRef = useRef<HTMLDivElement>(null)
  const closeTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- marshrut shu guruh ostida bo'lsa, uni avtomatik ochamiz (maqsadli)
    if (isUnder) setOpenGroup(true)
  }, [isUnder])

  useEffect(() => () => { if (closeTimer.current) clearTimeout(closeTimer.current) }, [])

  const groups = byGroup(item.children ?? [])
  // Ustun soni: ko'pi bilan uchta — undan keng panel ekranga sig'masligi mumkin.
  const columns = Math.min(groups.length || 1, 3)

  /** Sichqoncha element va panel orasidan o'tayotganda panel yopilib qolmasin. */
  const openFlyout = () => {
    if (closeTimer.current) clearTimeout(closeTimer.current)
    const r = rowRef.current?.getBoundingClientRect()
    if (r) setFlyoutTop(Math.min(r.top, window.innerHeight - 120))
  }
  const scheduleClose = () => {
    if (closeTimer.current) clearTimeout(closeTimer.current)
    closeTimer.current = setTimeout(() => setFlyoutTop(null), 140)
  }

  const childClass = ({ isActive }: { isActive: boolean }) =>
    cn(
      'flex items-center gap-2.5 rounded-lg px-3 py-2 text-[15px] transition-colors',
      isActive
        ? 'bg-brand-50 font-medium text-brand-700'
        : 'text-slate-500 hover:bg-slate-50 hover:text-slate-800',
    )

  return (
    <div ref={rowRef} onMouseEnter={openFlyout} onMouseLeave={scheduleClose}>
      <button
        type="button"
        // Katta ekranda bosish ham panelni ochadi (sensorli noutbuklar uchun);
        // kichik ekranda esa akkordeonni ochadi.
        onClick={() => {
          setOpenGroup((o) => !o)
          if (flyoutTop === null) openFlyout()
          else setFlyoutTop(null)
        }}
        className={cn(
          'flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition-colors',
          isUnder || flyoutTop !== null
            ? 'bg-brand-50 text-brand-700'
            : 'text-slate-600 hover:bg-slate-50 hover:text-slate-900',
        )}
      >
        <item.icon className="h-5 w-5" />
        {item.label}
        <ChevronRight className="ml-auto hidden h-4 w-4 lg:block" />
        <ChevronDown
          className={cn('ml-auto h-4 w-4 transition-transform lg:hidden', openGroup && 'rotate-180')}
        />
      </button>

      {/* Keng ekran — yon panel */}
      {flyoutTop !== null && (
        <div
          style={{ top: flyoutTop }}
          onMouseEnter={openFlyout}
          onMouseLeave={scheduleClose}
          className="fixed left-64 z-50 ml-2 hidden max-h-[80vh] max-w-[calc(100vw-17.5rem)] overflow-auto rounded-2xl border border-slate-200 bg-white p-5 shadow-xl lg:block"
        >
          <div
            className="grid gap-x-10 gap-y-1"
            style={{ gridTemplateColumns: `repeat(${columns}, max-content)` }}
          >
            {groups.map((g, gi) => (
              <div key={g.name ?? gi} className="min-w-0">
                {g.name && (
                  <p className="px-3 pb-1 pt-1 text-[11px] font-semibold uppercase tracking-wider text-slate-400">
                    {g.name}
                  </p>
                )}
                {g.items.map((child) => (
                  <NavLink
                    key={child.to}
                    to={child.to}
                    end={child.end}
                    onClick={() => {
                      setFlyoutTop(null)
                      onNavigate()
                    }}
                    className={childClass}
                  >
                    <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-slate-300" />
                    <span className="whitespace-nowrap">{child.label}</span>
                  </NavLink>
                ))}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Telefon — pastga ochiladigan akkordeon */}
      {openGroup && (
        <div className="mt-1 space-y-1 pl-9 lg:hidden">
          {groups.map((g, gi) => (
            <div key={g.name ?? gi}>
              {g.name && (
                <p className="px-3 pb-0.5 pt-2 text-[11px] font-semibold uppercase tracking-wider text-slate-400">
                  {g.name}
                </p>
              )}
              {g.items.map((child) => (
                <NavLink
                  key={child.to}
                  to={child.to}
                  end={child.end}
                  onClick={onNavigate}
                  className={childClass}
                >
                  <span className="truncate">{child.label}</span>
                </NavLink>
              ))}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
