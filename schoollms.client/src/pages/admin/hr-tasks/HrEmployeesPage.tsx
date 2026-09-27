import { useMemo, useState } from 'react'
import { Briefcase, ChevronDown, ChevronRight, ChevronsDownUp, ChevronsUpDown, Pencil, Plus, RotateCcw, Search, UserMinus, Users, X } from 'lucide-react'
import { deactivateEmployee, getEmployeeTree, getPositions, hrErrorMessage, updateEmployee } from '@/api/services/hrTasks'
import type { HrEmployee } from '@/types/hrTasks'
import { HR_MAX_PAGE_SIZE } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Badge, ConfirmModal, EmptyState, Field, PageHeader, Skeleton, inputCls } from '@/components/hr-tasks/ui'
import { useDebounced, useToast } from '@/components/hr-tasks/hooks'
import { useLoad } from '@/components/hr-tasks/useLoad'
import { HR_ROLE_LABEL, USER_ROLE_LABEL, hrManagerNames } from '@/components/hr-tasks/lib'
import { formatPhone, phoneHref } from '@/lib/phone'
import { cn } from '@/lib/utils'
import { EmployeeModal } from './EmployeeModal'
import { InviteCopyButton } from './InviteLink'
import { PositionsModal } from './PositionsModal'
import {
  hrAllTreePaths,
  hrBuildTreeRows,
  hrDefaultExpandedPaths,
  hrFlatRows,
  hrIndentSteps,
  hrRowSubordinates,
  hrToggleTreePath,
  hrTreeSize,
  type HrTreeRow,
} from './employeeTree'
import { useIsHrDirector } from './viewer'

function toBool(value: string): boolean | null {
  if (value === 'true') return true
  if (value === 'false') return false
  return null
}

/**
 * Employees — the REPORTING TREE (the old `HrEmployeesPage`). With a search or filter on,
 * the tree becomes a FLAT list (a match must not hide in a collapsed branch). Employees are
 * never deleted: "Ishdan bo'shatish" deactivates the HR row and cancels open tasks; the
 * user account is left alone.
 */
