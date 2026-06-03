import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Search } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type { Role } from '@/types'
import { useAuth } from '@/context/auth-context'
import { navByRole } from '@/config/navigation'
import { cn } from '@/lib/utils'

interface Cmd {
  label: string
  /** Ota guruh nomi (masalan "Boshqaruv") — bor bo'lsa ko'rsatamiz */
  group?: string
  to: string
  icon: LucideIcon
}

/**
 * Ctrl/⌘+K buyruq paneli — istalgan admin/o'qituvchi bo'limini tez qidirib o'tish uchun.
 * Ro'yxat Sidebar bilan bir xil mantiqda (rol + ruxsat) filtrlanadi.
 * Boshqa joydan ochish uchun: `window.dispatchEvent(new Event('cmdk:open'))`.
 */
export function CommandPalette() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)
  const listRef = useRef<HTMLDivElement>(null)

  // Ochish/yopish: Ctrl/⌘+K yoki tashqi 'cmdk:open' hodisasi
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault()
        setOpen((o) => !o)
      }
    }
    const onOpen = () => setOpen(true)
    document.addEventListener('keydown', onKey)
    window.addEventListener('cmdk:open', onOpen)
    return () => {
      document.removeEventListener('keydown', onKey)
      window.removeEventListener('cmdk:open', onOpen)
    }
  }, [])

  // Ochilganda qidiruvni tozalab, inputni fokuslaymiz
  useEffect(() => {
    if (!open) return
    setQuery('')
    setActive(0)
    const t = setTimeout(() => inputRef.current?.focus(), 0)
    return () => clearTimeout(t)
  }, [open])

  // Ko'rinadigan bo'limlar ro'yxati (Sidebar'dagi canSee bilan bir xil)
  const commands = useMemo<Cmd[]>(() => {
    if (!user) return []
    const role = user.role as Role
    const canSee = (x: { roles?: Role[]; perm?: string }) =>
      (!x.roles || x.roles.includes(role)) &&
      (!x.perm || !user.permissions || user.permissions.includes(x.perm))
    const out: Cmd[] = []
    for (const item of navByRole[role]) {
      if (!canSee(item)) continue
      if (item.children) {
        for (const c of item.children) {
          if (canSee(c)) out.push({ label: c.label, group: item.label, to: c.to, icon: item.icon })
        }
      } else {
        out.push({ label: item.label, to: item.to, icon: item.icon })
      }
    }
    return out
  }, [user])

  const results = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return commands
    return commands.filter(
      (c) => c.label.toLowerCase().includes(q) || (c.group?.toLowerCase().includes(q) ?? false),
    )
  }, [commands, query])

  // Qidiruv o'zgarsa tanlovni boshiga qaytaramiz
  useEffect(() => setActive(0), [query])

  // Tanlangan element ko'rinishda turishi uchun unga aylantiramiz
  useEffect(() => {
    listRef.current
      ?.querySelector<HTMLElement>(`[data-idx="${active}"]`)
      ?.scrollIntoView({ block: 'nearest' })
  }, [active, results])

  if (!open) return null

  const go = (c?: Cmd) => {
    const target = c ?? results[active]
    if (!target) return
    setOpen(false)
    navigate(target.to)
  }

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape') {
      setOpen(false)
    } else if (e.key === 'ArrowDown') {
      e.preventDefault()
      setActive((a) => Math.min(a + 1, results.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((a) => Math.max(a - 1, 0))
    } else if (e.key === 'Enter') {
      e.preventDefault()
      go()
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-slate-900/40 px-4 pt-[15vh]"
      onClick={() => setOpen(false)}
    >
      <div
        className="w-full max-w-lg overflow-hidden rounded-2xl bg-white shadow-2xl"
        onClick={(e) => e.stopPropagation()}
        onKeyDown={onKeyDown}
      >
        <div className="flex items-center gap-3 border-b border-slate-100 px-4">
          <Search className="h-5 w-5 shrink-0 text-slate-400" />
          <input
            ref={inputRef}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Bo'lim qidirish..."
            className="flex-1 bg-transparent py-4 text-sm text-slate-800 outline-none placeholder:text-slate-400"
          />
          <kbd className="rounded border border-slate-200 px-1.5 py-0.5 text-[10px] text-slate-400">
            ESC
          </kbd>
        </div>

        <div ref={listRef} className="max-h-80 overflow-y-auto p-2">
          {results.length === 0 ? (
            <p className="py-10 text-center text-sm text-slate-400">Hech narsa topilmadi</p>
          ) : (
            results.map((c, i) => (
              <button
                key={c.to + c.label}
                type="button"
                data-idx={i}
                onMouseEnter={() => setActive(i)}
                onClick={() => go(c)}
                className={cn(
                  'flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-left text-sm transition-colors',
                  i === active ? 'bg-brand-50 text-brand-700' : 'text-slate-700 hover:bg-slate-50',
                )}
              >
                <c.icon className="h-4 w-4 shrink-0 text-slate-400" />
                <span className="flex-1 truncate">
                  {c.group && <span className="text-slate-400">{c.group} · </span>}
                  {c.label}
                </span>
              </button>
            ))
          )}
        </div>

        <div className="flex items-center gap-4 border-t border-slate-100 px-4 py-2 text-[11px] text-slate-400">
          <span>↑↓ tanlash</span>
          <span>↵ ochish</span>
          <span>esc yopish</span>
        </div>
      </div>
    </div>
  )
}
