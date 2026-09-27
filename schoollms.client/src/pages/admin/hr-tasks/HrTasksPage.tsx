import { useState } from 'react'
import { Link } from 'react-router-dom'
import { BellRing, ChevronUp, Mic, Paperclip, Plus, Search, X } from 'lucide-react'
import { cancelTask, getEmployees, getTasks, hrErrorMessage } from '@/api/services/hrTasks'
import type { HrTask, HrTaskDetail } from '@/types/hrTasks'
import { HR_DEFAULT_PAGE_SIZE, HR_MAX_PAGE_SIZE } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import {
  Badge,
  ConfirmModal,
  CursorPager,
  DateRangeField,
  EmptyState,
  Field,
  PageHeader,
  PeriodTabs,
  Skeleton,
  ToggleChips,
  inputCls,
  primaryLinkCls,
} from '@/components/hr-tasks/ui'
import { useCursorStack, useDebounced, useFlashToast, useToast } from '@/components/hr-tasks/hooks'
import { useLoad } from '@/components/hr-tasks/useLoad'
import {
  HR_ACCEPTED_OPTIONS,
  HR_STATUS_OPTIONS,
  hrFormatDateTime,
  hrFormatNumber,
  hrIsOverdue,
  hrOverdueLabel,
  hrParseAccepted,
  hrParseStatuses,
  hrProgressText,
  hrTaskKindLabel,
  hrTaskSourceLabel,
  hrTaskStatusLabel,
  hrTaskStatusTone,
} from '@/components/hr-tasks/lib'
import { cn } from '@/lib/utils'
import { ReturnTaskModal, TaskEditModal } from './TaskActionModals'
import { TaskDetailDrawer } from './TaskDetailDrawer'
import { usePeriodRange } from './period'
import { hrtPaths } from './paths'

type Overlay = 'edit' | 'return' | 'cancel' | null

/** Backend `?sort=` allow-list — only the due-date column sorts. */
type SortValue = '-due_at' | 'due_at'

/**
 * Tasks — the director's main screen (the old `HrTasksPage`). Filters live in the URL, so
 * a dashboard link `?employeeId=…&dateFrom=…` works directly. Default — TODAY.
 */
