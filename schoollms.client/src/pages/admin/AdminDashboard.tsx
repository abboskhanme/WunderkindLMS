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
import { Pager } from '@/components/ui/Pager'
import { PAGE_SIZES } from '@/lib/pagination'
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

  // QAT'IY QOIDA (mijoz, 2026-09-22): har bir STAT karta — qaysi guruhdan bo'lishidan
  // qat'i nazar — faqat shu yuqori qatorda chiqadi. Pastda faqat jadval va grafik
  // bloklari turadi. Yangi karta qo'shilsa, u shu ro'yxatga qo'shiladi.
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
    on('gender') && <GenderCard key="gender" stats={stats} classes={classHeadcounts} />,
    on('classBreakdown') && <ClassBreakdownCard key="classBreakdown" classes={classHeadcounts} />,
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

      {on('attendanceByPeriod') && <AttendanceByPeriodCard rows={attendanceByPeriod} />}
      {on('absentStudents') && <AbsentStudentsCard rows={absentStudents} />}


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
    active ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
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
  // EduSchool kabi: 10 ta dars soati DOIM ko'rinadi, darsi yo'q soat — 0 va 0%.
  // Server har doim 1..10 ni qaytaradi; bo'sh kelsa ham jadval bo'sh qolmaydi.
  const byPeriod = new Map(rows.map((r) => [r.period, r]))
  const all = Array.from({ length: 10 }, (_, i) => {
    const period = i + 1
    return byPeriod.get(period) ?? { period, expected: 0, present: 0, absent: 0, unchecked: 0 }
  })

  return (
    <Card>
      <h2 className="mb-3 font-semibold text-slate-800">Davomat analitikasi</h2>
      <div className="overflow-x-auto">
        {/* table-fixed: 8 ta ustun teng kenglikda — raqamlar bir-birining ostida turadi. */}
        <table className="w-full min-w-[760px] table-fixed text-sm">
          <thead className="whitespace-nowrap">
            <tr className="border-b border-slate-100 text-left text-xs uppercase tracking-wide text-slate-400">
              <th className="truncate pb-2 pr-3 font-medium" title="Dars vaqti">Dars vaqti</th>
              <th className="truncate pb-2 pr-3 font-medium" title="O'quvchilar soni">O'quvchilar soni</th>
              <th className="truncate pb-2 pr-3 font-medium" title="Kelganlar soni">Kelganlar soni</th>
              <th className="truncate pb-2 pr-3 font-medium" title="Kelganlar foizi">Kelganlar foizi</th>
              <th className="truncate pb-2 pr-3 font-medium" title="Kelmaganlar soni">Kelmaganlar soni</th>
              <th className="truncate pb-2 pr-3 font-medium" title="Kelmaganlar foizi">Kelmaganlar foizi</th>
              <th className="truncate pb-2 pr-3 font-medium" title="Tekshirilmaganlar soni">Tekshirilmaganlar soni</th>
              <th className="truncate pb-2 font-medium" title="Tekshirilmaganlar foizi">Tekshirilmaganlar foizi</th>
            </tr>
          </thead>
          <tbody className="tabular-nums">
            {all.map((r) => {
              const pct = (n: number) => `${r.expected > 0 ? Math.round((n / r.expected) * 100) : 0}%`
              const idle = r.expected === 0
              return (
                <tr
                  key={r.period}
                  className={cn('border-b border-slate-50 last:border-0', idle && 'text-slate-400')}
                >
                  <td className="py-2 pr-3 font-medium text-slate-700">{r.period}</td>
                  <td className="py-2 pr-3">{r.expected}</td>
                  <td className={cn('py-2 pr-3', !idle && 'text-emerald-700')}>{r.present}</td>
                  <td className="py-2 pr-3">{pct(r.present)}</td>
                  <td className={cn('py-2 pr-3', !idle && r.absent > 0 && 'text-red-600')}>{r.absent}</td>
                  <td className="py-2 pr-3">{pct(r.absent)}</td>
                  <td className={cn('py-2 pr-3', r.unchecked > 0 && 'text-amber-600')}>{r.unchecked}</td>
                  <td className="py-2">{pct(r.unchecked)}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
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
  const [pageSize, setPageSize] = useState<number>(PAGE_SIZES[0])
  const [page, setPage] = useState(1)
  const lastPage = Math.max(1, Math.ceil(shown.length / pageSize))
  const current = Math.min(page, lastPage)
  const pageRows = shown.slice((current - 1) * pageSize, current * pageSize)

  return (
    <Card>
      <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
        <h2 className="font-semibold text-slate-800">Dars qoldirayotgan o'quvchilar</h2>
        <div className="flex gap-1 rounded-xl bg-slate-100 p-1">
          {MISS_FILTERS.map((f) => (
            <button
              key={f.key}
              type="button"
              onClick={() => {
                setFilter(f.key)
                setPage(1)
              }}
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
          {/* table-fixed: ism ustuni qolganlarini o'ngga surib yubormasin. */}
          <table className="w-full min-w-[560px] table-fixed text-sm">
            <colgroup>
              <col className="w-[40%]" />
              <col className="w-[20%]" />
              <col className="w-[20%]" />
              <col className="w-[20%]" />
            </colgroup>
            <thead className="whitespace-nowrap">
              <tr className="border-b border-slate-100 text-left text-xs uppercase tracking-wide text-slate-400">
                <th className="pb-2 pr-3 font-medium">O'quvchi</th>
                <th className="pb-2 pr-3 font-medium">Sinf</th>
                <th className="pb-2 pr-3 font-medium">Qoldirgan kun</th>
                <th className="pb-2 font-medium">Oxirgi kelgan</th>
              </tr>
            </thead>
            <tbody>
              {pageRows.map((r) => (
                <tr key={r.studentId} className="border-b border-slate-50 last:border-0">
                  <td className="py-2 pr-3 text-slate-700">
                      <span className="block truncate" title={r.fullName}>
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
          <div className="mt-2 -mx-5 -mb-5">
            <Pager
              total={shown.length}
              page={current}
              lastPage={lastPage}
              pageSize={pageSize}
              onPage={setPage}
              onPageSize={(n) => {
                setPageSize(n)
                setPage(1)
              }}
            />
          </div>
        </div>
      )}
    </Card>
  )
}
