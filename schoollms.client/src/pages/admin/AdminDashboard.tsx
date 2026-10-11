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
import { DashboardPeriodBar } from './DashboardPeriodBar'
import { displayDate, useDashboardPeriod } from './dashboardPeriod'

export function AdminDashboard() {
  const { user } = useAuth()
  const billing = useBillingAccess()
  const canLeads = !user?.permissions || user.permissions.includes('leads')
  const access = useMemo(() => ({ finance: billing.canOpen, leads: canLeads }), [billing.canOpen, canLeads])
  const widgets = useDashboardWidgets(user?.id ?? 'anon', access)
  const [pickerOpen, setPickerOpen] = useState(false)
  // Davr filtri butun sahifa uchun bitta (mijoz, 2026-10-08) — dashboardPeriod.ts.
  const periodState = useDashboardPeriod()
  const { period } = periodState
  const { data, loading, error } = useAsync(
    () => getAdminDashboard(period.from, period.to),
    [period.from, period.to],
  )
  const [metric, setMetric] = useState<Metric>('grade')

  // Sarlavha va filtr doim ko'rinadi: davr almashganda sahifa "Yuklanmoqda" ga sakramaydi,
  // eski raqamlar xira holda turadi va yangilari kelgach almashadi.
  const header = (
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Bosh sahifa</h1>
        <p className="text-sm text-slate-400">
          Maktab bo'yicha umumiy ko'rsatkichlar ·{' '}
          {period.singleDay ? displayDate(period.from) : `${displayDate(period.from)} – ${displayDate(period.to)}`}
        </p>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <DashboardPeriodBar state={periodState} />
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
    </div>
  )

  if (error || !data) {
    return (
      <div className="space-y-6">
        {header}
        {error ? <p className="text-red-600">Xatolik: {error}</p> : <Loader label="Yuklanmoqda..." />}
        {pickerOpen && <WidgetPickerDrawer widgets={widgets} onClose={() => setPickerOpen(false)} />}
      </div>
    )
  }

  const { stats, classPerformance, topClasses } = data
  const on = widgets.isOn
  // Server eski bo'lsa (deploy oralig'ida) yangi maydonlar kelmaydi — "undefined"
  // yoki bo'sh karta o'rniga "—".
  const num = (v: number | null | undefined) => (typeof v === 'number' ? v : '—')

  // O'rtacha baho bo'yicha eng yuqori 5 ta sinf (davrda bahosi yo'q sinf — reytingda emas)
  const ranked = [...topClasses]
    .filter((c) => c.averageGrade > 0)
    .sort((a, b) => b.averageGrade - a.averageGrade)
    .slice(0, 5)

  // Statistik kartalar — EduSchool bilan bir xil to'plam (docs/MENU-PARITY.md).
  // Pul bilan bog'liq uchtasi HISOBLANADI: `students.balance` ustuni P1-21 da
  // o'chirilgan, chunki u haqiqatdan ajralib ketgan edi.
  const classHeadcounts = data.classHeadcounts ?? []

  // QAT'IY QOIDA (mijoz, 2026-09-22): har bir STAT karta — qaysi guruhdan bo'lishidan
  // qat'i nazar — faqat shu yuqori qatorda chiqadi. Pastda faqat jadval va grafik
  // bloklari turadi. Yangi karta qo'shilsa, u shu ro'yxatga qo'shiladi.
  const topCards = [
    on('students') && (
      <StatCard
        key="students"
        to="/admin/students"
        label="Jami o'quvchilar"
        value={stats.studentsCount.toLocaleString()}
        icon={Users}
      />
    ),
    on('unassigned') && (
      <StatCard
        key="unassigned"
        to="/admin/students?placement=unassigned"
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
        to="/admin/students?placement=leftFromClass"
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
        to="/admin/classes"
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
        to="/admin/students?placement=inClass"
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
        to="/admin/students?placement=waiting"
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
        to="/admin/students/arxiv"
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
        to="/admin/students?balance=credit"
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
        to="/admin/finance/debtors"
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
        to="/admin/students?firstPayment=ever"
        label="Birinchi to'lov qilganlar"
        value={stats.paidAtLeastOnceCount}
        icon={BadgeCheck}
        iconBg="bg-violet-50"
        iconColor="text-violet-600"
        hint={
          typeof stats.firstPaymentInPeriodCount === 'number'
            ? `${period.label}: ${stats.firstPaymentInPeriodCount}`
            : typeof stats.firstPaymentThisMonthCount === 'number'
              ? `shu oyda: ${stats.firstPaymentThisMonthCount}`
              : undefined
        }
      />
    ),
    on('todayIncome') && <TodayMoneyStat key="todayIncome" kind="in" period={period} />,
    on('todayExpense') && <TodayMoneyStat key="todayExpense" kind="out" period={period} />,
    on('cashBalance') && <CashBalanceStat key="cashBalance" />,
    on('teachers') && (
      <StatCard
        key="teachers"
        to="/admin/teachers"
        label="O'qituvchilar"
        value={stats.teachersCount}
        icon={GraduationCap}
        iconBg="bg-indigo-50"
        iconColor="text-indigo-600"
      />
    ),
    // Davrda birorta baho qo'yilmagan bo'lsa server 0 qaytaradi — "0.0" emas, "—".
    on('averageGrade') && (
      <StatCard
        key="averageGrade"
        to="/admin/grades-report/school"
        label="O'rtacha baho"
        value={stats.averageGrade > 0 ? stats.averageGrade.toFixed(1) : '—'}
        icon={Star}
        iconBg="bg-amber-50"
        iconColor="text-amber-600"
        hint={`5 ballik tizim · ${period.label}`}
      />
    ),
    on('attendanceRate') && (
      <StatCard
        key="attendanceRate"
        to="/admin/attendance/analytics"
        label="Umumiy davomat"
        value={stats.attendanceRate == null ? '—' : `${stats.attendanceRate}%`}
        icon={CalendarCheck}
        iconBg="bg-emerald-50"
        iconColor="text-emerald-600"
        hint={period.label}
      />
    ),
  ].filter(Boolean)

  // Panel tartibida — EduSchool ro'yxati bo'yicha.
  const blocks = [
    on('debtDynamics') && <DebtDynamicsCard key="debtDynamics" period={period} />,
    on('monthlyFlow') && <MonthlyFlowCard key="monthlyFlow" period={period} />,
    on('financialState') && <FinancialStateCard key="financialState" period={period} />,
    on('debtState') && <DebtStateCard key="debtState" />,
    on('coverage') && <CoverageCard key="coverage" stats={stats} period={period} />,
    on('leadFunnel') && <LeadFunnelCard key="leadFunnel" />,
    on('bonusPenalty') && <BonusPenaltyCard key="bonusPenalty" period={period} />,
    on('contingent') && <ContingentCard key="contingent" stats={stats} classes={classHeadcounts} />,
    on('gender') && <GenderCard key="gender" stats={stats} classes={classHeadcounts} />,
    on('classBreakdown') && <ClassBreakdownCard key="classBreakdown" classes={classHeadcounts} />,
  ].filter(Boolean)


  const showChart = on('classChart')
  const showTop = on('topClasses')

  return (
    <div className="space-y-6">
      {header}

      <div className={cn('space-y-6 transition-opacity', loading && 'pointer-events-none opacity-60')}>

      {widgets.shownCount === 0 && (
        <Card className="py-10 text-center text-sm text-slate-400">
          Barcha vidjetlar o'chirilgan. «Vidjetlar» tugmasi orqali keraklilarini yoqing.
        </Card>
      )}

      {topCards.length > 0 && (
        <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">{topCards}</div>
      )}

      {/* Masonry: bloklar balandligi har xil — ustunlarga zich terilsin, orada bo'shliq qolmasin. */}
      {blocks.length > 0 && (
        <div className="gap-4 xl:columns-2">
          {blocks.map((b, i) => (
            // Blokning o'z kaliti: vidjet o'chirilsa qolganlari qayta yuklanmasin.
            <div key={(b as { key?: string | null }).key ?? i} className="mb-4 break-inside-avoid">
              {b}
            </div>
          ))}
        </div>
      )}



      {/* Davr oxirgi kunining dars jadvali (barcha sinflar) — "Bugun/Bu hafta/Bu oy" da bugungisi */}
      {on('todaySchedule') && <TodaySchedule date={period.to} today={period.today} />}

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
              <p className="mb-4 text-xs text-slate-400">O'rtacha baho bo'yicha Top 5 · {period.label}</p>
              {ranked.length === 0 && (
                <p className="py-6 text-center text-sm text-slate-400">Bu davrda baho qo'yilmagan.</p>
              )}
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

      </div>

      {pickerOpen && <WidgetPickerDrawer widgets={widgets} onClose={() => setPickerOpen(false)} />}
    </div>
  )
}

function toggleBtn(active: boolean): string {
  return cn(
    'rounded-md px-3 py-1 font-medium transition-colors',
    active ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
  )
}
