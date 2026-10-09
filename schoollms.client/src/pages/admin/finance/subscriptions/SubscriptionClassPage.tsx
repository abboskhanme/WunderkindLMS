/**
 * Abonementlar analitikasi → bitta sinf: har o'quvchining davrdagi abonementlari, toifalar
 * ustunlarida (o'qish, avtobus, yotoqxona, …), va shu davr oylari bo'yicha pul holati.
 * Davr ro'yxat sahifasidan URL orqali keladi va o'zgartirilsa ro'yxatga qaytganda ham saqlanadi.
 */
import { useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { ArrowLeft, FileSpreadsheet, Users, Wallet } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import { useAuth } from '@/context/auth-context'
import { hasFinanceAccess } from '@/pages/admin/billing/access'
import {
  NO_PLAN,
  downloadSubscriptionAnalytics,
  getSubscriptionClass,
  type SubscriptionClassDetail,
} from '@/api/services/subscriptionAnalytics'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { cn, formatMoney } from '@/lib/utils'
import { ReportState } from '../ReportState'
import { SubscriptionPeriodBar } from './SubscriptionPeriodBar'
import { SubscriptionStudentsTable } from './SubscriptionStudentsTable'
import { useSubscriptionPeriod } from './subscriptionPeriod'
import { SUBSCRIPTION_ANALYTICS_PATH } from './subscriptionFormat'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

export default function SubscriptionClassPage() {
  const { classId = '' } = useParams()
  const { user } = useAuth()
  const allowed = hasFinanceAccess(user)
  const periodState = useSubscriptionPeriod()
  const { period, search } = periodState
  const [query, setQuery] = useState('')
  // Umumiy sahifadagi katakdan kelsa — o'sha abonement bilan ochiladi (`?plan=`).
  const [searchParams] = useSearchParams()
  const [planFilter, setPlanFilter] = useState(() => searchParams.get('plan') ?? '')
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)

  const { data, loading, error, refetch } = useAsync<SubscriptionClassDetail | null>(
    () => (allowed ? getSubscriptionClass(classId, period.from, period.to) : Promise.resolve(null)),
    [allowed, classId, period.from, period.to],
  )

  // Filtr ro'yxati: sinfda kamida bitta o'quvchisi bor abonementlar, soni bilan.
  const planOptions = useMemo(() => {
    if (!data) return []
    const count = new Map<string, number>()
    for (const s of data.students) {
      const ids = new Set(s.subscriptions.map((x) => x.planId))
      if (s.noPlanBilled !== 0) ids.add(NO_PLAN)
      for (const id of ids) count.set(id, (count.get(id) ?? 0) + 1)
    }
    return data.plans.filter((p) => count.has(p.planId)).map((p) => ({ ...p, count: count.get(p.planId) ?? 0 }))
  }, [data])

  const visible = useMemo(() => {
    if (!data) return []
    const q = query.trim().toLowerCase()
    return data.students.filter((s) => {
      if (q && !s.fullName.toLowerCase().includes(q)) return false
      if (planFilter === 'no-tuition') return s.inSchool && !s.subscriptions.some((x) => x.categoryCode === 'tuition')
      if (planFilter === NO_PLAN) return s.noPlanBilled !== 0 || s.subscriptions.some((x) => x.planId === NO_PLAN)
      if (planFilter) return s.subscriptions.some((x) => x.planId === planFilter)
      return true
    })
  }, [data, query, planFilter])

  const withoutTuition = useMemo(
    () =>
      (data?.students ?? []).filter((s) => s.inSchool && !s.subscriptions.some((x) => x.categoryCode === 'tuition'))
        .length,
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
        <div className="flex flex-wrap items-center gap-2">
          <SubscriptionPeriodBar state={periodState} />
          <Button
            variant="secondary"
            disabled={exporting || !data}
            onClick={() => {
              setExporting(true)
              setExportError(null)
              downloadSubscriptionAnalytics({ from: period.from, to: period.to, classId })
                .catch((e: unknown) => setExportError(e instanceof Error ? e.message : 'Xatolik'))
                .finally(() => setExporting(false))
            }}
          >
            <FileSpreadsheet className="h-4 w-4" /> {exporting ? 'Tayyorlanmoqda…' : 'Excel'}
          </Button>
        </div>
      </div>
      {exportError && <p className="text-sm text-red-600">{exportError}</p>}

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

              <div className="mt-3">
                <SubscriptionStudentsTable rows={visible} plans={data.plans} from={period.from} to={period.to} search={search} />
              </div>
            </Card>
          </>
        )}
      </ReportState>
    </div>
  )
}
