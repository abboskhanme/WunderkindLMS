/**
 * MOLIYA → MOLIYA HISOBOTLARI (docs/modules/finance-parity.md §2.4).
 *
 * Direktorning kunlik savoli: "shu davrda qancha pul kirdi, qancha chiqdi,
 * o'tgan davrga nisbatan qanday va u qayerdan kelib qayerga ketdi".
 * Shu paytgacha javob ikki ekranga bo'lingan edi — P&L (daromad TAN
 * OLINGAN kun) va Pul oqimi (oylik jami). Bu ekran uchinchi savolga javob
 * beradi: KUNMA-KUN va TOIFALAR kesimida, har bir raqamning ortiga kirish
 * mumkin.
 *
 * Manba: `GET /api/admin/finance/dashboard` (`FinanceReportQueries
 * .DashboardAsync`). Kirim va chiqim — `ledger_entries` ning `cash` va
 * `bank` hisoblari, ya'ni "Pul oqimi" ekranidagi AYNAN o'sha raqamlar;
 * toifalar esa o'shaning bo'laklari.
 *
 * PULNI FRONTEND HISOBLAMAYDI: KPI, foiz o'zgarishi, bo'lim va qator
 * yakunlari — hammasi serverdan. Bu yerda faqat ULUSH FOIZI hisoblanadi va
 * u ham ko'rsatish uchun (qatorning yonidagi chiziqcha uzunligi).
 *
 * STORNO YASHIRILMAYDI: u o'z kunida chiqim bo'lib turadi (SPEC §4.1 va
 * `CashDayQueries.cs` sarlavhasi). Toifa qatorida "shundan storno" alohida
 * ko'rsatiladi.
 *
 * RUXSAT (SPEC §4.3): faqat `admin` va `superadmin`; kassir 403 oladi.
 */
