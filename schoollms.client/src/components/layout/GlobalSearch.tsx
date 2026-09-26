/**
 * Yuqori paneldagi umumiy qidiruv (mijoz, 2026-09-22) — "Xush kelibsiz" yozuvi
 * o'rnida, iOS/Apple uslubidagi keng maydon.
 *
 * Nimani topadi:
 * - bo'limlar (menyu) — darrov, brauzerning o'zida;
 * - o'quvchi, ota-ona, o'qituvchi, sinf, guruh, lid — serverdan
 *   (`GET /api/admin/search`): ism-familiya istalgan tartibda, telefon istalgan
 *   formatda. Har bo'lim o'z ruxsati bilan — server ruxsati yo'q bo'limni
 *   qaytarmaydi.
 *
 * Klaviatura: ⌘K / Ctrl+K — maydonga o'tish, ↑↓ — tanlash, ↵ — ochish, Esc — yopish.
 * Eski Ctrl+K buyruq paneli (CommandPalette, olib tashlangan) shu maydonga birlashdi: bo'limlar
 * ro'yxati ham shu yerda.
 */
import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  BookOpen,
  GraduationCap,
  Loader2,
  Search,
  UserPlus,
  UserRound,
  Users,
  UsersRound,
  X,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type { Role } from '@/types'
import { useAuth } from '@/context/auth-context'
import { navByRole, type NavChild, type NavItem } from '@/config/navigation'
import { canSeeNav } from '@/lib/access'
import { globalSearch, type GlobalSearchHit, type GlobalSearchKind } from '@/api/services/globalSearch'
import { cn } from '@/lib/utils'

/** Server qidiruvi faqat shu rollar uchun (qolganlarga faqat bo'limlar). */
const SEARCH_ROLES: Role[] = ['admin', 'superadmin', 'staff']
const MIN_LENGTH = 2
const DEBOUNCE_MS = 250

const KIND_META: Record<GlobalSearchKind, { label: string; icon: LucideIcon; tone: string }> = {
  student: { label: "O'quvchilar", icon: UserRound, tone: 'bg-amber-50 text-amber-600' },
  parent: { label: 'Ota-onalar', icon: Users, tone: 'bg-rose-50 text-rose-500' },
  teacher: { label: "O'qituvchilar", icon: GraduationCap, tone: 'bg-emerald-50 text-emerald-600' },
  class: { label: 'Sinflar', icon: BookOpen, tone: 'bg-sky-50 text-sky-600' },
  group: { label: 'Guruhlar', icon: UsersRound, tone: 'bg-violet-50 text-violet-600' },
  lead: { label: 'Lidlar', icon: UserPlus, tone: 'bg-orange-50 text-orange-500' },
}
const KIND_ORDER: GlobalSearchKind[] = ['student', 'parent', 'teacher', 'class', 'group', 'lead']

interface Item {
  key: string
  section: string
  title: string
  subtitle?: string
  to: string
  icon: LucideIcon
  tone: string
  archived?: boolean
}

