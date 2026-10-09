/**
 * Moliya → Abonementlar analitikasi (mijoz, 2026-10-09): "umumiy sinflar ro'yxati abonimetlar
 * turlari bo'yicha analitika hisoboti tursin, va sinfni bosib kirganda o'quvchilar bo'yicha qaysi
 * aboniment kimda qanaqa borligi jadvali chiqadigan bo'lsin".
 *
 * Ikki jadval: abonement turlari (butun maktab) va sinflar × abonementlar. Sinf qatori shu sinf
 * sahifasiga olib boradi, davr bilan birga; katak — o'sha abonement filtri bilan. Kartalar va
 * abonement qatorlari filtr: bosilsa o'sha o'quvchilar ro'yxati ochiladi (`?list=`). Excel — to'liq
 * hisobot. Hamma raqam serverdan tayyor keladi.
 */
import { useCallback, useMemo, useState, type ReactNode } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { AlertTriangle, ChevronRight, FileSpreadsheet, Layers, Users, Wallet } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import { useAuth } from '@/context/auth-context'
import { hasFinanceAccess } from '@/pages/admin/billing/access'
import {
  ARCHIVED,
  NO_CLASS,
  NO_PLAN,
  downloadSubscriptionAnalytics,
  getSubscriptionAnalytics,
  type SubscriptionAnalytics,
  type SubscriptionFilter,
  type SubscriptionPlanCell,
  type SubscriptionPlanColumn,
} from '@/api/services/subscriptionAnalytics'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { cn, formatMoney } from '@/lib/utils'
import { ReportState } from '../ReportState'
import { SubscriptionPeriodBar } from './SubscriptionPeriodBar'
import { SubscriptionStudentsPanel } from './SubscriptionStudentsPanel'
import { useSubscriptionPeriod } from './subscriptionPeriod'
import { classPath, compactMoney, groupByCategory } from './subscriptionFormat'

type Metric = 'students' | 'billed' | 'paid'

const METRICS: { key: Metric; label: string }[] = [
  { key: 'students', label: "O'quvchilar" },
  { key: 'billed', label: 'Hisoblangan' },
  { key: 'paid', label: "To'langan" },
]