import { hasFinanceAccess } from '@/pages/admin/billing/access'
import { useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { useSearchParams } from 'react-router-dom'
import {
  ArrowDownRight,
  ArrowUpRight,
  BarChart3,
  FileSpreadsheet,
  TrendingUp,
  Wallet,
} from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import {
  downloadFinanceDashboard,
  getFinanceDashboard,
  type CashFlowCategoryRow,
  type CashFlowSection,
  type FinanceDashboard,
  type FinanceMethodRow,
  type DiscountAnalysis,
} from '@/api/services/financeStatements'
import { useAuth } from '@/context/auth-context'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { StatCard } from '@/components/ui/StatCard'
import { DailyCashChart } from '@/components/charts/DailyCashChart'
import { cn, formatDate, formatMoney } from '@/lib/utils'
import { ReportState } from './ReportState'
import { LedgerDetailsModal, type LedgerDetailsRequest } from './LedgerDetailsModal'
import { formatSignedMoney, signClass } from './reportLabels'
import { DatePicker } from '@/components/ui/DatePicker'


/** SPEC §4.3: moliya hisobotlari faqat admin va direktorga ochiq. */
// Moliya — admin/direktor yoki rolida "Moliya" ruxsati bor xodim (Boshqaruv → Rollar).

const todayStr = new Date().toISOString().slice(0, 10)
const monthStart = `${todayStr.slice(0, 7)}-01`

/** Bo'lim ranglari — tushum yashil, chiqim qizil, qolgani kulrang. */
const sectionTone: Record<string, string> = {
  income: 'bg-emerald-500',
  expense: 'bg-red-500',
  other: 'bg-slate-400',
}

export function FinancialReportsPage() {
  const { user } = useAuth()
  const allowed = hasFinanceAccess(user)

  // "Kassa kuni" dan kelgan havola davrni o'zi beradi (§2.8 F8.01).
  const [params, setParams] = useSearchParams()
  const from = params.get('from') || monthStart
  const to = params.get('to') || todayStr

  const [chartMode, setChartMode] = useState<'bar' | 'line'>('bar')
  const [exporting, setExporting] = useState(false)
  const [details, setDetails] = useState<LedgerDetailsRequest | null>(null)

  const { data, loading, error, refetch } = useAsync<FinanceDashboard | null>(
    () => (allowed ? getFinanceDashboard(from, to) : Promise.resolve(null)),
    [allowed, from, to],
  )

  const setPeriod = (nextFrom: string, nextTo: string) => {
    setParams({ from: nextFrom, to: nextTo }, { replace: true })
  }

  /**
   * Excel (mijoz, 2026-09-19: "yuklab olish csv emas excel fayl uchun
   * bo'lsin"). Fayl serverda yig'iladi: ekrandagi beshala blok ham beshta
   * varaq bo'lib tushadi, ilgari esa faqat toifalar jadvali ketardi.
   */
  const handleExport = async () => {
    if (!data) return
    setExporting(true)
    try {
      await downloadFinanceDashboard(from, to)
    } finally {
      setExporting(false)
    }
  }

  const period = useMemo(() => `${formatDate(from)} — ${formatDate(to)}`, [from, to])

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
          <h1 className="text-xl font-semibold text-slate-800">Moliya hisobotlari</h1>
          <p className="mt-0.5 text-sm text-slate-500">
            {period} — kassa va bank harakati, oldingi davr bilan taqqoslab. Manba: buxgalteriya
            jurnali.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <DatePicker
            value={from}
            onChange={(value: string) => setPeriod(value, to)}
            ariaLabel="Davr boshi"
            className="w-40"
          />
          <span className="text-slate-400">—</span>
          <DatePicker
            value={to}
            onChange={(value: string) => setPeriod(from, value)}
            ariaLabel="Davr oxiri"
            className="w-40"
          />
          <Button variant="secondary" onClick={handleExport} disabled={exporting || !data}>
            <FileSpreadsheet className="h-4 w-4" /> {exporting ? 'Tayyorlanmoqda...' : 'Excel'}
          </Button>
        </div>
      </div>

      <ReportState loading={loading} error={error} onRetry={refetch}>
        {data && (
          <div className="space-y-5">
            {/* --- Uchta KPI, oldingi davr bilan --- */}
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              <StatCard
                label="Kirim"
                value={formatMoney(data.inflow.current)}
                icon={ArrowUpRight}
                iconBg="bg-emerald-50"
                iconColor="text-emerald-600"
                hint={changeHint(data.inflow.changePercent, data.inflow.previous)}
              />
              <StatCard
                label="Chiqim"
                value={formatMoney(data.outflow.current)}
                icon={ArrowDownRight}
                iconBg="bg-red-50"
                iconColor="text-red-600"
                hint={changeHint(data.outflow.changePercent, data.outflow.previous)}
              />
              <StatCard
                label="Qoldiq (kirim − chiqim)"
                value={formatSignedMoney(data.net.current)}
                icon={Wallet}
                iconBg={data.net.current >= 0 ? 'bg-brand-50' : 'bg-red-50'}
                iconColor={data.net.current >= 0 ? 'text-brand-600' : 'text-red-600'}
                hint={`Davr oxiridagi pul: ${formatMoney(data.closingBalance)}`}
              />
            </div>

            <p className="text-xs text-slate-400">
              Taqqoslanayotgan davr: {formatDate(data.previousFrom)} — {formatDate(data.previousTo)}
            </p>

            {/* --- Kunlik grafik --- */}
            <Card>
              <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
                <h2 className="font-semibold text-slate-800">Kunlar bo'yicha</h2>
                <div className="flex items-center gap-1 rounded-lg bg-slate-100 p-1">
                  <ModeButton
                    active={chartMode === 'bar'}
                    onClick={() => setChartMode('bar')}
                    label="Ustun"
                    icon={<BarChart3 className="h-4 w-4" />}
                  />
                  <ModeButton
                    active={chartMode === 'line'}
                    onClick={() => setChartMode('line')}
                    label="Chiziq"
                    icon={<TrendingUp className="h-4 w-4" />}
                  />
                </div>
              </div>
              {data.days.length === 0 ? (
                <p className="py-12 text-center text-slate-400">Bu davrda pul harakati yo'q</p>
              ) : (
                <DailyCashChart days={data.days} mode={chartMode} />
              )}
            </Card>

            {/* --- Toifalar kesimi, har qator ochiladi --- */}
            <div className="grid gap-4 xl:grid-cols-2">
              {data.sections.map((section) => (
                <SectionCard
                  key={section.kind}
                  section={section}
                  onOpen={(row) =>
                    setDetails({
                      source: 'cashflow',
                      title: row.label,
                      subtitle: `${section.label} · ${period}`,
                      key: row.key,
                      from,
                      to,
                    })
                  }
                />
              ))}

              <MethodsCard
                methods={data.methods}
                total={data.methodsTotal}
                onOpen={(row) =>
                  setDetails({
                    source: 'cashflow',
                    title: row.label,
                    subtitle: `To'lov usuli · ${period}`,
                    method: row.method,
                    from,
                    to,
                  })
                }
              />

              <DiscountsCard discounts={data.discounts} />
            </div>
          </div>
        )}
      </ReportState>

      <LedgerDetailsModal request={details} onClose={() => setDetails(null)} />
    </div>
  )
}

