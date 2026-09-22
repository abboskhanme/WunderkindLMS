/**
 * Bosh sahifaning "Vidjetlar" panelidan yoqiladigan qo'shimcha bloklar
 * (EduSchool ro'yxati, 2026-09-22).
 *
 * Har biri O'Z ma'lumotini o'zi oladi va faqat yoqilganda chiziladi — shuning
 * uchun o'chiq vidjet serverga so'rov ham yubormaydi. Pul raqamlari yangi
 * hisob-kitob emas: har biri moliya bo'limidagi mavjud hisobotdan olinadi,
 * shu sabab bosh sahifa va moliya ekrani hech qachon bir-biriga zid chiqmaydi.
 */
import type { ReactNode } from 'react'
import { ArrowDownCircle, ArrowUpCircle, Landmark } from 'lucide-react'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { AdminStats, ClassHeadcount } from '@/types'
import { useAsync } from '@/hooks/useAsync'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { CashFlowChart } from '@/components/charts/CashFlowChart'
import { getFinanceDashboard } from '@/api/services/financeStatements'
import { getCashBoxes } from '@/api/services/cashBoxes'
import {
  getCashFlow,
  getCollectionRate,
  getDebtors,
  getRevenueExpectation,
  type CashFlowMonth,
} from '@/api/services/financeReports'
import { getLeadFunnel } from '@/api/services/leadFunnel'
import { getPayrollAdjustments } from '@/api/services/payrollAdjustments'
import { cn, formatMoney } from '@/lib/utils'
import { chartColors, formatMonthLabel, shortAmount } from '@/pages/admin/finance/reportLabels'

/* ---------- sana yordamchilari ---------- */

const pad = (n: number) => String(n).padStart(2, '0')
const iso = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
const todayIso = () => iso(new Date())
/** `back` oy oldingi oyning birinchi kuni. */
const monthStartIso = (back = 0) => {
  const d = new Date()
  return iso(new Date(d.getFullYear(), d.getMonth() - back, 1))
}
const pct = (part: number, whole: number) => (whole > 0 ? Math.round((part / whole) * 100) : 0)

/* ---------- umumiy qobiq ---------- */

function WidgetCard({
  title,
  hint,
  loading,
  error,
  children,
  className,
}: {
  title: string
  hint?: string
  loading?: boolean
  error?: string | null
  children?: ReactNode
  className?: string
}) {
  return (
    <Card className={className}>
      <h2 className="font-semibold text-slate-800">{title}</h2>
      {hint && <p className="text-xs text-slate-400">{hint}</p>}
      <div className="mt-3">
        {loading ? (
          <p className="py-6 text-center text-sm text-slate-400">Yuklanmoqda...</p>
        ) : error ? (
          <p className="py-6 text-center text-sm text-red-600">{error}</p>
        ) : (
          children
        )}
      </div>
    </Card>
  )
}

/** Bir qator: nom — qiymat, ostida ulush chizig'i. */
function ShareRow({
  label,
  value,
  share,
  barClass = 'bg-brand-500',
}: {
  label: string
  value: ReactNode
  share: number
  barClass?: string
}) {
  return (
    <li className="py-1.5">
      <div className="flex items-baseline justify-between gap-3 text-sm">
        <span className="truncate text-slate-600" title={label}>
          {label}
        </span>
        <span className="shrink-0 font-medium tabular-nums text-slate-800">{value}</span>
      </div>
      <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-slate-100">
        <div className={cn('h-full rounded-full', barClass)} style={{ width: `${Math.min(100, share)}%` }} />
      </div>
    </li>
  )
}

function Figure({ label, value, tone }: { label: string; value: string; tone?: string }) {
  return (
    <div className="min-w-0 rounded-xl bg-slate-50 px-3 py-2">
      <p className="truncate text-xs text-slate-500">{label}</p>
      <p className={cn('truncate text-base font-semibold tabular-nums text-slate-800', tone)} title={value}>
        {value}
      </p>
    </div>
  )
}

/* =========================================================================
   Moliya — kichik kartalar (yuqoridagi to'rga tushadi)
   ========================================================================= */