export default function SubscriptionAnalyticsPage() {
  const { user } = useAuth()
  const allowed = hasFinanceAccess(user)
  const periodState = useSubscriptionPeriod()
  const { period, search } = periodState
  const navigate = useNavigate()
  const [metric, setMetric] = useState<Metric>('students')
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)

  // Ochiq ro'yxat URL'da (`?list=no-tuition`) — yangilansa ham, havola yuborilsa ham saqlanadi.
  const [params, setParams] = useSearchParams()
  const list = params.get('list') as SubscriptionFilter | null
  const setList = useCallback(
    (next: SubscriptionFilter | null) =>
      setParams(
        (prev) => {
          const p = new URLSearchParams(prev)
          if (next && next !== prev.get('list')) p.set('list', next)
          else p.delete('list')
          return p
        },
        { replace: true },
      ),
    [setParams],
  )

  const { data, loading, error, refetch } = useAsync<SubscriptionAnalytics | null>(
    () => (allowed ? getSubscriptionAnalytics(period.from, period.to) : Promise.resolve(null)),
    [allowed, period.from, period.to],
  )

  // Davrda hech kim yo'q va pul yo'q abonement ustun bo'lib turmaydi (eski, yopilgan tariflar).
  const columns = useMemo(() => {
    if (!data) return [] as SubscriptionPlanColumn[]
    const used = new Set(data.planTotals.filter((t) => t.students > 0 || t.billed !== 0).map((t) => t.planId))
    return data.plans.filter((p) => used.has(p.planId))
  }, [data])
  const groups = useMemo(() => groupByCategory(columns), [columns])
  const totalByPlan = useMemo(() => new Map((data?.planTotals ?? []).map((t) => [t.planId, t])), [data])

  if (!allowed) {
    return (
      <Card>
        <p className="py-12 text-center text-slate-400">Bu bo'limga ruxsatingiz yo'q.</p>
      </Card>
    )
  }

  const open = (classId: string) => navigate(classPath(classId, search))
  const listMeta = describe(list, data?.plans ?? [])

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Abonementlar analitikasi</h1>
          <p className="mt-0.5 text-sm text-slate-500">
            Davrda kim qaysi abonementda bo'lgani va shu davr oylari uchun qancha hisoblangani. Sinfni bosing — o'quvchilar
            ro'yxati.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <SubscriptionPeriodBar state={periodState} />
          <Button
            variant="secondary"
            disabled={exporting || !data}
            onClick={() => {
              setExporting(true)
              setExportError(null)
              downloadSubscriptionAnalytics({ from: period.from, to: period.to })
                .catch((e: unknown) => setExportError(e instanceof Error ? e.message : 'Xatolik'))
                .finally(() => setExporting(false))
            }}
            title="Sinflar, abonement turlari va barcha o'quvchilar — bitta faylda"
          >
            <FileSpreadsheet className="h-4 w-4" /> {exporting ? 'Tayyorlanmoqda…' : 'Excel'}
          </Button>
        </div>
      </div>
      {exportError && <p className="text-sm text-red-600">{exportError}</p>}

      <ReportState
        loading={loading}
        error={error}
        isEmpty={!!data && data.classes.length === 0}
        emptyTitle="Bu davrda abonement yo'q"
        emptyHint="Boshqa davrni tanlab ko'ring."
        onRetry={refetch}
      >
        {data && (
          <>
            <div className="grid grid-cols-2 gap-3 lg:grid-cols-5">
              <Pick active={list === 'all'}>
                <StatCard
                  label="O'quvchilar"
                  value={data.students}
                  icon={Users}
                  hint="maktabda, hozir"
                  onClick={() => setList('all')}
                />
              </Pick>
              <Pick active={list === 'subscribed'}>
                <StatCard
                  label="Abonementi bor"
                  value={data.withSubscription}
                  icon={Layers}
                  iconBg="bg-sky-50"
                  iconColor="text-sky-600"
                  hint={period.label}
                  onClick={() => setList('subscribed')}
                />
              </Pick>
              <Pick active={list === 'no-tuition'}>
                <StatCard
                  label="O'qish abonementisiz"
                  value={data.withoutTuition}
                  icon={AlertTriangle}
                  iconBg={data.withoutTuition > 0 ? 'bg-amber-50' : 'bg-slate-50'}
                  iconColor={data.withoutTuition > 0 ? 'text-amber-600' : 'text-slate-400'}
                  hint="bosing — kimlar ekani"
                  onClick={() => setList('no-tuition')}
                />
              </Pick>
              <Pick active={list === 'billed'}>
                <StatCard
                  label="Hisoblangan"
                  value={formatMoney(data.billed)}
                  icon={Wallet}
                  iconBg="bg-emerald-50"
                  iconColor="text-emerald-600"
                  hint={`to'langan ${formatMoney(data.paid)}`}
                  onClick={() => setList('billed')}
                />
              </Pick>
              <Pick active={list === 'debt'}>
                <StatCard
                  label="Qarz"
                  value={formatMoney(data.debt)}
                  icon={Wallet}
                  iconBg={data.debt > 0 ? 'bg-red-50' : 'bg-slate-50'}
                  iconColor={data.debt > 0 ? 'text-red-600' : 'text-slate-400'}
                  hint="bosing — qarzdorlar"
                  onClick={() => setList('debt')}
                />
              </Pick>
            </div>

            {list && listMeta && (
              <SubscriptionStudentsPanel
                filter={list}
                title={listMeta.title}
                hint={listMeta.hint}
                from={period.from}
                to={period.to}
                search={search}
                onClose={() => setList(null)}
              />
            )}

            <Card className="p-0">
              <div className="px-5 pt-5">
                <h2 className="font-semibold text-slate-800">Abonement turlari</h2>
                <p className="text-xs text-slate-400">Butun maktab bo'yicha, {period.label}. Qatorni bosing — shu abonementdagi o'quvchilar.</p>
              </div>
              <div className="mt-3 overflow-x-auto">
                <table className="w-full min-w-[44rem] text-sm tabular-nums">
                  <thead className="bg-slate-50 text-xs text-slate-400">
                    <tr>
                      <th className="px-5 py-2 text-left font-medium">Abonement</th>
                      <th className="px-3 py-2 text-right font-medium">Oylik narx</th>
                      <th className="px-3 py-2 text-left font-medium">O'quvchilar</th>
                      <th className="px-3 py-2 text-right font-medium">Hisoblangan</th>
                      <th className="px-3 py-2 text-right font-medium">To'langan</th>
                      <th className="px-5 py-2 text-right font-medium">Qarz</th>
                    </tr>
                  </thead>
                  <tbody>
                    {groups.map((g) => {
                      const max = Math.max(1, ...g.plans.map((p) => totalByPlan.get(p.planId)?.students ?? 0))
                      return [
                        <tr key={`h-${g.key}`}>
                          <td colSpan={6} className="border-t border-slate-100 px-5 pb-1 pt-3 text-xs font-semibold uppercase tracking-wide text-slate-400">
                            {g.name}
                          </td>
                        </tr>,
                        ...g.plans.map((p) => {
                          const t = totalByPlan.get(p.planId)
                          const students = t?.students ?? 0
                          return (
                            <tr
                              key={p.planId}
                              onClick={() => setList(`plan:${p.planId}`)}
                              className={cn(
                                'cursor-pointer hover:bg-brand-50/40',
                                list === `plan:${p.planId}` && 'bg-brand-50/70',
                              )}
                            >
                              <td className="px-5 py-2 text-slate-800">
                                {p.name}
                                {!p.isActive && <span className="ml-2 text-xs text-slate-400">(yopilgan)</span>}
                                {p.planId === NO_PLAN && (
                                  <div className="text-xs text-slate-400">faktura bor, lekin o'sha oyni qamragan abonement yo'q</div>
                                )}
                              </td>
                              <td className="px-3 py-2 text-right text-slate-500">
                                {p.monthlyAmount ? formatMoney(p.monthlyAmount) : '—'}
                              </td>
                              <td className="px-3 py-2">
                                <div className="flex items-center gap-2">
                                  <span className="w-8 text-right font-semibold text-slate-800">{students}</span>
                                  <span className="h-1.5 w-28 overflow-hidden rounded-full bg-slate-100">
                                    <span
                                      className="block h-full rounded-full bg-brand-400"
                                      style={{ width: `${(students / max) * 100}%` }}
                                    />
                                  </span>
                                </div>
                              </td>
                              <td className="px-3 py-2 text-right text-slate-700">{formatMoney(t?.billed ?? 0)}</td>
                              <td className="px-3 py-2 text-right text-emerald-700">{formatMoney(t?.paid ?? 0)}</td>
                              <td className={cn('px-5 py-2 text-right', (t?.debt ?? 0) > 0 ? 'font-semibold text-red-600' : 'text-slate-400')}>
                                {formatMoney(t?.debt ?? 0)}
                              </td>
                            </tr>
                          )
                        }),
                      ]
                    })}
                  </tbody>
                </table>
              </div>
            </Card>

            <Card className="p-0">
              <div className="flex flex-wrap items-center justify-between gap-3 px-5 pt-5">
                <div>
                  <h2 className="font-semibold text-slate-800">Sinflar kesimida</h2>
                  <p className="text-xs text-slate-400">Katakda — shu abonementdagi {METRICS.find((m) => m.key === metric)?.label.toLowerCase()}</p>
                </div>
                <div role="group" aria-label="Katakda" className="flex rounded-xl bg-slate-100 p-1">
                  {METRICS.map((m) => (
                    <button
                      key={m.key}
                      type="button"
                      aria-pressed={metric === m.key}
                      onClick={() => setMetric(m.key)}
                      className={cn(
                        'rounded-lg px-3 py-1 text-xs font-medium transition-colors',
                        metric === m.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                      )}
                    >
                      {m.label}
                    </button>
                  ))}
                </div>
              </div>
              <div className="mt-3 overflow-x-auto">
                <table className="w-max min-w-full border-separate border-spacing-0 text-sm tabular-nums">
                  <thead className="text-xs text-slate-400">
                    <tr>
                      <th rowSpan={2} className="sticky left-0 z-10 border-b border-slate-100 bg-slate-50 px-5 py-2 text-left font-medium">
                        Sinf
                      </th>
                      <th rowSpan={2} className="border-b border-slate-100 bg-slate-50 px-3 py-2 text-right font-medium">O'quvchi</th>
                      {groups.map((g) => (
                        <th
                          key={g.key}
                          colSpan={g.plans.length}
                          className="border-b border-l border-slate-100 bg-slate-50 px-3 pt-2 text-center font-semibold uppercase tracking-wide"
                        >
                          {g.name}
                        </th>
                      ))}
                      <th rowSpan={2} className="border-b border-l border-slate-100 bg-slate-50 px-3 py-2 text-right font-medium" title="Davrda o'qish abonementi yo'q">
                        O'qishsiz
                      </th>
                      <th rowSpan={2} className="border-b border-slate-100 bg-slate-50 px-3 py-2 text-right font-medium">Hisoblangan</th>
                      <th rowSpan={2} className="border-b border-slate-100 bg-slate-50 px-3 py-2 text-right font-medium">To'langan</th>
                      <th rowSpan={2} className="border-b border-slate-100 bg-slate-50 px-3 py-2 text-right font-medium">Qarz</th>
                      <th rowSpan={2} className="border-b border-slate-100 bg-slate-50 px-2" />
                    </tr>
                    <tr>
                      {groups.flatMap((g) =>
                        g.plans.map((p, i) => (
                          <th
                            key={p.planId}
                            className={cn(
                              'max-w-[9rem] border-b border-slate-100 bg-slate-50 px-3 pb-2 pt-1 text-right font-medium text-slate-500',
                              i === 0 && 'border-l',
                            )}
                            title={p.monthlyAmount ? `${p.name} — ${formatMoney(p.monthlyAmount)} / oy` : p.name}
                          >
                            <span className="line-clamp-2">{p.name}</span>
                          </th>
                        )),
                      )}
                    </tr>
                  </thead>
                  <tbody>
                    {data.classes.map((row) => {
                      const cells = new Map(row.plans.map((c) => [c.planId, c]))
                      const special = row.classId === NO_CLASS || row.classId === ARCHIVED
                      return (
                        <tr
                          key={row.classId}
                          onClick={() => open(row.classId)}
                          className={cn('group cursor-pointer hover:bg-brand-50/40', special && 'text-slate-500')}
                        >
                          <td className="sticky left-0 z-10 border-b border-slate-50 bg-white px-5 py-2 text-left group-hover:bg-brand-50/40">
                            <Link
                              to={classPath(row.classId, search)}
                              onClick={(e) => e.stopPropagation()}
                              className={cn('font-medium hover:underline', special ? 'text-slate-500 italic' : 'text-slate-800')}
                            >
                              {row.className}
                            </Link>
                          </td>
                          <td className="border-b border-slate-50 px-3 py-2 text-right text-slate-700">{row.students}</td>
                          {groups.flatMap((g) =>
                            g.plans.map((p, i) => {
                              const cell = cells.get(p.planId)
                              return (
                                <td
                                  key={p.planId}
                                  onClick={(e) => {
                                    if (!cell?.students) return
                                    e.stopPropagation()
                                    navigate(classPath(row.classId, search, p.planId))
                                  }}
                                  title={cell?.students ? `${row.className} — ${p.name}: o'quvchilarni ko'rish` : undefined}
                                  className={cn(
                                    'border-b border-slate-50 px-3 py-2 text-right',
                                    i === 0 && 'border-l border-l-slate-100',
                                    !!cell?.students && 'hover:bg-brand-100/60',
                                  )}
                                >
                                  <MetricCell cell={cell} metric={metric} />
                                </td>
                              )
                            }),
                          )}
                          <td
                            className={cn(
                              'border-b border-l border-slate-50 border-l-slate-100 px-3 py-2 text-right',
                              row.withoutTuition > 0 ? 'font-semibold text-amber-600' : 'text-slate-300',
                            )}
                          >
                            {row.classId === ARCHIVED ? '' : row.withoutTuition || '—'}
                          </td>
                          <td className="border-b border-slate-50 px-3 py-2 text-right text-slate-700" title={formatMoney(row.billed)}>
                            {compactMoney(row.billed)}
                          </td>
                          <td className="border-b border-slate-50 px-3 py-2 text-right text-emerald-700" title={formatMoney(row.paid)}>
                            {compactMoney(row.paid)}
                          </td>
                          <td
                            className={cn('border-b border-slate-50 px-3 py-2 text-right', row.debt > 0 ? 'font-semibold text-red-600' : 'text-slate-300')}
                            title={formatMoney(row.debt)}
                          >
                            {row.debt ? compactMoney(row.debt) : '—'}
                          </td>
                          <td className="border-b border-slate-50 px-2 text-slate-300 group-hover:text-brand-500">
                            <ChevronRight className="h-4 w-4" />
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                  <tfoot className="font-semibold text-slate-800">
                    <tr>
                      <td className="sticky left-0 z-10 bg-slate-50 px-5 py-2.5 text-left">Jami</td>
                      <td className="bg-slate-50 px-3 py-2.5 text-right" title="Maktabdagi o'quvchilar">{data.students}</td>
                      {groups.flatMap((g) =>
                        g.plans.map((p, i) => {
                          const t = totalByPlan.get(p.planId)
                          return (
                            <td key={p.planId} className={cn('bg-slate-50 px-3 py-2.5 text-right', i === 0 && 'border-l border-slate-100')}>
                              <MetricCell cell={t && { planId: p.planId, students: t.students, billed: t.billed, paid: t.paid }} metric={metric} />
                            </td>
                          )
                        }),
                      )}
                      <td className="border-l border-slate-100 bg-slate-50 px-3 py-2.5 text-right text-amber-600">{data.withoutTuition || '—'}</td>
                      <td className="bg-slate-50 px-3 py-2.5 text-right" title={formatMoney(data.billed)}>{compactMoney(data.billed)}</td>
                      <td className="bg-slate-50 px-3 py-2.5 text-right text-emerald-700" title={formatMoney(data.paid)}>{compactMoney(data.paid)}</td>
                      <td className={cn('bg-slate-50 px-3 py-2.5 text-right', data.debt > 0 && 'text-red-600')} title={formatMoney(data.debt)}>
                        {compactMoney(data.debt)}
                      </td>
                      <td className="bg-slate-50" />
                    </tr>
                  </tfoot>
                </table>
              </div>
            </Card>
          </>
        )}
      </ReportState>
    </div>
  )
}

function MetricCell({ cell, metric }: { cell: SubscriptionPlanCell | undefined; metric: Metric }) {
  const value = cell ? cell[metric] : 0
  if (!value) return <span className="text-slate-300">—</span>
  if (metric === 'students') return <span className="font-semibold">{value}</span>
  return (
    <span className={metric === 'paid' ? 'text-emerald-700' : undefined} title={formatMoney(value)}>
      {compactMoney(value)}
    </span>
  )
}

/** Tanlangan karta — atrofida halqa. */
function Pick({ active, children }: { active: boolean; children: ReactNode }) {
  return <div className={cn('rounded-2xl', active && 'ring-2 ring-brand-400 ring-offset-2')}>{children}</div>
}

/** Ochiq ro'yxatning sarlavhasi va izohi. */
function describe(list: string | null, plans: SubscriptionPlanColumn[]): { title: string; hint?: string } | null {
  if (!list) return null
  switch (list) {
    case 'all':
      return { title: "Maktabdagi o'quvchilar", hint: 'Sinflar tartibida' }
    case 'subscribed':
      return { title: "Abonementi bor o'quvchilar", hint: 'Davrda kamida bitta abonementi bor' }
    case 'no-tuition':
      return {
        title: "O'qish abonementi biriktirilmagan o'quvchilar",
        hint: "Davrda o'qish toifasidagi abonementi yo'q — biriktirish kerak bo'lishi mumkin",
      }
    case 'billed':
      return { title: "Hisoblangan o'quvchilar", hint: 'Davr oylariga hisob-faktura chiqqanlar' }
    case 'debt':
      return { title: 'Qarzdorlar', hint: "Davr oylari bo'yicha, qarzi kattasidan boshlab" }
  }
  if (list.startsWith('plan:')) {
    const plan = plans.find((p) => p.planId === list.slice(5))
    if (!plan) return null
    return plan.planId === NO_PLAN
      ? { title: 'Abonement ko\'rsatilmagan hisoblar', hint: "Shu oylarga faktura bor, lekin uni qamragan abonement yo'q" }
      : { title: `${plan.categoryName}: ${plan.name}`, hint: "Davrda shu abonementda bo'lgan o'quvchilar" }
  }
  return null
}
