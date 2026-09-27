import type { ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ChevronRight, ChevronUp, Inbox } from 'lucide-react'
import type { LaObservationListItem } from '@/types/lessonAnalysis'
import { Card } from '@/components/ui/Card'
import { DatePicker } from '@/components/ui/DatePicker'
import { cn } from '@/lib/utils'
import { fmtDate } from './format'

/** Foiz belgisi — eski ilova chegaralari: 85+ yashil, 65+ sariq, qolgani qizil. */
export function Score({ percent, size = 'md' }: { percent: number; size?: 'md' | 'lg' }) {
  const tone =
    percent >= 85 ? 'bg-emerald-50 text-emerald-700'
    : percent >= 65 ? 'bg-amber-50 text-amber-700'
    : 'bg-red-50 text-red-600'
  const dims = size === 'lg' ? 'px-3 py-1.5 text-base' : 'px-2.5 py-1 text-xs'
  return (
    <span className={cn('inline-flex items-center rounded-lg font-semibold tabular-nums', tone, dims)}>
      {Math.round(percent)}%
    </span>
  )
}

export function StatTile({
  label,
  value,
  hint,
  icon,
}: {
  label: string
  value: ReactNode
  hint?: string
  icon?: ReactNode
}) {
  return (
    <Card className="min-w-0 p-4">
      <div className="flex items-start justify-between gap-3">
        <p className="text-xs font-medium uppercase tracking-wide text-slate-400">{label}</p>
        {icon && <span className="text-slate-300">{icon}</span>}
      </div>
      <div className="mt-2 text-2xl font-semibold leading-none tabular-nums text-slate-800">{value}</div>
      {hint && <p className="mt-1.5 truncate text-xs text-slate-400" title={hint}>{hint}</p>}
    </Card>
  )
}