/* ------------------------------------------------------------------ */

/** "o'tgan davrga nisbatan +12%" — foiz SERVERDAN keladi. */
function changeHint(changePercent: number | null, previous: number): string {
  if (changePercent === null) {
    return previous === 0 ? "Oldingi davrda harakat yo'q" : `Oldingi davr: ${formatMoney(previous)}`
  }
  const sign = changePercent > 0 ? '+' : ''
  return `Oldingi davrga nisbatan ${sign}${changePercent}% (${formatMoney(previous)})`
}

function ModeButton({
  active,
  onClick,
  label,
  icon,
}: {
  active: boolean
  onClick: () => void
  label: string
  icon: ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
        active ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
      )}
    >
      {icon}
      {label}
    </button>
  )
}

/**
 * Bitta bo'lim: toifalar, ulush chiziqchasi va yakun. Qator bosilganda
 * o'sha toifaning jurnal satrlari ochiladi.
 */
function SectionCard({
  section,
  onOpen,
}: {
  section: CashFlowSection
  onOpen: (row: CashFlowCategoryRow) => void
}) {
  // Ulush — FAQAT chiziqcha uzunligi uchun; raqam sifatida ko'rsatilmaydi.
  const largest = Math.max(...section.rows.map((r) => Math.abs(r.total.amount)), 1)

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 p-4">
        <div>
          <h2 className="font-semibold text-slate-800">{section.label}</h2>
          <p className="text-sm text-slate-400">Qatorni bosing — satrlari ochiladi</p>
        </div>
        <p className={cn('text-lg font-semibold', signClass(section.total.amount))}>
          {formatSignedMoney(section.total.amount)}
        </p>
      </div>

      <ul className="divide-y divide-slate-100">
        {section.rows.map((row) => (
          <li key={row.key}>
            <button
              type="button"
              onClick={() => onOpen(row)}
              className="flex w-full items-center gap-3 px-4 py-2.5 text-left transition-colors hover:bg-slate-50/70"
            >
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm text-slate-700">{row.label}</p>
                <div className="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-slate-100">
                  <div
                    className={cn('h-full rounded-full', sectionTone[section.kind] ?? 'bg-slate-400')}
                    style={{ width: `${Math.round((Math.abs(row.total.amount) / largest) * 100)}%` }}
                  />
                </div>
                {/*
                  Qarama-qarshi tomon — STORNO: tushum qatorida u chiqim,
                  chiqim qatorida esa kirim bo'lib turadi.
                */}
                {row.total.inflow !== 0 && row.total.outflow !== 0 && (
                  <p className="mt-1 text-xs text-amber-600">
                    Shundan storno:{' '}
                    {formatMoney(section.kind === 'expense' ? row.total.inflow : row.total.outflow)}
                  </p>
                )}
              </div>
              <span className={cn('shrink-0 text-sm font-semibold', signClass(row.total.amount))}>
                {formatSignedMoney(row.total.amount)}
              </span>
            </button>
          </li>
        ))}
      </ul>
    </Card>
  )
}

