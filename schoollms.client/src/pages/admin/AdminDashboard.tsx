import { useMemo, useState } from 'react'
import {
  Users,
  GraduationCap,
  Star,
  CalendarCheck,
  School,
  UserMinus,
  Archive,
  Wallet,
  AlertTriangle,
  BadgeCheck,
  SlidersHorizontal,
  UserX,
  UserCheck,
  Hourglass,
} from 'lucide-react'
import type { AbsentStudent, AttendanceByPeriod } from '@/types'
import { getAdminDashboard } from '@/api/services/dashboard'
import { useAsync } from '@/hooks/useAsync'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import {
  ClassPerformanceChart,
  type Metric,
} from '@/components/charts/ClassPerformanceChart'
import { cn } from '@/lib/utils'
import { TodaySchedule } from './TodaySchedule'
import { useAuth } from '@/context/auth-context'
import { useDashboardWidgets } from './dashboardWidgets'
import { useBillingAccess } from './billing/access'
import {
  BonusPenaltyCard,
  CashBalanceStat,
  ClassBreakdownCard,
  ContingentCard,
  CoverageCard,
  DebtDynamicsCard,
  DebtStateCard,
  FinancialStateCard,
  GenderCard,
  LeadFunnelCard,
  MonthlyFlowCard,
  TodayMoneyStat,
} from './DashboardExtraWidgets'
import { WidgetPickerDrawer } from './WidgetPickerDrawer'