export function EmptyState({ title, hint, action }: { title: string; hint?: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center px-6 py-14 text-center">
      <span className="mb-3 grid h-12 w-12 place-items-center rounded-full bg-slate-100 text-slate-400">
        <Inbox className="h-5 w-5" />
      </span>
      <p className="font-semibold text-slate-700">{title}</p>
      {hint && <p className="mt-1 max-w-sm text-sm text-slate-400">{hint}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}

export function Notice({ kind = 'error', children }: { kind?: 'error' | 'info' | 'success'; children: ReactNode }) {
  const tone = {
    error: 'border-red-200 bg-red-50 text-red-700',
    info: 'border-brand-100 bg-brand-50 text-brand-800',
    success: 'border-emerald-200 bg-emerald-50 text-emerald-800',
  }[kind]
  return <div className={cn('rounded-xl border px-4 py-3 text-sm leading-snug', tone)}>{children}</div>
}

export function StatusChips({ status, hasAi }: { status: LaObservationListItem['status']; hasAi?: boolean }) {
  return (
    <span className="flex flex-wrap items-center gap-1.5">
      {status === 'draft' ? (
        <span className="rounded-md bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">Qoralama</span>
      ) : (
        <span className="rounded-md bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">Yakunlangan</span>
      )}
      {hasAi && <span className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">AI xulosa</span>}
    </span>
  )
}

export function SortHeader({
  label,
  active,
  dir,
  onClick,
}: {
  label: string
  active: boolean
  dir: 'asc' | 'desc'
  onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn('inline-flex items-center gap-1 uppercase tracking-wide transition hover:text-slate-700', active && 'text-slate-700')}
    >
      {label}
      <ChevronUp
        aria-hidden
        className={cn('h-3 w-3 transition', active ? 'opacity-100' : 'opacity-0', active && dir === 'desc' && 'rotate-180')}
      />
    </button>
  )
}

export function DateRangeFields({
  dateFrom,
  dateTo,
  onFrom,
  onTo,
}: {
  dateFrom: string
  dateTo: string
  onFrom: (value: string) => void
  onTo: (value: string) => void
}) {
  return (
    <>
      <DatePicker label="Sanadan" value={dateFrom} onChange={onFrom} max={dateTo || undefined} clearable />
      <DatePicker label="Sanagacha" value={dateTo} onChange={onTo} min={dateFrom || undefined} clearable />
    </>
  )
}

/**
 * Tahlillar jadvali — ro'yxat, o'qituvchi profili va o'qituvchining o'z sahifasi uchun
 * bitta belgilash. Keng ekranda jadval (o'z konteynerida suriladi), telefonda ro'yxat.
 */
export function ObservationTable({
  items,
  linkTo,
  showTeacher = false,
  showObserver = true,
  showStatus = true,
}: {
  items: LaObservationListItem[]
  linkTo: (item: LaObservationListItem) => string
  showTeacher?: boolean
  showObserver?: boolean
  showStatus?: boolean
}) {
  const navigate = useNavigate()
  const th = 'px-3 py-3 font-medium'
  return (
    <Card className="overflow-hidden p-0">
      <div className="hidden overflow-x-auto sm:block">
        <table className="w-full text-left text-sm">
          <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
            <tr>
              {showTeacher && <th className={cn(th, 'pl-5')}>O‘qituvchi</th>}
              <th className={cn(th, !showTeacher && 'pl-5')}>Sana</th>
              <th className={th}>Fan</th>
              <th className={th}>Sinf</th>
              {showObserver && <th className={th}>Kuzatuvchi</th>}
              <th className={cn(th, 'text-right')}>Natija</th>
              <th className={cn(th, 'text-right')}>Foiz</th>
              {showStatus && <th className={th}>Holat</th>}
              <th className="w-10 px-3 py-3">
                <span className="sr-only">Ochish</span>
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {items.map((item) => (
              <tr
                key={item.id}
                tabIndex={0}
                onClick={() => navigate(linkTo(item))}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') navigate(linkTo(item))
                }}
                className="cursor-pointer transition-colors hover:bg-slate-50/70 focus-visible:bg-slate-50 focus-visible:outline-none"
              >
                {showTeacher && <td className="py-3 pl-5 pr-3 font-medium text-slate-800">{item.teacherName}</td>}
                <td className={cn('whitespace-nowrap py-3 pr-3 tabular-nums text-slate-600', showTeacher ? 'pl-3' : 'pl-5 font-medium text-slate-800')}>
                  {fmtDate(item.lessonDate)}
                </td>
                <td className="px-3 py-3 text-slate-600">{item.subject || '—'}</td>
                <td className="px-3 py-3 text-slate-600">{item.className || '—'}</td>
                {showObserver && <td className="px-3 py-3 text-slate-600">{item.observerName}</td>}
                <td className="px-3 py-3 text-right tabular-nums text-slate-600">
                  {item.scoreDone}/{item.scoreTotal}
                </td>
                <td className="px-3 py-3 text-right">
                  <Score percent={item.scorePercent} />
                </td>
                {showStatus && (
                  <td className="px-3 py-3">
                    <StatusChips status={item.status} hasAi={item.hasAiSummary} />
                  </td>
                )}
                <td className="px-3 py-3 text-right">
                  <ChevronRight className="inline h-4 w-4 text-slate-300" />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <ul className="divide-y divide-slate-100 sm:hidden">
        {items.map((item) => (
          <li key={item.id}>
            <Link to={linkTo(item)} className="flex items-center gap-3 px-4 py-3.5 transition-colors hover:bg-slate-50">
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <p className="truncate font-medium text-slate-800">
                    {showTeacher ? item.teacherName : fmtDate(item.lessonDate)}
                  </p>
                  {showStatus && item.status === 'draft' && (
                    <span className="rounded-md bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">Qoralama</span>
                  )}
                  {showStatus && item.hasAiSummary && (
                    <span className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">AI xulosa</span>
                  )}
                </div>
                <p className="mt-0.5 truncate text-xs text-slate-400">
                  {[
                    showTeacher ? fmtDate(item.lessonDate) : null,
                    item.subject || 'Fan yo‘q',
                    item.className || '—',
                    showObserver ? item.observerName : null,
                  ]
                    .filter(Boolean)
                    .join(' · ')}
                </p>
              </div>
              <div className="shrink-0 text-right">
                <Score percent={item.scorePercent} />
                <p className="mt-1 text-[11px] tabular-nums text-slate-400">
                  {item.scoreDone}/{item.scoreTotal}
                </p>
              </div>
              <ChevronRight className="h-4 w-4 shrink-0 text-slate-300" />
            </Link>
          </li>
        ))}
      </ul>
    </Card>
  )
}
