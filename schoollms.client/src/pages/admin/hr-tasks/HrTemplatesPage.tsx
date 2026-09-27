import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Pencil, Plus, Search, Trash2, Users, X } from 'lucide-react'
import { deleteTemplate, getEmployees, getPositions, getTemplates, hrErrorMessage, toggleTemplate } from '@/api/services/hrTasks'
import type { HrRecurrence, HrTargetType, HrTaskTemplate } from '@/types/hrTasks'
import { HR_DEFAULT_PAGE_SIZE, HR_MAX_PAGE_SIZE, HR_RECURRENCES, HR_TARGET_TYPES } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import {
  ConfirmModal,
  CursorPager,
  EmptyState,
  Field,
  PageHeader,
  Skeleton,
  Switch,
  inputCls,
  secondaryLinkCls,
} from '@/components/hr-tasks/ui'
import { useCursorStack, useDebounced, useFlashToast, useToast } from '@/components/hr-tasks/hooks'
import { useLoad } from '@/components/hr-tasks/useLoad'
import { HR_RECURRENCE_LABEL, HR_TARGET_TYPE_LABEL, hrRecurrenceLabel, hrTaskKindLabel } from '@/components/hr-tasks/lib'
import { cn } from '@/lib/utils'
import { TemplateModal } from './TemplateModal'
import { hrtPaths } from './paths'
import { useIsHrDirector } from './viewer'

/**
 * Recurring tasks — the old `HrTemplatesPage`. A template is a RULE that spawns a task per
 * period. Deleting keeps history; the everyday action is the "Faol" switch.
 */
