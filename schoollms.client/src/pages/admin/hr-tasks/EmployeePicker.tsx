import { useMemo, useState } from 'react'
import { Check, Search, Users, X } from 'lucide-react'
import type { HrEmployee, HrPosition } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { EmptyState, Field, Skeleton, inputCls } from '@/components/hr-tasks/ui'
import { formatPhone } from '@/lib/phone'
import { cn } from '@/lib/utils'

/** One search string — name, phone and position. */
function haystack(employee: HrEmployee): string {
  return [employee.fullName, employee.phone, employee.positionName ?? ''].join(' ').toLowerCase()
}

/**
 * The "Kimga" block — pick several employees (the old `EmployeePicker`). Search is
 * client-side: the full list is loaded anyway (for "Butun lavozim"). Inactive employees
 * are not listed.
 */
export function EmployeePicker({
  employees,
  positions,
  loading,
  errored,
  selected,
  onChange,
}: {
  employees: HrEmployee[]
  positions: HrPosition[]
  loading: boolean
  errored?: boolean
  selected: string[]
  onChange: (next: string[]) => void
}) {
  const [search, setSearch] = useState('')
  const selectedSet = useMemo(() => new Set(selected), [selected])

  const visible = useMemo(() => {
    const needle = search.trim().toLowerCase()
    return needle === '' ? employees : employees.filter((e) => haystack(e).includes(needle))
  }, [employees, search])

  const selectedEmployees = useMemo(() => employees.filter((e) => selectedSet.has(e.id)), [employees, selectedSet])

  function addAll(ids: string[]): void {
    const next = [...selected]
    for (const id of ids) if (!selectedSet.has(id)) next.push(id)
    onChange(next)
  }

  function toggle(id: string): void {
    onChange(selectedSet.has(id) ? selected.filter((item) => item !== id) : [...selected, id])
  }

  function selectPosition(positionId: string): void {
    if (!positionId) return
    addAll(employees.filter((e) => e.positionId === positionId).map((e) => e.id))
  }

  return (
    <div className="space-y-3">
      <Field label="Xodimni qidirish">
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            className={cn(inputCls, 'pl-9')}
            placeholder="Ism, telefon yoki lavozim"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
      </Field>

      <div className="space-y-2">
        <p className="text-xs font-medium text-slate-500">Birdan belgilash</p>
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            variant="secondary"
            className="px-3 py-1.5"
            disabled={loading || employees.length === 0}
            onClick={() => addAll(employees.map((e) => e.id))}
          >
            <Users className="h-4 w-4" />
            Hammasi
          </Button>
          {selected.length > 0 && (
            <Button type="button" variant="ghost" className="px-3 py-1.5" onClick={() => onChange([])}>
              <X className="h-4 w-4" />
              Tanlovni tozalash
            </Button>
          )}
        </div>
        <Field label="Butun lavozim">
          <select
            className={inputCls}
            value=""
            disabled={loading || positions.length === 0}
            onChange={(e) => selectPosition(e.target.value)}
          >
            <option value="">Lavozimni tanlang</option>
            {positions.map((position) => (
              <option key={position.id} value={position.id}>
                {position.name}
              </option>
            ))}
          </select>
        </Field>
      </div>

      {selectedEmployees.length > 0 && (
        <div className="flex flex-wrap gap-1.5" aria-label="Tanlangan xodimlar">
          {selectedEmployees.map((employee) => (
            <span
              key={employee.id}
              className="inline-flex max-w-full items-center gap-1 rounded-full border border-brand-200 bg-brand-50 py-1 pl-3 pr-1 text-xs text-brand-800"
            >
              <span className="truncate">{employee.fullName}</span>
              <button
                type="button"
                aria-label={`${employee.fullName} — tanlovdan olib tashlash`}
                onClick={() => toggle(employee.id)}
                className="inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full hover:bg-brand-100"
              >
                <X className="h-3.5 w-3.5" />
              </button>
            </span>
          ))}
        </div>
      )}

      <div className="max-h-72 overflow-y-auto rounded-xl border border-slate-200">
        {loading ? (
          <div className="space-y-2 p-3">
            {Array.from({ length: 4 }, (_, i) => (
              <Skeleton key={i} className="h-10 w-full" />
            ))}
          </div>
        ) : errored ? (
          <EmptyState title="Xodimlar ro‘yxatini yuklab bo‘lmadi" hint="Sahifani yangilab ko‘ring." />
        ) : visible.length === 0 ? (
          <EmptyState
            title="Xodim topilmadi"
            hint={employees.length === 0 ? 'Avval «Xodimlar» bo‘limida xodim qo‘shing.' : 'Qidiruvni o‘zgartirib ko‘ring.'}
          />
        ) : (
          <ul className="divide-y divide-slate-100">
            {visible.map((employee) => {
              const checked = selectedSet.has(employee.id)
              return (
                <li key={employee.id}>
                  <button
                    type="button"
                    aria-pressed={checked}
                    onClick={() => toggle(employee.id)}
                    className="flex w-full items-center gap-3 px-3 py-2.5 text-left transition-colors hover:bg-slate-50"
                  >
                    <span
                      aria-hidden
                      className={cn(
                        'inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-md border',
                        checked ? 'border-brand-600 bg-brand-600 text-white' : 'border-slate-300',
                      )}
                    >
                      {checked && <Check className="h-3.5 w-3.5" />}
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm text-slate-800">{employee.fullName}</span>
                      <span className="block truncate text-xs text-slate-400">
                        {[employee.positionName ?? 'Lavozim belgilanmagan', formatPhone(employee.phone)].join(' · ')}
                      </span>
                    </span>
                  </button>
                </li>
              )
            })}
          </ul>
        )}
      </div>

      <p className="text-xs text-slate-400">
        {selected.length === 0 ? 'Hech kim tanlanmagan' : `${selected.length} ta xodim tanlandi`}
      </p>
    </div>
  )
}