export function HrEmployeesPage() {
  const toast = useToast()
  const isDirector = useIsHrDirector()

  const [search, setSearch] = useState('')
  const q = useDebounced(search.trim())
  const [positionId, setPositionId] = useState('')
  const [isActive, setIsActive] = useState('')
  const [linked, setLinked] = useState('')
  const [version, setVersion] = useState(0)

  const positions = useLoad(`positions|all|${version}`, () => getPositions({ limit: HR_MAX_PAGE_SIZE }))
  const positionItems = useMemo(() => positions.data?.items ?? positions.previous?.items ?? [], [positions.data, positions.previous])

  const linkedFilter = toBool(linked)
  const filterKey = JSON.stringify([q, positionId, isActive, linked])
  const query = useLoad(`tree|${filterKey}|${version}`, () =>
    getEmployeeTree({ q, positionId: positionId || null, isActive: toBool(isActive), linked: linkedFilter }),
  )
  const tree = query.data ?? query.previous
  const activeCount = (search.trim() ? 1 : 0) + (positionId ? 1 : 0) + (isActive ? 1 : 0) + (linked ? 1 : 0)
  // View: tree (default) or flat list; any filter forces flat.
  const [view, setView] = useState<'tree' | 'list'>('tree')
  const isFlat = activeCount > 0 || view === 'list'

  // Expanded branches by PATH; until the user toggles — first level only.
  const [expandedState, setExpandedState] = useState<{ key: string; paths: ReadonlySet<string> } | null>(null)
  const defaultExpanded = useMemo(() => hrDefaultExpandedPaths(tree), [tree])
  const expanded = expandedState && expandedState.key === filterKey ? expandedState.paths : defaultExpanded
  const branchPaths = useMemo(() => hrAllTreePaths(tree), [tree])
  const treeRows = useMemo(() => hrBuildTreeRows(tree, expanded), [tree, expanded])
  const flatRows = useMemo(() => {
    const rows = hrFlatRows(tree)
    // Stay correct even if the server ignores `linked`.
    return linkedFilter === null ? rows : rows.filter((row) => Boolean(row.employee.telegramUserId) === linkedFilter)
  }, [tree, linkedFilter])
  const visibleCount = isFlat ? flatRows.length : hrTreeSize(tree)

  const [dialog, setDialog] = useState<{ employee: HrEmployee | null } | null>(null)
  const [positionsOpen, setPositionsOpen] = useState(false)
  const [confirmTarget, setConfirmTarget] = useState<HrEmployee | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  function resetFilters(): void {
    setSearch('')
    setPositionId('')
    setIsActive('')
    setLinked('')
  }

  async function handleDeactivate(): Promise<void> {
    if (!confirmTarget) return
    const target = confirmTarget
    setConfirmTarget(null)
    setBusyId(target.id)
    try {
      await deactivateEmployee(target.id)
      toast.success('Xodim ishdan bo‘shatildi')
      setVersion((v) => v + 1)
    } catch (err) {
      toast.error(hrErrorMessage(err))
    } finally {
      setBusyId(null)
    }
  }

  async function handleRestore(employee: HrEmployee): Promise<void> {
    setBusyId(employee.id)
    try {
      await updateEmployee(employee.id, { isActive: true })
      toast.success('Xodim qayta faollashtirildi')
      setVersion((v) => v + 1)
    } catch (err) {
      toast.error(hrErrorMessage(err))
    } finally {
      setBusyId(null)
    }
  }

  const positionName = (employee: HrEmployee): string =>
    employee.positionName ?? (employee.positionId ? positionItems.find((p) => p.id === employee.positionId)?.name : null) ?? '—'

  const handlers: RowHandlers = {
    positionName,
    busyId,
    onEdit: (employee) => setDialog({ employee }),
    onDeactivate: setConfirmTarget,
    onRestore: (employee) => void handleRestore(employee),
    onToggle: (path) => setExpandedState({ key: filterKey, paths: hrToggleTreePath(expanded, path) }),
    onInvite: (message, tone) => (tone === 'success' ? toast.success(message) : toast.error(message)),
  }

  const isEmpty = visibleCount === 0
  const emptyNode = query.error ? (
    <EmptyState
      title="Xodimlarni yuklab bo‘lmadi"
      hint={query.error}
      action={
        <Button variant="secondary" onClick={query.reload}>
          Qayta urinish
        </Button>
      }
    />
  ) : (
    <EmptyState
      title="Xodim topilmadi"
      hint={activeCount > 0 ? 'Tanlangan filtrlar bo‘yicha hech narsa yo‘q.' : 'Birinchi xodimni qo‘shing — tizim unga taklif havolasini beradi.'}
      action={
        activeCount > 0 ? (
          <Button variant="secondary" onClick={resetFilters}>
            Filtrlarni tozalash
          </Button>
        ) : (
          <Button onClick={() => setDialog({ employee: null })}>
            <Plus className="h-4 w-4" />
            Xodim qo‘shish
          </Button>
        )
      }
    />
  )

  function renderRows(rows: HrTreeRow[], ariaLabel: string) {
    return (
      <div className={cn('transition-opacity', query.loading && 'opacity-60')}>
        <div className="sm:hidden">
          <EmployeeCards rows={rows} flat={isFlat} {...handlers} />
        </div>
        <div className="hidden sm:block">
          <EmployeeTreeTable rows={rows} flat={isFlat} ariaLabel={ariaLabel} {...handlers} />
        </div>
      </div>
    )
  }

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title="Xodimlar"
        description="Bo‘ysunish daraxti, Telegram bog‘lanishi va lavozimlar"
        actions={
          <>
            {isDirector && (
              <Button variant="secondary" onClick={() => setPositionsOpen(true)}>
                <Briefcase className="h-4 w-4" />
                Lavozimlar
              </Button>
            )}
            <Button onClick={() => setDialog({ employee: null })}>
              <Plus className="h-4 w-4" />
              Xodim
            </Button>
          </>
        }
      />

      <Card className="space-y-3 p-4">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
          <Field label="Qidiruv" className="sm:col-span-2">
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input className={cn(inputCls, 'pl-9')} placeholder="Ism yoki telefon raqami" value={search} onChange={(e) => setSearch(e.target.value)} />
            </div>
          </Field>
          <Field label="Lavozim">
            <select className={inputCls} value={positionId} onChange={(e) => setPositionId(e.target.value)}>
              <option value="">Barchasi</option>
              {positionItems.map((position) => (
                <option key={position.id} value={position.id}>
                  {position.name}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Holat">
            <select className={inputCls} value={isActive} onChange={(e) => setIsActive(e.target.value)}>
              <option value="">Barchasi</option>
              <option value="true">Faol</option>
              <option value="false">Ishdan bo‘shagan</option>
            </select>
          </Field>
          <Field label="Telegram">
            <select className={inputCls} value={linked} onChange={(e) => setLinked(e.target.value)}>
              <option value="">Barchasi</option>
              <option value="true">Bog‘langan</option>
              <option value="false">Bog‘lanmagan</option>
            </select>
          </Field>
        </div>
        {activeCount > 0 && (
          <div className="flex justify-end">
            <Button variant="ghost" className="px-3 py-1.5" onClick={resetFilters}>
              <X className="h-4 w-4" />
              Tozalash
            </Button>
          </div>
        )}
      </Card>

      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-3">
          <div className="inline-flex rounded-xl bg-slate-100 p-1" role="group" aria-label="Ko‘rinish">
            {(
              [
                { value: 'tree', label: 'Daraxt' },
                { value: 'list', label: 'Ro‘yxat' },
              ] as const
            ).map((option) => (
              <button
                key={option.value}
                type="button"
                aria-pressed={view === option.value}
                onClick={() => setView(option.value)}
                className={cn(
                  'rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
                  view === option.value ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                )}
              >
                {option.label}
              </button>
            ))}
          </div>
          <p className="text-sm text-slate-400">{isEmpty ? 'Xodim yo‘q' : `Jami ${visibleCount} ta xodim`}</p>
        </div>
        {!isFlat && branchPaths.size > 0 && (
          <div className="flex gap-2">
            <Button variant="secondary" className="px-3 py-1.5" onClick={() => setExpandedState({ key: filterKey, paths: branchPaths })}>
              <ChevronsUpDown className="h-4 w-4" />
              Hammasini ochish
            </Button>
            <Button variant="ghost" className="px-3 py-1.5" onClick={() => setExpandedState({ key: filterKey, paths: new Set<string>() })}>
              <ChevronsDownUp className="h-4 w-4" />
              Yig‘ish
            </Button>
          </div>
        )}
      </div>

      {isFlat && activeCount > 0 && !isEmpty && (
        <p className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-xs text-slate-400">
          Qidiruv yoki filtr yoqilgan — daraxt vaqtincha TEKIS ro‘yxatga aylandi, aks holda topilgan xodim yopiq shox
          ichida ko‘rinmay qolardi. Daraxtni qaytarish uchun filtrlarni tozalang.
        </p>
      )}

      {query.loading && !tree ? (
        <div className="space-y-3">
          {Array.from({ length: 3 }, (_, i) => (
            <Skeleton key={i} className="h-14 w-full" />
          ))}
        </div>
      ) : isEmpty ? (
        <Card>{emptyNode}</Card>
      ) : isFlat ? (
        renderRows(flatRows, 'Xodimlar ro‘yxati')
      ) : (
        <div className="space-y-4">
          {treeRows.roots.length > 0 && renderRows(treeRows.roots, 'Bo‘ysunish daraxti')}
          {treeRows.unassigned.length > 0 && (
            <section className="space-y-2">
              <h2 className="flex items-center gap-2 text-sm font-medium text-slate-700">
                <Users className="h-4 w-4 text-slate-400" />
                Biriktirilmagan
              </h2>
              <p className="text-xs text-slate-400">
                Rahbari ko‘rsatilmagan xodimlar. Rahbarni xodim formasidagi «Rahbarlari» maydonidan biriktiring.
              </p>
              {renderRows(treeRows.unassigned, 'Biriktirilmagan xodimlar')}
            </section>
          )}
        </div>
      )}

      {dialog && (
        <EmployeeModal
          key={dialog.employee?.id ?? 'new'}
          employee={dialog.employee}
          positions={positionItems}
          canSetHrRole={isDirector}
          onClose={() => setDialog(null)}
          onSaved={(message, close) => {
            toast.success(message)
            setVersion((v) => v + 1)
            if (close) setDialog(null)
          }}
        />
      )}

      {positionsOpen && (
        <PositionsModal
          onClose={() => setPositionsOpen(false)}
          onChanged={() => setVersion((v) => v + 1)}
          onToast={(message, tone) => (tone === 'success' ? toast.success(message) : toast.error(message))}
        />
      )}

      <ConfirmModal
        open={confirmTarget !== null}
        onClose={() => setConfirmTarget(null)}
        onConfirm={() => void handleDeactivate()}
        danger
        confirmLabel="Ishdan bo‘shatish"
        title="Xodimni ishdan bo‘shatish"
        description={`«${confirmTarget?.fullName ?? ''}» ro‘yxatdan chiqariladi. Xodimning ochiq topshiriqlari bekor qilinadi, tarixi saqlanadi. Tizimdagi hisobi o‘chirilmaydi. Kerak bo‘lsa uni keyin qayta faollashtirish mumkin.`}
      />
      {toast.node}
    </div>
  )
}

/* -------------------------------------------------------------------- rows */

interface RowHandlers {
  positionName: (employee: HrEmployee) => string
  busyId: string | null
  onEdit: (employee: HrEmployee) => void
  onDeactivate: (employee: HrEmployee) => void
  onRestore: (employee: HrEmployee) => void
  onToggle: (path: string) => void
  onInvite: (message: string, tone: 'success' | 'error') => void
}

/** Column template — shared by the header and the rows. */
const GRID = 'grid grid-cols-[minmax(14rem,1.4fr)_8rem_8.5rem_7.5rem_7rem_7rem_6rem_8.5rem] gap-x-3'

function EmployeeTreeTable({ rows, flat, ariaLabel, ...h }: RowHandlers & { rows: HrTreeRow[]; flat: boolean; ariaLabel: string }) {
  return (
    <Card className="overflow-hidden p-0">
      <div className="overflow-x-auto">
        <div className="min-w-[70rem]">
          <div className={cn(GRID, 'bg-slate-50 px-3 py-3 text-xs font-medium uppercase tracking-wide text-slate-400')}>
            <span className="pl-2">F.I.Sh.</span>
            <span>Lavozim</span>
            <span>Telefon</span>
            <span>Telegram</span>
            <span>HR doirasi</span>
            <span>Holat</span>
            <span className="text-right">Bo‘ysunuvchi</span>
            <span className="text-right">Amallar</span>
          </div>
          <ul aria-label={ariaLabel} className="divide-y divide-slate-100">
            {rows.map((row) => {
              const employee = row.employee
              const steps = flat ? 0 : hrIndentSteps(row.depth)
              const subordinates = hrRowSubordinates(row)
              return (
                <li
                  key={row.path}
                  className={cn(GRID, 'items-center px-3 py-2 text-sm transition-opacity', (!employee.isActive || h.busyId === employee.id) && 'opacity-60')}
                >
                  <div className="flex min-w-0 items-stretch self-stretch">
                    {Array.from({ length: steps }, (_, i) => (
                      <span key={i} aria-hidden className="w-5 shrink-0 border-l border-slate-200" />
                    ))}
                    {row.hasChildren && !flat ? (
                      <button
                        type="button"
                        aria-expanded={row.expanded}
                        aria-label={row.expanded ? `${employee.fullName} — bo‘ysunuvchilarini yopish` : `${employee.fullName} — bo‘ysunuvchilarini ochish`}
                        onClick={() => h.onToggle(row.path)}
                        className="mr-1 inline-flex h-7 w-7 shrink-0 items-center justify-center self-center rounded-lg text-slate-500 hover:bg-slate-100"
                      >
                        {row.expanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                      </button>
                    ) : (
                      <span aria-hidden className="mr-1 h-7 w-7 shrink-0 self-center" />
                    )}
                    <div className="min-w-0 self-center">
                      <button
                        type="button"
                        onClick={() => h.onEdit(employee)}
                        className="block max-w-full truncate rounded text-left font-medium text-slate-800 hover:underline"
                      >
                        {employee.fullName}
                      </button>
                      <p className="truncate text-xs text-slate-400">
                        {[USER_ROLE_LABEL[employee.role] ?? employee.role, employee.note].filter(Boolean).join(' · ')}
                      </p>
                      {flat && <p className="truncate text-xs text-slate-400">Rahbari: {hrManagerNames(employee)}</p>}
                    </div>
                  </div>
                  <span className="truncate text-slate-700">{h.positionName(employee)}</span>
                  <span className="truncate tabular-nums text-slate-700">{formatPhone(employee.phone)}</span>
                  <span className="truncate">
                    {employee.telegramUserId ? (
                      <Badge tone="success" dot>
                        Bog‘langan
                      </Badge>
                    ) : (
                      <Badge tone="warning" dot>
                        Kutilmoqda
                      </Badge>
                    )}
                  </span>
                  <span className="truncate">
                    {employee.hrRole !== 'employee' ? <Badge tone="info">{HR_ROLE_LABEL[employee.hrRole]}</Badge> : <span className="text-slate-300">—</span>}
                  </span>
                  <span className="truncate">{employee.isActive ? <Badge tone="info">Faol</Badge> : <Badge>Ishdan bo‘shagan</Badge>}</span>
                  <span className="text-right tabular-nums text-slate-700">{subordinates > 0 ? subordinates : '—'}</span>
                  <div className="flex items-center justify-end gap-1">
                    {!employee.telegramUserId && employee.isActive && <InviteCopyButton employee={employee} iconOnly onDone={h.onInvite} />}
                    <button
                      type="button"
                      aria-label={`${employee.fullName} — tahrirlash`}
                      onClick={() => h.onEdit(employee)}
                      className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                    >
                      <Pencil className="h-4 w-4" />
                    </button>
                    {employee.isActive ? (
                      <button
                        type="button"
                        aria-label={`${employee.fullName} — ishdan bo‘shatish`}
                        onClick={() => h.onDeactivate(employee)}
                        className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                      >
                        <UserMinus className="h-4 w-4" />
                      </button>
                    ) : (
                      <button
                        type="button"
                        aria-label={`${employee.fullName} — qayta faollashtirish`}
                        disabled={h.busyId === employee.id}
                        onClick={() => h.onRestore(employee)}
                        className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:opacity-50"
                      >
                        <RotateCcw className="h-4 w-4" />
                      </button>
                    )}
                  </div>
                </li>
              )
            })}
          </ul>
        </div>
      </div>
    </Card>
  )
}

/** Per-level indent on phones (px) and its cap. */
const MOBILE_INDENT_PX = 10
const MOBILE_MAX_STEPS = 5

function EmployeeCards({ rows, flat, ...h }: RowHandlers & { rows: HrTreeRow[]; flat: boolean }) {
  return (
    <ul className="space-y-3">
      {rows.map((row) => {
        const employee = row.employee
        const steps = flat ? 0 : Math.min(hrIndentSteps(row.depth), MOBILE_MAX_STEPS)
        const subordinates = hrRowSubordinates(row)
        return (
          <li
            key={row.path}
            style={{ marginInlineStart: steps * MOBILE_INDENT_PX }}
            className={cn(
              'rounded-2xl border border-slate-200/80 bg-white p-3 shadow-sm transition-opacity',
              steps > 0 && 'border-l-2 border-l-slate-300',
              (!employee.isActive || h.busyId === employee.id) && 'opacity-60',
            )}
          >
            <div className="flex items-start gap-2">
              {row.hasChildren && !flat && (
                <button
                  type="button"
                  aria-expanded={row.expanded}
                  aria-label={row.expanded ? `${employee.fullName} — bo‘ysunuvchilarini yopish` : `${employee.fullName} — bo‘ysunuvchilarini ochish`}
                  onClick={() => h.onToggle(row.path)}
                  className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-xl text-slate-500 hover:bg-slate-100"
                >
                  {row.expanded ? <ChevronDown className="h-5 w-5" /> : <ChevronRight className="h-5 w-5" />}
                </button>
              )}
              <div className="min-w-0 flex-1">
                <p className="break-words font-medium text-slate-800">{employee.fullName}</p>
                <p className="text-sm text-slate-400">{h.positionName(employee)}</p>
                {employee.note && <p className="mt-0.5 line-clamp-2 text-xs text-slate-400">{employee.note}</p>}
                <p className="mt-0.5 break-words text-xs text-slate-400">Rahbari: {hrManagerNames(employee)}</p>
              </div>
            </div>
            <p className="mt-2 text-sm tabular-nums text-slate-700">
              <a href={phoneHref(employee.phone)} className="underline decoration-slate-300 underline-offset-2">
                {formatPhone(employee.phone)}
              </a>
            </p>
            <div className="mt-2 flex flex-wrap items-center gap-1.5">
              {employee.telegramUserId ? (
                <Badge tone="success" dot>
                  Bog‘langan
                </Badge>
              ) : (
                <Badge tone="warning" dot>
                  Kutilmoqda
                </Badge>
              )}
              {employee.isActive ? <Badge tone="info">Faol</Badge> : <Badge>Ishdan bo‘shagan</Badge>}
              {employee.hrRole !== 'employee' && <Badge tone="info">{HR_ROLE_LABEL[employee.hrRole]}</Badge>}
              {subordinates > 0 && <Badge>{subordinates} ta bo‘ysunuvchi</Badge>}
              {employee.openTasksCount !== undefined && <Badge>{employee.openTasksCount} ta ochiq</Badge>}
            </div>
            <div className="mt-3 flex flex-wrap gap-2">
              <Button variant="secondary" className="flex-1" onClick={() => h.onEdit(employee)}>
                <Pencil className="h-4 w-4" />
                Tahrirlash
              </Button>
              {!employee.telegramUserId && employee.isActive && <InviteCopyButton employee={employee} onDone={h.onInvite} />}
              {employee.isActive ? (
                <Button variant="ghost" className="px-3" aria-label={`${employee.fullName} — ishdan bo‘shatish`} onClick={() => h.onDeactivate(employee)}>
                  <UserMinus className="h-4 w-4" />
                </Button>
              ) : (
                <Button
                  variant="ghost"
                  className="px-3"
                  aria-label={`${employee.fullName} — qayta faollashtirish`}
                  disabled={h.busyId === employee.id}
                  onClick={() => h.onRestore(employee)}
                >
                  <RotateCcw className="h-4 w-4" />
                </Button>
              )}
            </div>
          </li>
        )
      })}
    </ul>
  )
}