export function AdminDashboard() {
  const { user } = useAuth()
  const billing = useBillingAccess()
  const canLeads = !user?.permissions || user.permissions.includes('leads')
  const access = useMemo(() => ({ finance: billing.canOpen, leads: canLeads }), [billing.canOpen, canLeads])
  const widgets = useDashboardWidgets(user?.id ?? 'anon', access)
  const [pickerOpen, setPickerOpen] = useState(false)
  const { data, loading, error } = useAsync(getAdminDashboard, [])
  const [metric, setMetric] = useState<Metric>('grade')

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (error) return <p className="text-red-600">Xatolik: {error}</p>
  if (!data) return null

  const { stats, classPerformance, topClasses, attendanceByPeriod, absentStudents } = data
  const on = widgets.isOn
  // Server eski bo'lsa (deploy oralig'ida) yangi maydonlar kelmaydi — "undefined"
  // yoki bo'sh karta o'rniga "—".
  const num = (v: number | null | undefined) => (typeof v === 'number' ? v : '—')

  // O'rtacha baho bo'yicha eng yuqori 5 ta sinf
  const ranked = [...topClasses]
    .sort((a, b) => b.averageGrade - a.averageGrade)
    .slice(0, 5)

  // Statistik kartalar — EduSchool bilan bir xil to'plam (docs/MENU-PARITY.md).
  // Pul bilan bog'liq uchtasi HISOBLANADI: `students.balance` ustuni P1-21 da
  // o'chirilgan, chunki u haqiqatdan ajralib ketgan edi.
  const classHeadcounts = data.classHeadcounts ?? []

  const topCards = [
    on('students') && (
      <StatCard key="students" label="Jami o'quvchilar" value={stats.studentsCount.toLocaleString()} icon={Users} />
    ),
    on('unassigned') && (
      <StatCard
        key="unassigned"
        label="Sinfga qo'shilmagan"
        value={stats.unassignedCount}
        icon={UserMinus}
        iconBg="bg-amber-50"
        iconColor="text-amber-600"
      />
    ),
    on('leftFromClass') && (
      <StatCard
        key="leftFromClass"
        label="Sinfdan chiqarilgan"
        value={num(stats.leftFromClassCount)}
        icon={UserX}
        iconBg="bg-rose-50"
        iconColor="text-rose-600"
        hint="boshqa sinfga qo'yilmagan"
      />
    ),
    on('classes') && (
      <StatCard
        key="classes"
        label="Jami sinflar"
        value={stats.classesCount}
        icon={School}
        iconBg="bg-sky-50"
        iconColor="text-sky-600"
      />
    ),
    on('active') && (
      <StatCard
        key="active"
        label="Aktiv o'quvchilar"
        value={num(stats.activeCount)}
        icon={UserCheck}
        iconBg="bg-emerald-50"
        iconColor="text-emerald-600"
        hint="sinfda o'qiyotgan"
      />
    ),
    on('waiting') && (
      <StatCard
        key="waiting"
        label="Kutayotgan o'quvchilar"
        value={num(stats.waitingCount)}
        icon={Hourglass}
        iconBg="bg-amber-50"
        iconColor="text-amber-600"
        hint="qabul qilingan, sinf hal qilinmagan"
      />
    ),
    on('archived') && (
      <StatCard
        key="archived"
        label="Arxiv o'quvchilar"
        value={stats.archivedCount}
        icon={Archive}
        iconBg="bg-slate-100"
        iconColor="text-slate-500"
      />
    ),
    on('credit') && (
      <StatCard
        key="credit"
        label="Haqdorlar"
        value={stats.creditCount}
        icon={Wallet}
        iconBg="bg-emerald-50"
        iconColor="text-emerald-600"
        hint="avansi bor"
      />
    ),
    on('debtors') && (
      <StatCard
        key="debtors"
        label="Qarzdorlar"
        value={stats.debtorCount}
        icon={AlertTriangle}
        iconBg="bg-red-50"
        iconColor="text-red-600"
      />
    ),
    on('firstPayment') && (
      <StatCard
        key="firstPayment"
        label="Birinchi to'lov qilganlar"
        value={stats.paidAtLeastOnceCount}
        icon={BadgeCheck}
        iconBg="bg-violet-50"
        iconColor="text-violet-600"
        hint={
          typeof stats.firstPaymentThisMonthCount === 'number'
            ? `shu oyda: ${stats.firstPaymentThisMonthCount}`
            : undefined
        }
      />
    ),
    on('todayIncome') && <TodayMoneyStat key="todayIncome" kind="in" />,
    on('todayExpense') && <TodayMoneyStat key="todayExpense" kind="out" />,
    on('cashBalance') && <CashBalanceStat key="cashBalance" />,
    on('teachers') && (
      <StatCard
        key="teachers"
        label="O'qituvchilar"
        value={stats.teachersCount}
        icon={GraduationCap}
        iconBg="bg-indigo-50"
        iconColor="text-indigo-600"
      />
    ),
  ].filter(Boolean)

  // Panel tartibida — EduSchool ro'yxati bo'yicha.
  const blocks = [
    on('debtDynamics') && <DebtDynamicsCard key="debtDynamics" />,
    on('monthlyFlow') && <MonthlyFlowCard key="monthlyFlow" />,
    on('financialState') && <FinancialStateCard key="financialState" />,
    on('debtState') && <DebtStateCard key="debtState" />,
    on('coverage') && <CoverageCard key="coverage" stats={stats} />,
    on('leadFunnel') && <LeadFunnelCard key="leadFunnel" />,
    on('bonusPenalty') && <BonusPenaltyCard key="bonusPenalty" />,
    on('contingent') && <ContingentCard key="contingent" stats={stats} classes={classHeadcounts} />,
    on('gender') && <GenderCard key="gender" stats={stats} />,
    on('classBreakdown') && <ClassBreakdownCard key="classBreakdown" classes={classHeadcounts} />,
  ].filter(Boolean)

  const bottomCards = [
    on('averageGrade') && (
      <StatCard
        key="averageGrade"
        label="O'rtacha baho"
        value={stats.averageGrade.toFixed(1)}
        icon={Star}
        iconBg="bg-amber-50"
        iconColor="text-amber-600"
        hint="5 ballik tizim"
      />
    ),
    on('attendanceRate') && (
      <StatCard
        key="attendanceRate"
        label="Umumiy davomat"
        value={stats.attendanceRate == null ? '—' : `${stats.attendanceRate}%`}
        icon={CalendarCheck}
        iconBg="bg-emerald-50"
        iconColor="text-emerald-600"
      />
    ),
  ].filter(Boolean)

  const showChart = on('classChart')
  const showTop = on('topClasses')

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Bosh sahifa</h1>
          <p className="text-sm text-slate-400">Maktab bo'yicha umumiy ko'rsatkichlar</p>
        </div>
        <button
          type="button"
          onClick={() => setPickerOpen(true)}
          className="flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50"
        >
          <SlidersHorizontal className="h-4 w-4" />
          Vidjetlar
          <span className="text-xs text-slate-400">
            {widgets.shownCount}/{widgets.visible.length}
          </span>
        </button>
      </div>

      {widgets.shownCount === 0 && (
        <Card className="py-10 text-center text-sm text-slate-400">
          Barcha vidjetlar o'chirilgan. «Vidjetlar» tugmasi orqali keraklilarini yoqing.
        </Card>
      )}

      {topCards.length > 0 && (
        <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">{topCards}</div>
      )}

      {blocks.length > 0 && <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">{blocks}</div>}

      {on('attendanceByPeriod') && <AttendanceByPeriodCard rows={attendanceByPeriod} />}
      {on('absentStudents') && <AbsentStudentsCard rows={absentStudents} />}

      {bottomCards.length > 0 && (
        <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">{bottomCards}</div>
      )}

      {/* Bugungi dars jadvali (barcha sinflar) */}
      {on('todaySchedule') && <TodaySchedule />}

      {/* Statistika grafigi + reyting */}
      {(showChart || showTop) && (
        <div className="grid grid-cols-1 gap-6 xl:grid-cols-3">
          {/* Grafik (baho / davomat tanlash) */}
          {showChart && (
            <Card className={showTop ? 'xl:col-span-2' : 'xl:col-span-3'}>
              <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
                <h2 className="font-semibold text-slate-800">Sinflar bo'yicha statistika</h2>
                <div className="flex rounded-lg bg-slate-100 p-1 text-sm">
                  <button onClick={() => setMetric('grade')} className={toggleBtn(metric === 'grade')}>
                    O'rtacha baho
                  </button>
                  <button
                    onClick={() => setMetric('attendance')}
                    className={toggleBtn(metric === 'attendance')}
                  >
                    Davomat
                  </button>
                </div>
              </div>
              <ClassPerformanceChart data={classPerformance} metric={metric} />
            </Card>
          )}

          {/* Eng yuqori o'rtacha baholi sinflar (Top 5) */}
          {showTop && (
            <Card className={showChart ? undefined : 'xl:col-span-3'}>
              <h2 className="mb-1 font-semibold text-slate-800">Eng yuqori bahoga ega sinflar</h2>
              <p className="mb-4 text-xs text-slate-400">O'rtacha baho bo'yicha Top 5</p>
              <ul className="space-y-2">
                {ranked.map((c, i) => (
                  <li
                    key={c.id}
                    className="flex items-center gap-3 rounded-xl border border-slate-100 p-3"
                  >
                    <div
                      className={cn(
                        'flex h-9 w-9 shrink-0 items-center justify-center rounded-lg text-sm font-bold',
                        i === 0
                          ? 'bg-amber-100 text-amber-700'
                          : 'bg-slate-100 text-slate-500',
                      )}
                    >
                      {i + 1}
                    </div>
                    <div className="min-w-0 flex-1">
                      <p className="font-medium text-slate-800">{c.name}</p>
                      <p className="text-xs text-slate-400">{c.studentsCount} o'quvchi</p>
                    </div>
                    <div className="flex items-center gap-1 text-amber-600">
                      <Star className="h-4 w-4 fill-amber-400 text-amber-400" />
                      <span className="font-semibold">{c.averageGrade.toFixed(1)}</span>
                    </div>
                  </li>
                ))}
              </ul>
            </Card>
          )}
        </div>
      )}

      {pickerOpen && <WidgetPickerDrawer widgets={widgets} onClose={() => setPickerOpen(false)} />}
    </div>
  )
}