export function GlobalSearch() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  // Oxirgi javob — QAYSI so'rov uchun ekani bilan: "yuklanmoqda" shundan hosil qilinadi,
  // effekt ichida holatni sinxron o'zgartirish shart bo'lmaydi.
  const [result, setResult] = useState<{ q: string; hits: GlobalSearchHit[]; error: boolean }>({
    q: '',
    hits: [],
    error: false,
  })
  const inputRef = useRef<HTMLInputElement>(null)
  const boxRef = useRef<HTMLDivElement>(null)
  const listRef = useRef<HTMLDivElement>(null)

  const canSearchServer = !!user && SEARCH_ROLES.includes(user.role as Role)
  const q = query.trim()
  const serverOn = canSearchServer && q.length >= MIN_LENGTH
  const hits = useMemo(() => (serverOn && result.q === q ? result.hits : []), [serverOn, result, q])
  const error = serverOn && result.q === q && result.error
  const loading = serverOn && result.q !== q

  // ⌘K / Ctrl+K — maydonga o'tish (eski buyruq panelining tugmasi ham shu hodisani yuboradi).
  useEffect(() => {
    const focus = () => {
      inputRef.current?.focus()
      setOpen(true)
    }
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault()
        focus()
      }
    }
    document.addEventListener('keydown', onKey)
    window.addEventListener('cmdk:open', focus)
    return () => {
      document.removeEventListener('keydown', onKey)
      window.removeEventListener('cmdk:open', focus)
    }
  }, [])

  // Tashqariga bosilganda yopamiz.
  useEffect(() => {
    if (!open) return
    const onClick = (e: MouseEvent) => {
      if (boxRef.current && !boxRef.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onClick)
    return () => document.removeEventListener('mousedown', onClick)
  }, [open])

  // Server qidiruvi — kechiktirib va eskisini bekor qilib.
  useEffect(() => {
    if (!serverOn) return
    const ctrl = new AbortController()
    const t = setTimeout(() => {
      globalSearch(q, ctrl.signal)
        .then((res) => setResult({ q, hits: res, error: false }))
        .catch((e: unknown) => {
          if ((e as { name?: string })?.name === 'CanceledError') return
          setResult({ q, hits: [], error: true })
        })
    }, DEBOUNCE_MS)
    return () => {
      clearTimeout(t)
      ctrl.abort()
    }
  }, [q, serverOn])

  // Bo'limlar — Sidebar bilan bir xil ko'rinish qoidasi (rol + ruxsat).
  const pages = useMemo<Item[]>(() => {
    if (!user) return []
    const role = user.role as Role
    const canSee = (x: NavItem | NavChild) => canSeeNav(user, x)
    const out: Item[] = []
    for (const item of navByRole[role] ?? []) {
      if (!canSee(item)) continue
      if (item.children && !item.children.some(canSee)) continue
      const push = (label: string, to: string, group?: string) =>
        out.push({
          key: `page:${to}:${label}`,
          section: "Bo'limlar",
          title: label,
          subtitle: group,
          to,
          icon: item.icon,
          tone: 'bg-slate-100 text-slate-500',
        })
      if (item.children) {
        for (const c of item.children) if (canSee(c)) push(c.label, c.to, item.label)
      } else push(item.label, item.to)
    }
    return out
  }, [user])

  const items = useMemo<Item[]>(() => {
    if (!q) return []
    const needle = q.toLocaleLowerCase('uz')
    const pageHits = pages
      .filter(
        (p) =>
          p.title.toLocaleLowerCase('uz').includes(needle) ||
          (p.subtitle?.toLocaleLowerCase('uz').includes(needle) ?? false),
      )
      .slice(0, 5)
    const entityHits = KIND_ORDER.flatMap((kind) =>
      hits
        .filter((h) => h.kind === kind)
        .map<Item>((h) => ({
          key: `${h.kind}:${h.id}`,
          section: KIND_META[kind].label,
          title: h.title,
          subtitle: h.subtitle,
          to: h.url,
          icon: KIND_META[kind].icon,
          tone: KIND_META[kind].tone,
          archived: h.archived,
        })),
    )
    return [...entityHits, ...pageHits]
  }, [q, pages, hits])

  // Natijalar qisqarsa tanlov chegaradan chiqmasin.
  const current = Math.min(active, Math.max(0, items.length - 1))

  useEffect(() => {
    listRef.current?.querySelector<HTMLElement>(`[data-idx="${current}"]`)?.scrollIntoView({ block: 'nearest' })
  }, [current])

  const go = (item?: Item) => {
    const target = item ?? items[current]
    if (!target) return
    setOpen(false)
    setQuery('')
    ;(document.activeElement as HTMLElement | null)?.blur()
    navigate(target.to)
  }

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Escape') {
      if (query) setQuery('')
      else {
        setOpen(false)
        ;(document.activeElement as HTMLElement | null)?.blur()
      }
    } else if (e.key === 'ArrowDown') {
      e.preventDefault()
      setOpen(true)
      setActive(Math.min(current + 1, items.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive(Math.max(current - 1, 0))
    } else if (e.key === 'Enter') {
      e.preventDefault()
      go()
    }
  }

  const showPanel = open && q.length > 0
  const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform)

  return (
    <div ref={boxRef} className="relative w-full max-w-xl">
      <div
        className={cn(
          'flex h-10 items-center gap-2 rounded-full bg-slate-100/80 px-4 ring-1 ring-transparent transition-all',
          'focus-within:bg-white focus-within:shadow-sm focus-within:ring-slate-200',
        )}
      >
        {loading ? (
          <Loader2 className="h-4 w-4 shrink-0 animate-spin text-slate-400" />
        ) : (
          <Search className="h-4 w-4 shrink-0 text-slate-400" />
        )}
        <input
          ref={inputRef}
          value={query}
          onChange={(e) => {
            setQuery(e.target.value)
            setActive(0)
            setOpen(true)
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={onKeyDown}
          placeholder={
            canSearchServer ? "O'quvchi, o'qituvchi, guruh yoki telefon raqami..." : "Bo'lim qidirish..."
          }
          className="min-w-0 flex-1 bg-transparent text-sm text-slate-800 outline-none placeholder:text-slate-400"
          aria-label="Qidirish"
          role="combobox"
          aria-expanded={showPanel}
          aria-controls="global-search-results"
        />
        {query ? (
          <button
            type="button"
            onClick={() => {
              setQuery('')
              inputRef.current?.focus()
            }}
            className="rounded-full bg-slate-300/70 p-0.5 text-white transition-colors hover:bg-slate-400"
            aria-label="Tozalash"
          >
            <X className="h-3 w-3" />
          </button>
        ) : (
          <kbd className="hidden rounded-md bg-white px-1.5 py-0.5 text-[10px] font-medium text-slate-400 shadow-sm sm:block">
            {isMac ? '⌘K' : 'Ctrl K'}
          </kbd>
        )}
      </div>

      {showPanel && (
        <div
          id="global-search-results"
          ref={listRef}
          role="listbox"
          className="absolute left-0 right-0 top-12 z-50 max-h-[70vh] overflow-y-auto rounded-2xl border border-slate-200/70 bg-white/95 p-2 shadow-xl backdrop-blur-xl"
        >
          {items.length === 0 ? (
            <p className="px-3 py-8 text-center text-sm text-slate-400">
              {q.length < MIN_LENGTH && canSearchServer
                ? 'Kamida 2 ta harf yoki 3 ta raqam kiriting'
                : loading
                  ? 'Qidirilmoqda...'
                  : error
                    ? "Qidiruvda xatolik — qayta urinib ko'ring"
                    : 'Hech narsa topilmadi'}
            </p>
          ) : (
            items.map((item, i) => (
              <div key={item.key}>
                {(i === 0 || items[i - 1].section !== item.section) && (
                  <p className="px-3 pb-1 pt-2 text-[11px] font-semibold uppercase tracking-wide text-slate-400">
                    {item.section}
                  </p>
                )}
                <button
                  type="button"
                  role="option"
                  aria-selected={i === current}
                  data-idx={i}
                  onMouseEnter={() => setActive(i)}
                  onClick={() => go(item)}
                  className={cn(
                    'flex w-full items-center gap-3 rounded-xl px-3 py-2 text-left transition-colors',
                    i === current ? 'bg-slate-100' : 'hover:bg-slate-50',
                  )}
                >
                  <span className={cn('flex h-8 w-8 shrink-0 items-center justify-center rounded-full', item.tone)}>
                    <item.icon className="h-4 w-4" />
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-medium text-slate-800" title={item.title}>
                      {item.title}
                      {item.archived && (
                        <span className="ml-2 rounded-full bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-slate-500">
                          Arxiv
                        </span>
                      )}
                    </span>
                    {item.subtitle && (
                      <span className="block truncate text-xs text-slate-400" title={item.subtitle}>
                        {item.subtitle}
                      </span>
                    )}
                  </span>
                </button>
              </div>
            ))
          )}
        </div>
      )}
    </div>
  )
}
