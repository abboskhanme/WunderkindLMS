import { useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { Columns3, ChevronUp, ChevronDown, Eye, EyeOff, Pin, PinOff, RotateCcw } from 'lucide-react'
import {
  getTableSettings,
  saveTableSettings,
  type TableViewSettings,
} from '@/api/services/userTableSettings'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

/**
 * Umumiy jadval komponenti — `docs/modules/students-parity.md` §2.11 (X-1).
 *
 * Katta ro'yxatlar (Guruhlar va h.k.) uchun ustunlarni yashirish, tartiblash
 * va qadash (pin) imkonini beradi; tanlov `UserTableSettingsController`
 * orqali foydalanuvchi × sahifa kesimida serverda saqlanadi.
 *
 * Bu YANGI, umumiy komponent — mavjud jadval ekranlari (masalan Lidlar
 * taxtasi, O'quvchilar ro'yxati) TEGILMAYDI; ular o'z holicha qoladi.
 * Hozircha faqat Guruhlar ro'yxati (`GroupsPage.tsx`) shunga ulangan.
 */
export interface DataTableColumn<T> {
  /** Barqaror kalit — sozlamada aynan shu id saqlanadi. */
  id: string
  header: ReactNode
  cell: (row: T, index: number) => ReactNode
  cellClassName?: string
  headerClassName?: string
  /** Yashirib bo'lmaydigan ustun (masalan "Amallar"). */
  alwaysVisible?: boolean
  /** Hali sozlama saqlanmagan bo'lsa — sukut bo'yicha yashirilgan. */
  defaultHidden?: boolean
}

interface DataTableProps<T> {
  /** `user_table_settings.page` kaliti — ekran bo'yicha noyob (masalan "admin.groups"). */
  pageKey: string
  columns: DataTableColumn<T>[]
  rows: T[]
  getRowId: (row: T) => string
  onRowClick?: (row: T) => void
  rowClassName?: (row: T) => string | undefined
  loading?: boolean
  emptyMessage?: ReactNode
}

/** Sozlama bo'sh — hech narsa moslashtirilmagan (sukut ko'rinish). */
function isEmptyView(view: TableViewSettings): boolean {
  return !(view.order?.length || view.hidden?.length || view.pinned?.length)
}

/** Barcha ustun id'lari — saqlangan tartib + yangi qo'shilganlar oxirida. */
function resolveOrder<T>(columns: DataTableColumn<T>[], view: TableViewSettings): string[] {
  const known = new Set(columns.map((c) => c.id))
  const saved = (view.order ?? []).filter((id) => known.has(id))
  const savedSet = new Set(saved)
  const rest = columns.map((c) => c.id).filter((id) => !savedSet.has(id))
  return [...saved, ...rest]
}

/** Yashirilgan ustun id'lari — sozlama bo'sh bo'lsa, `defaultHidden` ishlatiladi. */
function resolveHidden<T>(columns: DataTableColumn<T>[], view: TableViewSettings): Set<string> {
  if (isEmptyView(view)) return new Set(columns.filter((c) => c.defaultHidden).map((c) => c.id))
  return new Set(view.hidden ?? [])
}

export function DataTable<T>({
  pageKey,
  columns,
  rows,
  getRowId,
  onRowClick,
  rowClassName,
  loading,
  emptyMessage,
}: DataTableProps<T>) {
  const [view, setView] = useState<TableViewSettings | null>(null)
  const [panelOpen, setPanelOpen] = useState(false)
  const panelRef = useRef<HTMLDivElement>(null)
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => {
    let cancelled = false
    getTableSettings(pageKey).then((s) => {
      if (!cancelled) setView(s)
    })
    return () => {
      cancelled = true
    }
  }, [pageKey])

  // Panel ochiq bo'lganda tashqariga bosilsa yopamiz (NotificationsBell bilan bir xil naqsh).
  useEffect(() => {
    if (!panelOpen) return
    const onClick = (e: MouseEvent) => {
      if (panelRef.current && !panelRef.current.contains(e.target as Node)) setPanelOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setPanelOpen(false)
    }
    document.addEventListener('mousedown', onClick)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onClick)
      document.removeEventListener('keydown', onKey)
    }
  }, [panelOpen])

  useEffect(() => () => {
    if (saveTimer.current) clearTimeout(saveTimer.current)
  }, [])

  const current = view ?? {}

  /** Darrov ekranga qo'llaydi, serverga esa bir oz kutib (debounce) saqlaydi. */
  const persist = (next: TableViewSettings) => {
    setView(next)
    if (saveTimer.current) clearTimeout(saveTimer.current)
    saveTimer.current = setTimeout(() => {
      saveTableSettings(pageKey, next).catch(() => {
        // Saqlanmasa ham ko'rinish ekranda qoladi — faqat keyingi safar tiklanmaydi.
      })
    }, 400)
  }

  const byId = new Map(columns.map((c) => [c.id, c]))
  const order = resolveOrder(columns, current)
  const hidden = resolveHidden(columns, current)
  const pinned = new Set(current.pinned ?? [])

  const visibleIds = order.filter((id) => !hidden.has(id) || byId.get(id)?.alwaysVisible)
  const pinnedIds = visibleIds.filter((id) => pinned.has(id))
  const restIds = visibleIds.filter((id) => !pinned.has(id))
  const renderColumns = [...pinnedIds, ...restIds]
    .map((id) => byId.get(id))
    .filter((c): c is DataTableColumn<T> => c != null)

  const toggleHidden = (id: string) => {
    const col = byId.get(id)
    if (col?.alwaysVisible) return
    const nextHidden = new Set(hidden)
    if (nextHidden.has(id)) nextHidden.delete(id)
    else nextHidden.add(id)
    persist({ order, hidden: [...nextHidden], pinned: [...pinned] })
  }

  const togglePinned = (id: string) => {
    const nextPinned = new Set(pinned)
    if (nextPinned.has(id)) nextPinned.delete(id)
    else nextPinned.add(id)
    persist({ order, hidden: [...hidden], pinned: [...nextPinned] })
  }

  const move = (id: string, dir: -1 | 1) => {
    const idx = order.indexOf(id)
    const swapWith = idx + dir
    if (idx < 0 || swapWith < 0 || swapWith >= order.length) return
    const next = [...order]
    ;[next[idx], next[swapWith]] = [next[swapWith], next[idx]]
    persist({ order: next, hidden: [...hidden], pinned: [...pinned] })
  }

  const reset = () => persist({})

  return (
    <div>
      <div className="flex justify-end px-3 pb-1 pt-3">
        <div className="relative" ref={panelRef}>
          <button
            type="button"
            onClick={() => setPanelOpen((v) => !v)}
            disabled={view === null}
            title="Ustunlarni sozlash"
            className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-xs font-medium text-slate-600 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
          >
            <Columns3 className="h-3.5 w-3.5" /> Ustunlar
          </button>
          {panelOpen && (
            <div className="absolute right-0 z-30 mt-1 w-80 rounded-xl border border-slate-200 bg-white p-2 shadow-lg">
              <div className="max-h-80 divide-y divide-slate-100 overflow-y-auto">
                {order.map((id) => {
                  const col = byId.get(id)
                  if (!col) return null
                  const isHidden = hidden.has(id) && !col.alwaysVisible
                  const isPinned = pinned.has(id)
                  return (
                    <div key={id} className="flex items-center gap-1 py-1.5">
                      <span
                        className={cn(
                          'flex-1 truncate text-sm',
                          isHidden ? 'text-slate-400' : 'text-slate-700',
                        )}
                      >
                        {col.header}
                      </span>
                      <button
                        type="button"
                        title="Yuqoriga"
                        onClick={() => move(id, -1)}
                        className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700 disabled:opacity-30"
                        disabled={order.indexOf(id) === 0}
                      >
                        <ChevronUp className="h-3.5 w-3.5" />
                      </button>
                      <button
                        type="button"
                        title="Pastga"
                        onClick={() => move(id, 1)}
                        className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700 disabled:opacity-30"
                        disabled={order.indexOf(id) === order.length - 1}
                      >
                        <ChevronDown className="h-3.5 w-3.5" />
                      </button>
                      <button
                        type="button"
                        title={isPinned ? 'Qadashni bekor qilish' : 'Qadash'}
                        onClick={() => togglePinned(id)}
                        className={cn(
                          'rounded p-1 hover:bg-slate-100',
                          isPinned ? 'text-brand-600' : 'text-slate-400 hover:text-slate-700',
                        )}
                      >
                        {isPinned ? <Pin className="h-3.5 w-3.5" /> : <PinOff className="h-3.5 w-3.5" />}
                      </button>
                      <button
                        type="button"
                        title={isHidden ? "Ko'rsatish" : 'Yashirish'}
                        onClick={() => toggleHidden(id)}
                        disabled={col.alwaysVisible}
                        className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-30"
                      >
                        {isHidden ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
                      </button>
                    </div>
                  )
                })}
              </div>
              <button
                type="button"
                onClick={reset}
                className="mt-2 inline-flex w-full items-center justify-center gap-1.5 rounded-lg px-2 py-1.5 text-xs text-slate-500 hover:bg-slate-100"
              >
                <RotateCcw className="h-3.5 w-3.5" /> Sukutga qaytarish
              </button>
            </div>
          )}
        </div>
      </div>

      <div className="overflow-x-auto">
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                {renderColumns.map((c) => (
                  <th key={c.id} className={cn('px-4 py-3', c.headerClassName)}>
                    {c.header}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rows.map((row, i) => (
                <tr
                  key={getRowId(row)}
                  onClick={onRowClick ? () => onRowClick(row) : undefined}
                  className={cn(onRowClick && 'cursor-pointer hover:bg-slate-50/60', rowClassName?.(row))}
                >
                  {renderColumns.map((c) => (
                    <td key={c.id} className={cn('px-4 py-3', c.cellClassName)}>
                      {c.cell(row, i)}
                    </td>
                  ))}
                </tr>
              ))}
              {rows.length === 0 && (
                <tr>
                  <td colSpan={renderColumns.length || 1} className="px-4 py-12 text-center text-slate-400">
                    {emptyMessage ?? "Ma'lumot yo'q"}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