/** Bugungi kirim yoki chiqim — "Moliya hisobotlari" panelining bugungi kuni. */
export function TodayMoneyStat({ kind }: { kind: 'in' | 'out' }) {
  const { data, error } = useAsync(() => getFinanceDashboard(todayIso(), todayIso()), [])
  const value = error ? '—' : data ? formatMoney(kind === 'in' ? data.inflow.current : data.outflow.current) : '...'
  return kind === 'in' ? (
    <StatCard label="Bugungi kirim" value={value} icon={ArrowDownCircle} iconBg="bg-emerald-50" iconColor="text-emerald-600" hint={error ?? undefined} />
  ) : (
    <StatCard label="Bugungi chiqim" value={value} icon={ArrowUpCircle} iconBg="bg-red-50" iconColor="text-red-600" hint={error ?? undefined} />
  )
}

/** Faol kassalar qoldig'ining yig'indisi. */
export function CashBalanceStat() {
  const { data, error } = useAsync(getCashBoxes, [])
  const active = (data ?? []).filter((b) => b.isActive)
  const total = active.reduce((s, b) => s + b.balance, 0)
  return (
    <StatCard
      label="Kassa balansi"
      value={error ? '—' : data ? formatMoney(total) : '...'}
      icon={Landmark}
      iconBg="bg-sky-50"
      iconColor="text-sky-600"
      hint={error ? error : data ? `${active.length} ta kassa` : undefined}
    />
  )
}

/* =========================================================================
   Moliya — bloklar
   ========================================================================= */