export function HrTasksPage() {
  const toast = useToast()
  const flash = useFlashToast()
  const period = usePeriodRange()
  const { params, update, range } = period

  const employeeId = params.get('employeeId') ?? ''
  const statuses = hrParseStatuses(params.getAll('status'))
  const acceptedRaw = params.get('accepted') ?? ''
  const [search, setSearch] = useState(params.get('q') ?? '')
  const q = useDebounced(search.trim())
  const [sort, setSort] = useState<SortValue>('-due_at')
  const [version, setVersion] = useState(0)

  const employees = useLoad('employees|all', () => getEmployees({ limit: HR_MAX_PAGE_SIZE }))
  const employeeOptions = employees.data?.items ?? []

  const filterKey = JSON.stringify([employeeId, statuses, acceptedRaw, range, q, sort])
  const pager = useCursorStack(filterKey)

  const list = useLoad(`tasks|${filterKey}|${pager.cursor ?? ''}|${version}`, () =>
    getTasks({
      q,
      employeeId: employeeId || null,
      status: statuses,
      dateFrom: range.dateFrom || null,
      dateTo: range.dateTo || null,
      accepted: hrParseAccepted(acceptedRaw),
      sort,
      limit: HR_DEFAULT_PAGE_SIZE,
      cursor: pager.cursor,
      withTotal: true,
    }),
  )
  const page = list.data ?? list.previous
  const items = page?.items ?? []
  const total = page?.total ?? null

  const activeCount =
    (employeeId ? 1 : 0) + (statuses.length > 0 ? 1 : 0) + (acceptedRaw ? 1 : 0) + (search.trim() ? 1 : 0) +
    (period.isCustom || period.preset !== 'today' ? 1 : 0)

  function resetFilters(): void {
    setSearch('')
    update({ employeeId: null, status: null, accepted: null, period: null, dateFrom: null, dateTo: null, q: null })
  }

  function setStatuses(next: string[]): void {
    update({ status: next })
  }

  // ── Overlays ───────────────────────────────────────────────────────────────
  const [detailId, setDetailId] = useState<string | null>(null)
  const [overlay, setOverlay] = useState<Overlay>(null)
  const [activeTask, setActiveTask] = useState<HrTask | null>(null)
  const [cancelling, setCancelling] = useState(false)

  function openAction(next: Exclude<Overlay, null>, task: HrTaskDetail): void {
    setActiveTask(task)
    setOverlay(next)
  }

  function closeOverlay(): void {
    setOverlay(null)
    setActiveTask(null)
  }

  function afterAction(message: string): void {
    toast.success(message)
    closeOverlay()
    setVersion((v) => v + 1)
  }

  async function confirmCancel(): Promise<void> {
    if (!activeTask) return
    setCancelling(true)
    try {
      await cancelTask(activeTask.id)
      afterAction('Topshiriq bekor qilindi')
    } catch (err) {
      toast.error(hrErrorMessage(err))
    } finally {
      setCancelling(false)
    }
  }

  const summary = total !== null && total !== undefined ? `Jami ${total} ta topshiriq` : `${items.length} ta ko‘rsatilmoqda`

  const emptyBlock = list.error ? (
    <EmptyState
      title="Topshiriqlarni yuklab bo‘lmadi"
      hint={list.error}
      action={
        <Button variant="secondary" onClick={list.reload}>
          Qayta urinish
        </Button>
      }
    />
  ) : (
    <EmptyState
      title="Topshiriq topilmadi"
      hint="Tanlangan filtrlar bo‘yicha hech narsa yo‘q. Sanani kengaytirib yoki filtrlarni bo‘shatib ko‘ring."
      action={
        activeCount > 0 ? (
          <Button variant="secondary" onClick={resetFilters}>
            Filtrlarni tozalash
          </Button>
        ) : undefined
      }
    />
  )

  const th = 'px-3 py-3 font-medium'

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title="Topshiriqlar"
        description="Kim nima qilgani — bitta ro‘yxatda"
        actions={
          <Link to={hrtPaths.create(employeeId || undefined)} className={primaryLinkCls}>
            <Plus className="h-4 w-4" />
            Topshiriq berish
          </Link>
        }
      />

      <Card className="space-y-3 p-4">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          <Field label="Qidiruv">
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input
                className={cn(inputCls, 'pl-9')}
                placeholder="Sarlavha yoki tavsif bo‘yicha"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
          </Field>
          <Field label="Xodim">
            <select className={inputCls} value={employeeId} onChange={(e) => update({ employeeId: e.target.value || null })}>
              <option value="">{employees.loading ? 'Yuklanmoqda…' : 'Barchasi'}</option>
              {employeeOptions.map((employee) => (
                <option key={employee.id} value={employee.id}>
                  {employee.fullName}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Tasdiq">
            <select className={inputCls} value={acceptedRaw} onChange={(e) => update({ accepted: e.target.value || null })}>
              <option value="">Barchasi</option>
              {HR_ACCEPTED_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </Field>
          <DateRangeField
            label="Muddat"
            className="sm:col-span-2 lg:col-span-3 xl:col-span-1"
            dateFrom={range.dateFrom}
            dateTo={range.dateTo}
            onFrom={period.setFrom}
            onTo={period.setTo}
          />
        </div>

        <PeriodTabs value={period.preset} active={!period.isCustom} onChange={period.setPreset} />

        <div className="flex flex-wrap items-center gap-2">
          <span className="text-xs font-medium text-slate-500">Status</span>
          <ToggleChips options={HR_STATUS_OPTIONS} value={statuses} onChange={setStatuses} ariaLabel="Status" />
          {activeCount > 0 && (
            <Button variant="ghost" className="ml-auto px-3 py-1.5" onClick={resetFilters}>
              <X className="h-4 w-4" />
              Tozalash
            </Button>
          )}
        </div>
      </Card>

      <div className={cn('transition-opacity', list.loading && items.length > 0 && 'opacity-60')}>
        {list.loading && items.length === 0 ? (
          <div className="space-y-2">
            {Array.from({ length: 5 }, (_, i) => (
              <Skeleton key={i} className="h-16 w-full" />
            ))}
          </div>
        ) : items.length === 0 ? (
          <Card>{emptyBlock}</Card>
        ) : (
          <>
            <Card className="hidden overflow-hidden p-0 sm:block">
              <div className="overflow-x-auto">
                <table className="w-full min-w-[58rem] table-fixed text-left text-sm">
                  <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className={cn(th, 'w-[16rem] pl-5')}>Sarlavha</th>
                      <th className={cn(th, 'w-[11rem]')}>Xodim</th>
                      <th className={cn(th, 'w-[9rem]')}>Turi</th>
                      <th className={cn(th, 'w-[10rem]')}>Status</th>
                      <th className={cn(th, 'w-[10rem]')}>
                        <button
                          type="button"
                          onClick={() => setSort((s) => (s === '-due_at' ? 'due_at' : '-due_at'))}
                          className="inline-flex items-center gap-1 uppercase tracking-wide text-slate-600 hover:text-slate-800"
                        >
                          Muddat
                          <ChevronUp className={cn('h-3 w-3 transition', sort === '-due_at' && 'rotate-180')} />
                        </button>
                      </th>
                      <th className={cn(th, 'w-[6rem] text-right')}>Isbot</th>
                      <th className={cn(th, 'w-[8rem]')}>Manba</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {items.map((task) => {
                      const overdue = hrIsOverdue(task)
                      const progress = hrProgressText(task)
                      const proofs = task.proofCount ?? 0
                      return (
                        <tr
                          key={task.id}
                          tabIndex={0}
                          onClick={() => setDetailId(task.id)}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter') setDetailId(task.id)
                          }}
                          className={cn(
                            'cursor-pointer align-top transition-colors hover:bg-slate-50/70 focus-visible:bg-slate-50 focus-visible:outline-none',
                            task.status === 'cancelled' && 'opacity-60',
                          )}
                        >
                          <td className="py-3 pl-5 pr-3">
                            <p className="flex items-center gap-1.5 font-medium text-slate-800">
                              {task.voiceUrl && <Mic className="h-3.5 w-3.5 shrink-0 text-slate-400" aria-label="Ovozli izoh" />}
                              <span className="truncate">{task.title}</span>
                            </p>
                            {task.description && <p className="truncate text-xs text-slate-400">{task.description}</p>}
                          </td>
                          <td className="px-3 py-3">
                            <p className="truncate text-slate-700">{task.employeeName ?? '—'}</p>
                            {task.positionName && <p className="truncate text-xs text-slate-400">{task.positionName}</p>}
                          </td>
                          <td className="px-3 py-3">
                            <p className="truncate text-slate-700">{hrTaskKindLabel(task.kind)}</p>
                            {progress && <p className="truncate text-xs tabular-nums text-slate-400">{progress}</p>}
                          </td>
                          <td className="px-3 py-3">
                            <div className="flex flex-col items-start gap-1">
                              <Badge tone={hrTaskStatusTone(task.status)}>{hrTaskStatusLabel(task.status)}</Badge>
                              {task.needsAcceptance === true && (
                                <Badge tone="warning" dot>
                                  Tasdiq kutilmoqda
                                </Badge>
                              )}
                            </div>
                          </td>
                          <td className="px-3 py-3">
                            <p className={cn('truncate tabular-nums', overdue ? 'font-medium text-red-600' : 'text-slate-700')}>
                              {hrFormatDateTime(task.dueAt)}
                            </p>
                            {overdue && <p className="truncate text-xs text-red-600">{hrOverdueLabel(task.dueAt)}</p>}
                          </td>
                          <td className="px-3 py-3 text-right tabular-nums">
                            {proofs === 0 ? (
                              <span className="text-slate-300">—</span>
                            ) : (
                              <span className="inline-flex items-center gap-1 text-slate-700">
                                <Paperclip className="h-3.5 w-3.5 text-slate-400" />
                                {hrFormatNumber(proofs)}
                              </span>
                            )}
                          </td>
                          <td className="truncate px-3 py-3 text-slate-600">{hrTaskSourceLabel(task.source)}</td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            </Card>

            <ul className="space-y-2 sm:hidden">
              {items.map((task) => {
                const overdue = hrIsOverdue(task)
                const progress = hrProgressText(task)
                const proofs = task.proofCount ?? 0
                return (
                  <li key={task.id}>
                    <button
                      type="button"
                      onClick={() => setDetailId(task.id)}
                      className={cn(
                        'block w-full rounded-2xl border border-slate-200/80 bg-white p-3 text-left shadow-sm transition active:scale-[0.99]',
                        task.status === 'cancelled' && 'opacity-60',
                      )}
                    >
                      <div className="flex items-start justify-between gap-2">
                        <p className="flex min-w-0 flex-1 items-center gap-1.5 text-sm font-medium text-slate-800">
                          {task.voiceUrl && <Mic className="h-3.5 w-3.5 shrink-0 text-slate-400" aria-label="Ovozli izoh" />}
                          <span className="truncate">{task.title}</span>
                        </p>
                        <Badge className="shrink-0" tone={hrTaskStatusTone(task.status)}>
                          {hrTaskStatusLabel(task.status)}
                        </Badge>
                      </div>
                      <p className="mt-0.5 truncate text-xs text-slate-400">
                        {task.employeeName ?? '—'}
                        {task.positionName ? ` · ${task.positionName}` : ''}
                      </p>
                      <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
                        <span className={cn('tabular-nums', overdue ? 'font-medium text-red-600' : 'text-slate-400')}>
                          {hrFormatDateTime(task.dueAt)}
                          {overdue ? ` · ${hrOverdueLabel(task.dueAt)}` : ''}
                        </span>
                        {progress && <span className="tabular-nums text-slate-400">{progress}</span>}
                        <span className="inline-flex items-center gap-1 tabular-nums text-slate-400">
                          <Paperclip className="h-3 w-3" />
                          {proofs === 0 ? 'isbot yo‘q' : hrFormatNumber(proofs)}
                        </span>
                        {task.needsAcceptance === true && (
                          <span className="inline-flex items-center gap-1 font-medium text-amber-600">
                            <BellRing className="h-3 w-3" />
                            tasdiq kutilmoqda
                          </span>
                        )}
                      </div>
                    </button>
                  </li>
                )
              })}
            </ul>
          </>
        )}
      </div>

      <CursorPager
        summary={summary}
        loading={list.loading}
        hasPrev={pager.hasPrev}
        hasMore={Boolean(page?.hasMore && page.nextCursor)}
        onPrev={pager.prev}
        onNext={() => pager.next(page?.nextCursor ?? null)}
      />

      <TaskDetailDrawer
        open={detailId !== null && overlay === null}
        taskId={detailId}
        version={version}
        onClose={() => setDetailId(null)}
        onEdit={(task) => openAction('edit', task)}
        onReturn={(task) => openAction('return', task)}
        onCancel={(task) => openAction('cancel', task)}
      />

      {overlay === 'edit' && activeTask && (
        <TaskEditModal key={activeTask.id} task={activeTask} onClose={closeOverlay} onSaved={afterAction} />
      )}
      {overlay === 'return' && activeTask && (
        <ReturnTaskModal key={activeTask.id} task={activeTask} onClose={closeOverlay} onSaved={afterAction} />
      )}
      <ConfirmModal
        open={overlay === 'cancel'}
        onClose={closeOverlay}
        onConfirm={() => void confirmCancel()}
        danger
        busy={cancelling}
        confirmLabel="Bekor qilish"
        cancelLabel="Yopish"
        title="Topshiriq bekor qilinsinmi?"
        description={`«${activeTask?.title ?? ''}» bekor qilinadi. Bu yakuniy holat — ortga qaytarib bo‘lmaydi.`}
      />
      {toast.node}
      {flash}
    </div>
  )
}
