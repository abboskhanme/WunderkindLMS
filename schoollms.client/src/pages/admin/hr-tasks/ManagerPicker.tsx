import { useMemo, useState } from 'react'
import { Check, Search, X } from 'lucide-react'
import { getEmployees } from '@/api/services/hrTasks'
import type { HrEmployee } from '@/types/hrTasks'
import { HR_MAX_PAGE_SIZE } from '@/types/hrTasks'
import { EmptyState, Field, Skeleton, inputCls } from '@/components/hr-tasks/ui'
import { useLoad } from '@/components/hr-tasks/useLoad'
import { formatPhone } from '@/lib/phone'
import { cn } from '@/lib/utils'

/** Max managers per employee (backend `MAX_MANAGERS`). */
export const HR_MAX_MANAGERS = 20

function haystack(employee: HrEmployee): string {
  return [employee.fullName, employee.phone, employee.positionName ?? ''].join(' ').toLowerCase()
}

/**
 * "Rahbarlari" — pick several managers (the old `ManagerPicker`). Self is excluded; longer
 * loops (A → B → A) are rejected by the backend with 409 `hr_manager_cycle`.
 */
export function ManagerPicker({
  selfId = null,
  value,
  onChange,
  error,
  resolveName,
}: {
  selfId?: string | null
  value: string[]
  onChange: (next: string[]) => void
  error?: string | null
  resolveName?: (id: string) => string | null
}) {
  const [search, setSearch] = useState('')
  const query = useLoad('employees|active', () => getEmployees({ isActive: true, limit: HR_MAX_PAGE_SIZE }))
  const employees = useMemo(() => query.data?.items ?? [], [query.data])
  const selectedSet = useMemo(() => new Set(value), [value])

  const candidates = useMemo(() => employees.filter((e) => e.id !== selfId && e.isActive), [employees, selfId])
  const visible = useMemo(() => {
    const needle = search.trim().toLowerCase()
    return needle === '' ? candidates : candidates.filter((e) => haystack(e).includes(needle))
  }, [candidates, search])
  const byId = useMemo(() => new Map(employees.map((e) => [e.id, e] as const)), [employees])

  const nameOf = (id: string): string => byId.get(id)?.fullName ?? resolveName?.(id) ?? id
  const isFull = value.length >= HR_MAX_MANAGERS

  function toggle(id: string): void {
    if (selectedSet.has(id)) {
      onChange(value.filter((item) => item !== id))
      return
    }
    if (isFull) return
    onChange([...value, id])
  }

  return (
    <Field
      label="Rahbarlari"
      error={error}
      hint="Rahbar biriktirish panelga kirish huquqini BERMAYDI — u Boshqaruv → Rollar'da beriladi. Bu tanlov faqat bo‘ysunish daraxtini va rahbarning ko‘rish doirasini belgilaydi."
    >
      <div className="space-y-2">
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            aria-label="Rahbarni qidirish"
            className={cn(inputCls, 'pl-9')}
            placeholder="Ism, telefon yoki lavozim"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') e.preventDefault()
            }}
          />
        </div>

        {value.length > 0 && (
          <div className="flex flex-wrap gap-1.5" aria-label="Tanlangan rahbarlar">
            {value.map((id) => (
              <span
                key={id}
                className="inline-flex max-w-full items-center gap-1 rounded-full border border-brand-200 bg-brand-50 py-1 pl-3 pr-1 text-xs text-brand-800"
              >
                <span className="truncate">{nameOf(id)}</span>
                <button
                  type="button"
                  aria-label={`${nameOf(id)} — rahbarlikdan olib tashlash`}
                  onClick={() => toggle(id)}
                  className="inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full hover:bg-brand-100"
                >
                  <X className="h-3.5 w-3.5" />
                </button>
              </span>
            ))}
          </div>
        )}

        <div className="max-h-56 overflow-y-auto rounded-xl border border-slate-200">
          {query.loading ? (
            <div className="space-y-2 p-3">
              {Array.from({ length: 3 }, (_, i) => (
                <Skeleton key={i} className="h-10 w-full" />
              ))}
            </div>
          ) : query.error ? (
            <EmptyState title="Xodimlar ro‘yxatini yuklab bo‘lmadi" hint="Oynani yopib qaytadan oching." />
          ) : visible.length === 0 ? (
            <EmptyState
              title="Xodim topilmadi"
              hint={candidates.length === 0 ? 'Rahbar qilib biriktirish uchun boshqa faol xodim yo‘q.' : 'Qidiruvni o‘zgartirib ko‘ring.'}
            />
          ) : (
            <ul className="divide-y divide-slate-100">
              {visible.map((item) => {
                const checked = selectedSet.has(item.id)
                return (
                  <li key={item.id}>
                    <button
                      type="button"
                      aria-pressed={checked}
                      disabled={isFull && !checked}
                      onClick={() => toggle(item.id)}
                      className="flex w-full items-center gap-3 px-3 py-2.5 text-left transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
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
                        <span className="block truncate text-sm text-slate-800">{item.fullName}</span>
                        <span className="block truncate text-xs text-slate-400">
                          {[item.positionName ?? 'Lavozim belgilanmagan', item.phone ? formatPhone(item.phone) : null].filter(Boolean).join(' · ')}
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
          {value.length === 0
            ? 'Rahbar tanlanmagan — xodim «Biriktirilmagan» guruhida turadi'
            : isFull
              ? `${value.length} ta rahbar tanlandi — chegara (${HR_MAX_MANAGERS} ta) to‘ldi`
              : `${value.length} ta rahbar tanlandi`}
        </p>
      </div>
    </Field>
  )
}