/**
 * Chegirmalar tahlili — EduSchool hisobot ekranida ham shu blok bor
 * (2026-09-18 da o'qildi): jami chegirma, necha marta qo'llangani, o'rtachasi
 * va ulushlari.
 *
 * KESIM TOIFA BO'YICHA, CHEGIRMA NOMI BO'YICHA EMAS — sabab serverda
 * (`DiscountAnalysisDto` izohi): hisob-faktura qaysi chegirma qoidasidan
 * kelganini saqlaydigan ustun yo'q, taxmin qilish esa pul hisobotida
 * yaramaydi.
 */
function DiscountsCard({ discounts }: { discounts: DiscountAnalysis }) {
  return (
    <Card className="p-0">
      <div className="border-b border-slate-100 p-4">
        <h2 className="font-semibold text-slate-800">Chegirmalar tahlili</h2>
        <p className="text-xs text-slate-400">
          Davr hisob-fakturalaridagi chegirma — toifalar kesimida
        </p>
      </div>

      <div className="grid grid-cols-3 gap-3 border-b border-slate-100 p-4">
        <Figure label="Jami chegirma" value={formatMoney(discounts.total)} />
        <Figure label="Qo'llanishlar" value={String(discounts.appliedCount)} />
        <Figure label="O'rtacha" value={formatMoney(discounts.average)} />
      </div>

      {discounts.rows.length === 0 ? (
        <p className="p-4 text-sm text-slate-400">Bu davrda chegirma qo'llanmagan.</p>
      ) : (
        <ul className="divide-y divide-slate-100">
          {discounts.rows.map((row) => (
            <li key={row.categoryCode} className="flex items-center justify-between gap-3 px-4 py-2.5">
              <span className="min-w-0">
                <span className="block truncate font-medium text-slate-700">{row.categoryName}</span>
                <span className="block text-xs text-slate-400">
                  {row.invoiceCount} ta · ulushi {row.share}%
                </span>
              </span>
              <span className="shrink-0 text-sm font-semibold tabular-nums text-slate-800">
                {formatMoney(row.total)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}

/** Kichik ko'rsatkich — chegirma blokidagi uchta raqam. */
function Figure({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl bg-slate-50 p-3">
      <p className="text-[11px] font-medium uppercase tracking-wide text-slate-400">{label}</p>
      <p className="mt-0.5 truncate text-sm font-semibold text-slate-800">{value}</p>
    </div>
  )
}

/** To'lov usullari — faqat TO'LOVLAR: chiqimda usul saqlanmaydi. */
function MethodsCard({
  methods,
  total,
  onOpen,
}: {
  methods: FinanceMethodRow[]
  total: FinanceMethodRow
  onOpen: (row: FinanceMethodRow) => void
}) {
  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 p-4">
        <div>
          <h2 className="font-semibold text-slate-800">To'lov usullari</h2>
          <p className="text-sm text-slate-400">
            Faqat o'quvchi to'lovlari — chiqimda usul saqlanmaydi
          </p>
        </div>
        <p className="text-lg font-semibold text-slate-800">{formatMoney(total.amount)}</p>
      </div>

      {methods.length === 0 ? (
        <p className="p-6 text-center text-sm text-slate-400">Bu davrda to'lov bo'lmagan.</p>
      ) : (
        <ul className="divide-y divide-slate-100">
          {methods.map((row) => (
            <li key={row.method}>
              <button
                type="button"
                onClick={() => onOpen(row)}
                className="flex w-full items-center justify-between gap-3 px-4 py-2.5 text-left transition-colors hover:bg-slate-50/70"
              >
                <span className="text-sm text-slate-700">{row.label}</span>
                <span className="flex items-center gap-3">
                  <span className="text-xs text-slate-400">{row.count} ta</span>
                  <span className={cn('text-sm font-semibold', signClass(row.amount))}>
                    {formatMoney(row.amount)}
                  </span>
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}

export default FinancialReportsPage