/** Oxirgi 6 oy: hisoblangan va yig'ilgan (hisob-faktura oyi bo'yicha). */
export function DebtDynamicsCard() {
  const { data, loading, error } = useAsync(() => getCollectionRate(monthStartIso(5), todayIso()), [])
  const rows = (data ?? []).map((m) => ({
    name: formatMonthLabel(m.periodMonth),
    "Hisoblangan": m.accrued,
    "Yig'ilgan": m.collected,
    Qarz: Math.max(0, m.accrued - m.collected),
  }))
  return (
    <WidgetCard title="Qarzdorlik dinamikasi" hint="Oxirgi 6 oy — har oyga hisoblangan va shu oy uchun yig'ilgan" loading={loading} error={error}>
      {rows.length === 0 ? (
        <p className="py-6 text-center text-sm text-slate-400">Ma'lumot yo'q.</p>
      ) : (
        <div className="h-64">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={rows} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#f1f5f9" vertical={false} />
              <XAxis dataKey="name" tick={{ fontSize: 12, fill: '#64748b' }} axisLine={false} tickLine={false} />
              <YAxis tickFormatter={shortAmount} tick={{ fontSize: 12, fill: '#64748b' }} axisLine={false} tickLine={false} width={56} />
              <Tooltip formatter={(v) => formatMoney(Number(v))} />
              <Legend wrapperStyle={{ fontSize: 12 }} />
              <Bar dataKey="Yig'ilgan" fill={chartColors.inflow} radius={[4, 4, 0, 0]} />
              <Bar dataKey="Qarz" fill={chartColors.outflow} radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}
    </WidgetCard>
  )
}

/** Oxirgi 6 oyning pul oqimi — "Pul oqimi" hisobotining o'zi, kassa + bank. */
export function MonthlyFlowCard() {
  const { data, loading, error } = useAsync(() => getCashFlow(monthStartIso(5), todayIso()), [])
  const byMonth = new Map<string, CashFlowMonth>()
  for (const acc of data?.accounts ?? []) {
    for (const m of acc.months) {
      const prev = byMonth.get(m.month)
      byMonth.set(
        m.month,
        prev
          ? {
              month: m.month,
              opening: prev.opening + m.opening,
              inflow: prev.inflow + m.inflow,
              outflow: prev.outflow + m.outflow,
              net: prev.net + m.net,
              closing: prev.closing + m.closing,
            }
          : { ...m },
      )
    }
  }
  const months = [...byMonth.values()].sort((a, b) => a.month.localeCompare(b.month))
  return (
    <WidgetCard title="Oylik kirim-chiqim" hint="Oxirgi 6 oy, kassa va bank birga" loading={loading} error={error}>
      {months.length === 0 ? (
        <p className="py-6 text-center text-sm text-slate-400">Ma'lumot yo'q.</p>
      ) : (
        <CashFlowChart months={months} />
      )}
    </WidgetCard>
  )
}

/** Joriy oy: kutilgan, yig'ilgan, qolgan — P&L 2.0 ning shu oyi. */
export function FinancialStateCard() {
  const { data, loading, error } = useAsync(() => getRevenueExpectation(), [])
  const rate = data?.collectionRateForPeriod
  return (
    <WidgetCard title="Moliyaviy holat" hint="Joriy oy" loading={loading} error={error}>
      {data && (
        <>
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
            <Figure label="Kutilgan tushum" value={formatMoney(data.netExpected)} />
            <Figure label="Yig'ilgan" value={formatMoney(data.collectedForPeriod)} tone="text-emerald-700" />
            <Figure label="Qolgan" value={formatMoney(data.outstandingForPeriod)} tone="text-red-600" />
            <Figure label="Haqiqiy daromad" value={formatMoney(data.revenueActual)} />
            <Figure label="Xarajat" value={formatMoney(data.expenseActual)} />
            <Figure
              label="Foyda"
              value={formatMoney(data.profitActual)}
              tone={data.profitActual < 0 ? 'text-red-600' : 'text-emerald-700'}
            />
          </div>
          <div className="mt-3">
            <div className="flex justify-between text-xs text-slate-500">
              <span>Yig'ilish darajasi</span>
              <span className="tabular-nums">{rate == null ? '—' : `${rate}%`}</span>
            </div>
            <div className="mt-1 h-2 overflow-hidden rounded-full bg-slate-100">
              <div className="h-full rounded-full bg-emerald-500" style={{ width: `${Math.min(100, rate ?? 0)}%` }} />
            </div>
          </div>
        </>
      )}
    </WidgetCard>
  )
}

const OVERDUE_BUCKETS = [
  { label: "Muddati o'tmagan", test: (d: number) => d <= 0, bar: 'bg-amber-300' },
  { label: '1–30 kun', test: (d: number) => d >= 1 && d <= 30, bar: 'bg-amber-500' },
  { label: '31–60 kun', test: (d: number) => d >= 31 && d <= 60, bar: 'bg-orange-500' },
  { label: '61–90 kun', test: (d: number) => d >= 61 && d <= 90, bar: 'bg-red-500' },
  { label: '90 kundan ortiq', test: (d: number) => d > 90, bar: 'bg-red-700' },
]

/** Jami qarz va uning necha kun kechikkani bo'yicha taqsimoti. */
export function DebtStateCard() {
  const { data, loading, error } = useAsync(() => getDebtors({ includeArchived: false }), [])
  const rows = data ?? []
  const total = rows.reduce((s, r) => s + r.debt, 0)
  return (
    <WidgetCard title="Qarzdorlik holati" hint="Maktabdagi o'quvchilar, kechikish muddati bo'yicha" loading={loading} error={error}>
      {data && (
        <>
          <div className="grid grid-cols-2 gap-2">
            <Figure label="Jami qarz" value={formatMoney(total)} tone="text-red-600" />
            <Figure label="Qarzdorlar" value={`${rows.length} ta`} />
          </div>
          <ul className="mt-2">
            {OVERDUE_BUCKETS.map((b) => {
              const mine = rows.filter((r) => b.test(r.daysOverdue))
              const sum = mine.reduce((s, r) => s + r.debt, 0)
              return (
                <ShareRow
                  key={b.label}
                  label={`${b.label} · ${mine.length} ta`}
                  value={formatMoney(sum)}
                  share={pct(sum, total)}
                  barClass={b.bar}
                />
              )
            })}
          </ul>
        </>
      )}
    </WidgetCard>
  )
}

/** Abonementi bor o'quvchilar ulushi — joriy oy. */
export function CoverageCard({ stats }: { stats: AdminStats }) {
  const { data, loading, error } = useAsync(() => getRevenueExpectation(), [])
  const covered = data?.studentsActive ?? 0
  const share = pct(covered, stats.studentsCount)
  return (
    <WidgetCard title="Abonement qamrovi" hint="Joriy oyda faol abonementi bor o'quvchilar" loading={loading} error={error}>
      {data && (
        <>
          <p className="text-3xl font-semibold tabular-nums text-slate-800">{share}%</p>
          <p className="text-sm text-slate-500">
            {covered} / {stats.studentsCount} o'quvchi
          </p>
          <div className="mt-3 h-2 overflow-hidden rounded-full bg-slate-100">
            <div className="h-full rounded-full bg-brand-500" style={{ width: `${Math.min(100, share)}%` }} />
          </div>
          <p className="mt-2 text-xs text-slate-400">
            Abonementsiz: {Math.max(0, stats.studentsCount - covered)} ta
          </p>
        </>
      )}
    </WidgetCard>
  )
}

/* =========================================================================
   Lidlar, boshqaruv
   ========================================================================= */

export function LeadFunnelCard() {
  const { data, loading, error } = useAsync(() => getLeadFunnel(), [])
  const stages = [...(data?.stages ?? [])].sort((a, b) => a.order - b.order)
  const top = Math.max(1, ...stages.map((s) => s.reachedCount))
  return (
    <WidgetCard
      title="Lidlar voronkasi"
      hint={data ? `Jami ${data.allTimeLeads} ta lid · o'quvchiga aylangan: ${data.enrolledCount}` : undefined}
      loading={loading}
      error={error}
    >
      {stages.length === 0 ? (
        <p className="py-6 text-center text-sm text-slate-400">Bosqichlar yo'q.</p>
      ) : (
        <ul>
          {stages.map((s) => (
            <ShareRow
              key={s.stageId}
              label={`${s.title} · hozir ${s.currentCount}`}
              value={s.reachedCount}
              share={pct(s.reachedCount, top)}
            />
          ))}
        </ul>
      )}
    </WidgetCard>
  )
}

/** Joriy oydagi bonus va jarima — storno qilinganlari chiqarilgan. */
export function BonusPenaltyCard() {
  const now = new Date()
  const { data, loading, error } = useAsync(
    () => getPayrollAdjustments({ periodYear: now.getFullYear(), periodMonth: now.getMonth() + 1 }),
    [],
  )
  const live = (data ?? []).filter((a) => !a.reversed && !a.reversalOf)
  const bonus = live.filter((a) => a.kind === 'bonus').reduce((s, a) => s + a.amount, 0)
  const penalty = live.filter((a) => a.kind === 'penalty').reduce((s, a) => s + a.amount, 0)
  const total = bonus + penalty
  return (
    <WidgetCard title="Bonus / Jarima ulushi" hint="Joriy oy" loading={loading} error={error}>
      {data && (
        <ul>
          <ShareRow label="Bonus" value={formatMoney(bonus)} share={pct(bonus, total)} barClass="bg-emerald-500" />
          <ShareRow label="Jarima" value={formatMoney(penalty)} share={pct(penalty, total)} barClass="bg-red-500" />
        </ul>
      )}
    </WidgetCard>
  )
}

/* =========================================================================
   Kontingent
   ========================================================================= */

/** Sinf darajalari bo'yicha: 1-sinflar, 2-sinflar ... + sinfsizlar. */
export function ContingentCard({ stats, classes }: { stats: AdminStats; classes: ClassHeadcount[] }) {
  const byGrade = new Map<number, number>()
  for (const c of classes) byGrade.set(c.grade, (byGrade.get(c.grade) ?? 0) + c.studentsCount)
  const grades = [...byGrade.entries()].sort((a, b) => a[0] - b[0])
  const total = stats.studentsCount
  return (
    <WidgetCard title="Kontingent tarkibi" hint={`Jami ${total} ta faol o'quvchi`}>
      <ul>
        {grades.map(([grade, n]) => (
          <ShareRow
            key={grade}
            label={grade === 0 ? 'Maktabgacha' : `${grade}-sinflar`}
            value={`${n} · ${pct(n, total)}%`}
            share={pct(n, total)}
          />
        ))}
        {stats.unassignedCount > 0 && (
          <ShareRow
            label="Sinfsiz"
            value={`${stats.unassignedCount} · ${pct(stats.unassignedCount, total)}%`}
            share={pct(stats.unassignedCount, total)}
            barClass="bg-amber-400"
          />
        )}
      </ul>
    </WidgetCard>
  )
}

export function GenderCard({ stats, classes }: { stats: AdminStats; classes: ClassHeadcount[] }) {
  if (typeof stats.maleCount !== 'number' || typeof stats.femaleCount !== 'number') {
    return <WidgetCard title="Jins bo'yicha" error="Server bu ma'lumotni hali bermayapti." />
  }
  const total = stats.maleCount + stats.femaleCount
  const male = pct(stats.maleCount, total)

  // Sinf darajalari kesimida — qaysi parallelda muvozanat buzilganini ko'rsatadi.
  const byGrade = new Map<number, { male: number; female: number }>()
  for (const c of classes) {
    const g = byGrade.get(c.grade) ?? { male: 0, female: 0 }
    g.male += c.maleCount
    g.female += c.femaleCount
    byGrade.set(c.grade, g)
  }
  const grades = [...byGrade.entries()].sort((x, y) => x[0] - y[0])

  return (
    <WidgetCard title="Jins bo'yicha" hint="Faol o'quvchilar">
      <div className="grid grid-cols-2 gap-2">
        <Figure label="O'g'il bolalar" value={`${stats.maleCount} · ${male}%`} tone="text-sky-700" />
        <Figure label="Qiz bolalar" value={`${stats.femaleCount} · ${total > 0 ? 100 - male : 0}%`} tone="text-pink-600" />
      </div>
      <SplitBar male={stats.maleCount} female={stats.femaleCount} className="mt-3 h-2" />

      {grades.length > 0 && (
        <>
          <p className="mt-5 text-[11px] font-semibold uppercase tracking-wide text-slate-400">
            Sinf darajalari bo'yicha
          </p>
          <ul className="mt-1">
            {grades.map(([grade, g]) => (
              <li key={grade} className="grid grid-cols-[6.5rem_1fr_4.5rem] items-center gap-3 py-1.5 text-sm">
                <span className="text-slate-600">{grade === 0 ? 'Maktabgacha' : `${grade}-sinflar`}</span>
                <SplitBar male={g.male} female={g.female} className="h-1.5" />
                <span className="text-right tabular-nums text-slate-500">
                  <span className="text-sky-700">{g.male}</span> / <span className="text-pink-600">{g.female}</span>
                </span>
              </li>
            ))}
          </ul>
        </>
      )}
    </WidgetCard>
  )
}

/** O'g'il (ko'k) va qiz (pushti) ulushi bitta chiziqda. */
function SplitBar({ male, female, className }: { male: number; female: number; className?: string }) {
  const total = male + female
  return (
    <div className={cn('flex overflow-hidden rounded-full bg-slate-100', className)}>
      {total > 0 && (
        <>
          <div className="h-full bg-sky-500" style={{ width: `${pct(male, total)}%` }} />
          <div className="h-full flex-1 bg-pink-400" />
        </>
      )}
    </div>
  )
}

export function ClassBreakdownCard({ classes }: { classes: ClassHeadcount[] }) {
  const rows = [...classes].sort((a, b) => a.grade - b.grade || a.className.localeCompare(b.className, 'uz'))
  return (
    <WidgetCard title="Sinflar kesimi" hint={`${rows.length} ta sinf`}>
      {rows.length === 0 ? (
        <p className="py-6 text-center text-sm text-slate-400">Sinflar yo'q.</p>
      ) : (
        <div className="max-h-80 overflow-auto">
          <table className="w-full table-fixed text-sm">
            <thead className="sticky top-0 whitespace-nowrap bg-white">
              <tr className="border-b border-slate-100 text-left text-xs uppercase tracking-wide text-slate-400">
                <th className="pb-2 pr-3 font-medium">Sinf</th>
                <th className="pb-2 pr-3 font-medium">O'quvchi</th>
                <th className="pb-2 font-medium">O'g'il / qiz</th>
              </tr>
            </thead>
            <tbody className="tabular-nums">
              {rows.map((c) => (
                <tr key={c.classId} className="border-b border-slate-50 last:border-0">
                  <td className="py-2 pr-3 font-medium text-slate-700">{c.className}</td>
                  <td className="py-2 pr-3 text-slate-600">{c.studentsCount}</td>
                  <td className="py-2 text-slate-500">
                    {c.maleCount} / {c.femaleCount}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </WidgetCard>
  )
}