export function HrTemplatesPage() {
  const toast = useToast()
  const flash = useFlashToast()
  const isDirector = useIsHrDirector()

  const [search, setSearch] = useState('')
  const q = useDebounced(search.trim())
  const [targetType, setTargetType] = useState<HrTargetType | ''>('')
  const [recurrence, setRecurrence] = useState<HrRecurrence | ''>('')
  const [isActive, setIsActive] = useState<'' | 'true' | 'false'>('')
  const [version, setVersion] = useState(0)

  const filterKey = JSON.stringify([q, targetType, recurrence, isActive])
  const pager = useCursorStack(filterKey)

  const positions = useLoad('positions|all', () => getPositions({ limit: HR_MAX_PAGE_SIZE }))
  const employees = useLoad('employees|active', () => getEmployees({ isActive: true, limit: HR_MAX_PAGE_SIZE }))
  const positionItems = useMemo(() => positions.data?.items ?? [], [positions.data])
  const employeeItems = useMemo(() => employees.data?.items ?? [], [employees.data])

  const list = useLoad(`templates|${filterKey}|${pager.cursor ?? ''}|${version}`, () =>
    getTemplates({
      q,
      targetType: targetType || null,
      recurrence: recurrence || null,
      isActive: isActive === '' ? null : isActive === 'true',
      limit: HR_DEFAULT_PAGE_SIZE,
      cursor: pager.cursor,
      withTotal: true,
    }),
  )
  const page = list.data ?? list.previous
  const items = page?.items ?? []
  const total = page?.total ?? null
  const activeCount = (search.trim() ? 1 : 0) + (targetType ? 1 : 0) + (recurrence ? 1 : 0) + (isActive ? 1 : 0)

  const [editing, setEditing] = useState<HrTaskTemplate | null>(null)
  const [creating, setCreating] = useState(false)
  const [confirmTarget, setConfirmTarget] = useState<HrTaskTemplate | null>(null)
  const [deleting, setDeleting] = useState(false)

  function resetFilters(): void {
    setSearch('')
    setTargetType('')
    setRecurrence('')
    setIsActive('')
  }

  /** Optimistic switch: flips at once, reverts on error. */
  async function handleToggle(template: HrTaskTemplate): Promise<void> {
    const flip = (id: string) =>
      list.setData((prev) => ({
        ...prev,
        items: prev.items.map((item) => (item.id === id ? { ...item, isActive: !item.isActive } : item)),
      }))
    flip(template.id)
    try {
      const next = await toggleTemplate(template.id)
      toast.success(next.isActive ? 'Shablon yoqildi' : 'Shablon o‘chirildi')
    } catch (err) {
      flip(template.id)
      toast.error(hrErrorMessage(err))
    }
  }

  async function handleDelete(): Promise<void> {
    if (!confirmTarget) return
    setDeleting(true)
    try {
      await deleteTemplate(confirmTarget.id)
      setConfirmTarget(null)
      toast.success('Shablon o‘chirildi')
      setVersion((v) => v + 1)
    } catch (err) {
      toast.error(hrErrorMessage(err))
    } finally {
      setDeleting(false)
    }
  }

  /** "Xodim: Aliyev Aziz" or "Lavozim: Marketolog". */
  const targetLabel = useMemo(() => {
    const employeeById = new Map(employeeItems.map((e) => [e.id, e.fullName] as const))
    const positionById = new Map(positionItems.map((p) => [p.id, p.name] as const))
    return (t: HrTaskTemplate): string => {
      const kind = HR_TARGET_TYPE_LABEL[t.targetType]
      const name =
        t.targetType === 'employee'
          ? (t.targetEmployeeName ?? (t.targetEmployeeId ? employeeById.get(t.targetEmployeeId) : null))
          : (t.targetPositionName ?? (t.targetPositionId ? positionById.get(t.targetPositionId) : null))
      return name ? `${kind}: ${name}` : kind
    }
  }, [employeeItems, positionItems])

  const kindLabel = (t: HrTaskTemplate): string =>
    t.kind !== 'numeric'
      ? hrTaskKindLabel(t.kind)
      : `${hrTaskKindLabel('numeric')}: ${t.targetAmount ?? '—'}${t.unit ? ` ${t.unit}` : ''}`

  const summary = total !== null && total !== undefined ? `Jami ${total} ta shablon` : `${items.length} ta ko‘rsatilmoqda`

  const emptyNode = list.error ? (
    <EmptyState
      title="Shablonlarni yuklab bo‘lmadi"
      hint={list.error}
      action={
        <Button variant="secondary" onClick={list.reload}>
          Qayta urinish
        </Button>
      }
    />
  ) : (
    <EmptyState
      title="Takroriy topshiriq yo‘q"
      hint={
        activeCount > 0
          ? 'Tanlangan filtrlar bo‘yicha hech narsa yo‘q.'
          : 'Har kuni takrorlanadigan ishlarni shablon qilib qo‘ying — tizim ularni o‘zi yaratadi.'
      }
      action={
        activeCount > 0 ? (
          <Button variant="secondary" onClick={resetFilters}>
            Filtrlarni tozalash
          </Button>
        ) : (
          <Button onClick={() => setCreating(true)}>
            <Plus className="h-4 w-4" />
            Shablon qo‘shish
          </Button>
        )
      }
    />
  )

  const th = 'px-3 py-3 font-medium'

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title="Takroriy topshiriqlar"
        description="Har kuni, har hafta yoki har oy avtomatik yaratiladigan ishlar"
        actions={
          <>
            <Link to={hrtPaths.createRecurring} className={secondaryLinkCls}>
              <Users className="h-4 w-4" />
              Bir nechta xodimga
            </Link>
            <Button onClick={() => setCreating(true)}>
              <Plus className="h-4 w-4" />
              Takroriy topshiriq
            </Button>
          </>
        }
      />

      <Card className="space-y-3 p-4">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <Field label="Qidiruv">
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input className={cn(inputCls, 'pl-9')} placeholder="Sarlavha bo‘yicha" value={search} onChange={(e) => setSearch(e.target.value)} />
            </div>
          </Field>
          <Field label="Kimga">
            <select className={inputCls} value={targetType} onChange={(e) => setTargetType(e.target.value as HrTargetType | '')}>
              <option value="">Barchasi</option>
              {HR_TARGET_TYPES.map((value) => (
                <option key={value} value={value}>
                  {HR_TARGET_TYPE_LABEL[value]}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Takroriylik">
            <select className={inputCls} value={recurrence} onChange={(e) => setRecurrence(e.target.value as HrRecurrence | '')}>
              <option value="">Barchasi</option>
              {HR_RECURRENCES.map((value) => (
                <option key={value} value={value}>
                  {HR_RECURRENCE_LABEL[value]}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Holat">
            <select className={inputCls} value={isActive} onChange={(e) => setIsActive(e.target.value as '' | 'true' | 'false')}>
              <option value="">Barchasi</option>
              <option value="true">Faol</option>
              <option value="false">O‘chirilgan</option>
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

      <div className={cn('transition-opacity', list.loading && items.length > 0 && 'opacity-60')}>
        {list.loading && items.length === 0 ? (
          <div className="space-y-2">
            {Array.from({ length: 4 }, (_, i) => (
              <Skeleton key={i} className="h-14 w-full" />
            ))}
          </div>
        ) : items.length === 0 ? (
          <Card>{emptyNode}</Card>
        ) : (
          <>
            <Card className="hidden overflow-hidden p-0 sm:block">
              <div className="overflow-x-auto">
                <table className="w-full min-w-[58rem] table-fixed text-left text-sm">
                  <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className={cn(th, 'w-[14rem] pl-5')}>Sarlavha</th>
                      <th className={cn(th, 'w-[12rem]')}>Kimga</th>
                      <th className={cn(th, 'w-[12rem]')}>Takroriylik</th>
                      <th className={cn(th, 'w-[7rem]')}>Muddat</th>
                      <th className={cn(th, 'w-[11rem]')}>Turi</th>
                      <th className={cn(th, 'w-[5.5rem]')}>Faol</th>
                      <th className={cn(th, 'w-[6.5rem]')}>
                        <span className="sr-only">Amallar</span>
                      </th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {items.map((t) => (
                      <tr
                        key={t.id}
                        onClick={() => setEditing(t)}
                        className={cn('cursor-pointer transition-colors hover:bg-slate-50/70', !t.isActive && 'opacity-60')}
                      >
                        <td className="py-3 pl-5 pr-3">
                          <p className="truncate font-medium text-slate-800">{t.title}</p>
                          {t.description && <p className="truncate text-xs text-slate-400">{t.description}</p>}
                        </td>
                        <td className="truncate px-3 py-3 text-slate-600" title={targetLabel(t)}>
                          {targetLabel(t)}
                        </td>
                        <td className="truncate px-3 py-3 text-slate-600" title={hrRecurrenceLabel(t)}>
                          {hrRecurrenceLabel(t)}
                        </td>
                        <td className="px-3 py-3 tabular-nums text-slate-600">{t.dueTime ? t.dueTime.slice(0, 5) : 'Kun oxiri'}</td>
                        <td className="truncate px-3 py-3 text-slate-600">{kindLabel(t)}</td>
                        <td className="px-3 py-3">
                          <Switch
                            checked={t.isActive}
                            ariaLabel={`«${t.title}» shabloni yoqilgan`}
                            onChange={() => void handleToggle(t)}
                          />
                        </td>
                        <td className="px-3 py-3">
                          <div className="flex items-center justify-end gap-1" onClick={(e) => e.stopPropagation()}>
                            <button
                              type="button"
                              aria-label="Tahrirlash"
                              onClick={() => setEditing(t)}
                              className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                            >
                              <Pencil className="h-4 w-4" />
                            </button>
                            <button
                              type="button"
                              aria-label="O‘chirish"
                              onClick={() => setConfirmTarget(t)}
                              className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                            >
                              <Trash2 className="h-4 w-4" />
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </Card>

            <ul className="space-y-3 sm:hidden">
              {items.map((t) => (
                <li
                  key={t.id}
                  className={cn('rounded-2xl border border-slate-200/80 bg-white p-3 shadow-sm', !t.isActive && 'opacity-60')}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <p className="break-words font-medium text-slate-800">{t.title}</p>
                      {t.description && <p className="mt-0.5 line-clamp-2 text-xs text-slate-400">{t.description}</p>}
                    </div>
                    <Switch checked={t.isActive} ariaLabel={`«${t.title}» shabloni yoqilgan`} onChange={() => void handleToggle(t)} />
                  </div>
                  <dl className="mt-2 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs">
                    <dt className="text-slate-400">Kimga</dt>
                    <dd className="min-w-0 truncate text-slate-700">{targetLabel(t)}</dd>
                    <dt className="text-slate-400">Takroriylik</dt>
                    <dd className="min-w-0 truncate text-slate-700">{hrRecurrenceLabel(t)}</dd>
                    <dt className="text-slate-400">Muddat</dt>
                    <dd className="text-slate-700">{t.dueTime ? t.dueTime.slice(0, 5) : 'Kun oxiri'}</dd>
                    <dt className="text-slate-400">Turi</dt>
                    <dd className="min-w-0 truncate text-slate-700">{kindLabel(t)}</dd>
                  </dl>
                  <div className="mt-3 flex gap-2">
                    <Button variant="secondary" className="flex-1" onClick={() => setEditing(t)}>
                      <Pencil className="h-4 w-4" />
                      Tahrirlash
                    </Button>
                    <Button variant="ghost" className="px-3" aria-label="O‘chirish" onClick={() => setConfirmTarget(t)}>
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                </li>
              ))}
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

      {(creating || editing) && (
        <TemplateModal
          key={editing?.id ?? 'new'}
          template={editing}
          employees={employeeItems}
          positions={positionItems.filter((p) => p.isActive || p.id === editing?.targetPositionId)}
          canManagePositions={isDirector}
          onClose={() => {
            setCreating(false)
            setEditing(null)
          }}
          onSaved={(message) => {
            setCreating(false)
            setEditing(null)
            toast.success(message)
            setVersion((v) => v + 1)
          }}
        />
      )}

      <ConfirmModal
        open={confirmTarget !== null}
        onClose={() => setConfirmTarget(null)}
        onConfirm={() => void handleDelete()}
        danger
        busy={deleting}
        confirmLabel="O‘chirish"
        title="Shablonni o‘chirish"
        description={`«${confirmTarget?.title ?? ''}» butunlay o‘chiriladi va yangi nusxalar yaratilmaydi. Ilgari yaratilgan topshiriqlar tarixda qoladi. Vaqtincha to‘xtatish uchun «Faol» tugmasini o‘chiring.`}
      />
      {toast.node}
      {flash}
    </div>
  )
}