function toggleBtn(active: boolean): string {
  return cn(
    'rounded-md px-3 py-1 font-medium transition-colors',
    active ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
  )
}

/* ==========================================================================
   Bugungi davomat — dars soatlari kesimida
   ========================================================================== */

/**
 * "Tekshirilmagan" ALOHIDA ustun va u "kelgan" ga QO'SHILMAYDI.
 *
 * Eng oson xato shu bo'lardi: davomati belgilanmagan o'quvchini kelgan deb
 * hisoblash. Unda direktor 100% ko'rib, aslida o'sha soatda hech kim davomat
 * qo'ymaganini bilmay qolardi — ya'ni ekran muammoni ko'rsatish o'rniga
 * yashirardi.
 */
function AttendanceByPeriodCard({ rows }: { rows: AttendanceByPeriod[] }) {
  const active = rows.filter((r) => r.expected > 0)

  return (
    <Card>
      <h2 className="mb-3 font-semibold text-slate-800">Davomat analitikasi</h2>
      {active.length === 0 ? (
        <p className="py-6 text-center text-sm text-slate-400">
          Bugun dars yo'q — davomat ham yo'q.
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[640px] text-sm">
            <thead className="whitespace-nowrap">
              <tr className="border-b border-slate-100 text-left text-xs uppercase tracking-wide text-slate-400">
                <th className="pb-2 pr-3 font-medium">Dars vaqti</th>
                <th className="pb-2 pr-3 font-medium">O'quvchilar</th>
                <th className="pb-2 pr-3 font-medium">Kelgan</th>
                <th className="pb-2 pr-3 font-medium">Kelmagan</th>
                <th className="pb-2 font-medium">Tekshirilmagan</th>
              </tr>
            </thead>
            <tbody className="tabular-nums">
              {active.map((r) => {
                const pct = (n: number) =>
                  r.expected > 0 ? `${Math.round((n / r.expected) * 100)}%` : '—'
                return (
                  <tr key={r.period} className="border-b border-slate-50 last:border-0">
                    <td className="py-2 pr-3 font-medium text-slate-700">{r.period}-soat</td>
                    <td className="py-2 pr-3 text-slate-600">{r.expected}</td>
                    <td className="py-2 pr-3 text-emerald-700">
                      {r.present} <span className="text-slate-400">· {pct(r.present)}</span>
                    </td>
                    <td className="py-2 pr-3 text-red-600">
                      {r.absent} <span className="text-slate-400">· {pct(r.absent)}</span>
                    </td>
                    <td className={cn('py-2', r.unchecked > 0 ? 'text-amber-600' : 'text-slate-400')}>
                      {r.unchecked} <span className="text-slate-400">· {pct(r.unchecked)}</span>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  )
}

/* ==========================================================================
   Dars qoldirayotgan o'quvchilar
   ========================================================================== */

const MISS_FILTERS = [
  { key: 'all', label: 'Barchasi', min: 1 },
  { key: '3', label: '3+ sababsiz', min: 3 },
  { key: '5', label: '5+ sababsiz', min: 5 },
] as const

/**
 * Faqat SABABSIZ qoldirilgan kunlar. Kasal bo'lgan yoki ruxsat olgan bola bu
 * ro'yxatga tushmaydi — aks holda ekran intizom muammosi bo'lmagan joyda
 * muammo ko'rsatardi.
 */
function AbsentStudentsCard({ rows }: { rows: AbsentStudent[] }) {
  const [filter, setFilter] = useState<(typeof MISS_FILTERS)[number]['key']>('all')
  const min = MISS_FILTERS.find((f) => f.key === filter)!.min
  const shown = rows.filter((r) => r.missedDays >= min)

  return (
    <Card>
      <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
        <h2 className="font-semibold text-slate-800">Dars qoldirayotgan o'quvchilar</h2>
        <div className="flex gap-1 rounded-xl bg-slate-100 p-1">
          {MISS_FILTERS.map((f) => (
            <button
              key={f.key}
              type="button"
              onClick={() => setFilter(f.key)}
              className={cn(
                'rounded-lg px-3 py-1.5 text-xs font-medium transition-colors',
                filter === f.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500',
              )}
            >
              {f.label}
            </button>
          ))}
        </div>
      </div>

      {shown.length === 0 ? (
        <p className="py-6 text-center text-sm text-slate-400">
          {rows.length === 0
            ? "Oxirgi 30 kunda sababsiz qoldirish qayd etilmagan."
            : 'Bu chegaradan oshgan o‘quvchi yo‘q.'}
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[560px] text-sm">
            <thead className="whitespace-nowrap">
              <tr className="border-b border-slate-100 text-left text-xs uppercase tracking-wide text-slate-400">
                <th className="pb-2 pr-3 font-medium">O'quvchi</th>
                <th className="pb-2 pr-3 font-medium">Sinf</th>
                <th className="pb-2 pr-3 font-medium">Qoldirgan kun</th>
                <th className="pb-2 font-medium">Oxirgi kelgan</th>
              </tr>
            </thead>
            <tbody>
              {shown.slice(0, 20).map((r) => (
                <tr key={r.studentId} className="border-b border-slate-50 last:border-0">
                  <td className="py-2 pr-3 text-slate-700">
                      <span className="block max-w-[14rem] truncate" title={r.fullName}>
                        {r.fullName}
                      </span>
                    </td>
                  <td className="py-2 pr-3 text-slate-500">{r.className || '—'}</td>
                  <td className="py-2 pr-3 font-medium tabular-nums text-red-600">
                    {r.missedDays} kun
                  </td>
                  <td className="py-2 tabular-nums text-slate-500">{r.lastSeen ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {shown.length > 20 && (
            <p className="pt-2 text-xs text-slate-400">
              Yana {shown.length - 20} ta — to'liq ro'yxat davomat bo'limida.
            </p>
          )}
        </div>
      )}
    </Card>
  )
}
