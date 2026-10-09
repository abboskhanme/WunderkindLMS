/**
 * Abonementlar analitikasi → bitta sinf: har o'quvchining davrdagi abonementlari, toifalar
 * ustunlarida (o'qish, avtobus, yotoqxona, …), va shu davr oylari bo'yicha pul holati.
 * Davr ro'yxat sahifasidan URL orqali keladi va o'zgartirilsa ro'yxatga qaytganda ham saqlanadi.
 */
import { useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ArrowLeft, Users, Wallet } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import { useAuth } from '@/context/auth-context'
import { hasFinanceAccess } from '@/pages/admin/billing/access'
import {
  NO_PLAN,
  getSubscriptionClass,
  type SubscriptionClassDetail,
  type SubscriptionStudentPlan,
} from '@/api/services/subscriptionAnalytics'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { cn, formatMoney } from '@/lib/utils'
import { ReportState } from '../ReportState'
import { SubscriptionPeriodBar } from './SubscriptionPeriodBar'
import { useSubscriptionPeriod } from './subscriptionPeriod'
import { SUBSCRIPTION_ANALYTICS_PATH, compactMoney, shortDate } from './subscriptionFormat'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/** Toifa ustuni. */
interface CategoryColumn {
  code: string
  name: string
}

export default function SubscriptionClassPage() {
  const { classId = '' } = useParams()
  const { user } = useAuth()
  const allowed = hasFinanceAccess(user)
  const periodState = useSubscriptionPeriod()
  const { period, search } = periodState
  const [query, setQuery] = useState('')
  const [planFilter, setPlanFilter] = useState('')

  const { data, loading, error, refetch } = useAsync<SubscriptionClassDetail | null>(
    () => (allowed ? getSubscriptionClass(classId, period.from, period.to) : Promise.resolve(null)),
    [allowed, classId, period.from, period.to],
  )

  // Ustunlar — sinfda uchragan toifalar, server bergan abonement tartibida (o'qish birinchi).
  const categories = useMemo<CategoryColumn[]>(() => {
    if (!data) return []
    const seen = new Set(data.students.flatMap((s) => s.subscriptions.map((x) => x.categoryCode)))
    const cols: CategoryColumn[] = []
    for (const p of data.plans) {
      if (p.planId === NO_PLAN || !seen.has(p.categoryCode) || cols.some((c) => c.code === p.categoryCode)) continue
      cols.push({ code: p.categoryCode, name: p.categoryName })
    }
    if (!cols.some((c) => c.code === 'tuition')) {
      const tuition = data.plans.find((p) => p.categoryCode === 'tuition')
      cols.unshift({ code: 'tuition', name: tuition?.categoryName ?? "O'qish" })
    }
    return cols
  }, [data])

  // Filtr ro'yxati: sinfda kamida bitta o'quvchisi bor abonementlar, soni bilan.
  const planOptions = useMemo(() => {
    if (!data) return []
    const count = new Map<string, number>()
    for (const s of data.students)
      for (const id of new Set(s.subscriptions.map((x) => x.planId))) count.set(id, (count.get(id) ?? 0) + 1)
    return data.plans.filter((p) => count.has(p.planId)).map((p) => ({ ...p, count: count.get(p.planId) ?? 0 }))
  }, [data])

  const visible = useMemo(() => {
    if (!data) return []
    const q = query.trim().toLowerCase()
    return data.students.filter((s) => {
      if (q && !s.fullName.toLowerCase().includes(q)) return false
      if (planFilter === 'no-tuition') return !s.subscriptions.some((x) => x.categoryCode === 'tuition')
      if (planFilter) return s.subscriptions.some((x) => x.planId === planFilter)
      return true
    })
  }, [data, query, planFilter])

  const withoutTuition = useMemo(
    () => (data?.students ?? []).filter((s) => !s.subscriptions.some((x) => x.categoryCode === 'tuition')).length,
    [data],
  )

  if (!allowed) {
    return (
      <Card>
        <p className="py-12 text-center text-slate-400">Bu bo'limga ruxsatingiz yo'q.</p>
      </Card>
    )
  }

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <Link
            to={`${SUBSCRIPTION_ANALYTICS_PATH}${search}`}
            className="inline-flex items-center gap-1 text-sm text-slate-500 hover:text-brand-600"
          >
            <ArrowLeft className="h-4 w-4" /> Abonementlar analitikasi
          </Link>
          <h1 className="mt-1 text-xl font-semibold text-slate-800">{data?.className ?? 'Sinf'}</h1>
          <p className="mt-0.5 text-sm text-slate-500">Har o'quvchining abonementlari, {period.label}</p>
        </div>
        <SubscriptionPeriodBar state={periodState} />
      </div>

      <ReportState
        loading={loading}
        error={error}
        isEmpty={!!data && data.students.length === 0}
        emptyTitle="Bu sinfda o'quvchi yo'q"
        onRetry={refetch}
      >
        {data && (
          <>
            <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
              <StatCard
                label="O'quvchilar"
                value={data.students.length}
                icon={Users}
                hint={withoutTuition ? `${withoutTuition} tasida o'qish abonementi yo'q` : undefined}
              />
              <StatCard
                label="Hisoblangan"
                value={formatMoney(data.billed)}
                icon={Wallet}
                iconBg="bg-sky-50"
                iconColor="text-sky-600"
                hint="davr oylari bo'yicha"
              />
              <StatCard
                label="To'langan"
                value={formatMoney(data.paid)}
                icon={Wallet}
                iconBg="bg-emerald-50"
                iconColor="text-emerald-600"
              />
              <StatCard
                label="Qarz"
                value={formatMoney(data.debt)}
                icon={Wallet}
                iconBg={data.debt > 0 ? 'bg-red-50' : 'bg-slate-50'}
                iconColor={data.debt > 0 ? 'text-red-600' : 'text-slate-400'}
              />
            </div>

            <Card className="p-0">
              <div className="flex flex-wrap items-center gap-3 px-5 pt-5">
                <input
                  type="search"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder="O'quvchi ismi"
                  aria-label="O'quvchi bo'yicha qidirish"
                  className={cn(control, 'min-w-[200px] flex-1 sm:flex-none')}
                />
                <select
                  value={planFilter}
                  onChange={(e) => setPlanFilter(e.target.value)}
                  aria-label="Abonement"
                  className={control}
                >
                  <option value="">Barcha abonementlar</option>
                  {planOptions.map((p) => (
                    <option key={p.planId} value={p.planId}>
                      {p.categoryName} · {p.name} ({p.count})
                    </option>
                  ))}
                  {withoutTuition > 0 && <option value="no-tuition">O'qish abonementisiz ({withoutTuition})</option>}
                </select>
                <span className="text-xs text-slate-400">
                  {visible.length} / {data.students.length} o'quvchi
                </span>
              </div>

              <div className="mt-3 overflow-x-auto">
                <table className="w-full min-w-[48rem] text-sm tabular-nums">
                  <thead className="bg-slate-50 text-xs text-slate-400">
                    <tr>
                      <th className="w-10 px-5 py-2 text-left font-medium">#</th>
                      <th className="px-3 py-2 text-left font-medium">F.I.SH</th>
                      {categories.map((c) => (
                        <th key={c.code} className="px-3 py-2 text-left font-medium">
                          {c.name}
                        </th>
                      ))}
                      <th className="px-3 py-2 text-right font-medium">Hisoblangan</th>
                      <th className="px-3 py-2 text-right font-medium">To'langan</th>
                      <th className="px-5 py-2 text-right font-medium">Qarz</th>
                    </tr>
                  </thead>
                  <tbody>
                    {visible.map((s, i) => (
                      <tr key={s.studentId} className="border-t border-slate-100 align-top hover:bg-slate-50/60">
                        <td className="px-5 py-2.5 text-slate-400">{i + 1}</td>
                        <td className="px-3 py-2.5">
                          <Link
                            to={`/admin/students/${s.studentId}`}
                            className="font-medium text-slate-800 hover:text-brand-600 hover:underline"
                          >
                            {s.fullName}
                          </Link>
                        </td>
                        {categories.map((c) => {
                          const subs = s.subscriptions.filter((x) => x.categoryCode === c.code)
                          return (
                            <td key={c.code} className="px-3 py-2.5">
                              {subs.length === 0 ? (
                                c.code === 'tuition' ? (
                                  <span className="rounded-md bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">
                                    Yo'q
                                  </span>
                                ) : (
                                  <span className="text-slate-300">—</span>
                                )
                              ) : (
                                <div className="space-y-1.5">
                                  {subs.map((x) => (
                                    <PlanChip key={x.subscriptionId} sub={x} from={period.from} to={period.to} />
                                  ))}
                                </div>
                              )}
                            </td>
                          )
                        })}
                        <td className="px-3 py-2.5 text-right text-slate-700" title={formatMoney(s.billed)}>
                          {s.billed ? compactMoney(s.billed) : '—'}
                        </td>
                        <td className="px-3 py-2.5 text-right text-emerald-700" title={formatMoney(s.paid)}>
                          {s.paid ? compactMoney(s.paid) : '—'}
                        </td>
                        <td
                          className={cn('px-5 py-2.5 text-right', s.debt > 0 ? 'font-semibold text-red-600' : 'text-slate-300')}
                          title={formatMoney(s.debt)}
                        >
                          {s.debt ? compactMoney(s.debt) : '—'}
                        </td>
                      </tr>
                    ))}
                    {visible.length === 0 && (
                      <tr>
                        <td colSpan={categories.length + 5} className="px-5 py-10 text-center text-slate-400">
                          Mos o'quvchi topilmadi.
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            </Card>
          </>
        )}
      </ReportState>
    </div>
  )
}

/** Bitta abonement: nomi, oylik narxi va — davrni to'liq qoplamasa — muddati. */
function PlanChip({ sub, from, to }: { sub: SubscriptionStudentPlan; from: string; to: string }) {
  const startsInside = sub.startsOn > from
  const endsInside = sub.endsOn !== null && sub.endsOn < to
  const term =
    startsInside && endsInside
      ? `${shortDate(sub.startsOn)} – ${shortDate(sub.endsOn!)}`
      : startsInside
        ? `${shortDate(sub.startsOn)} dan`
        : endsInside
          ? `${shortDate(sub.endsOn!)} gacha`
          : null
  const title = [
    `${sub.planName} — ${formatMoney(sub.monthlyAmount)} / oy`,
    `Muddat: ${shortDate(sub.startsOn)} – ${sub.endsOn ? shortDate(sub.endsOn) : 'ochiq'}`,
    sub.detail ? `Izoh: ${sub.detail}` : null,
    `Davrda hisoblangan: ${formatMoney(sub.billed)}, to'langan: ${formatMoney(sub.paid)}`,
  ]
    .filter(Boolean)
    .join('\n')

  return (
    <div title={title} className="rounded-lg bg-slate-50 px-2.5 py-1.5 ring-1 ring-slate-100">
      <div className="text-sm font-medium leading-tight text-slate-800">{sub.planName}</div>
      <div className="mt-0.5 flex flex-wrap gap-x-2 text-xs text-slate-500">
        <span>{compactMoney(sub.monthlyAmount)} / oy</span>
        {term && <span className="text-sky-700">{term}</span>}
        {sub.detail && <span className="max-w-[12rem] truncate text-slate-400">{sub.detail}</span>}
      </div>
    </div>
  )
}
