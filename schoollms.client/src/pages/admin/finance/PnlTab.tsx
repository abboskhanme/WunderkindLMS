/**
 * Foyda va zarar (P&L) — P1-18, va yil × oy matritsasi (§2.5 F5.01–F5.03).
 *
 * Manba: `GET /api/admin/finance/pnl` — `ledger_entries` ni akkaunt
 * prefiksi bo'yicha yig'adi. Bu yerda hech narsa qayta hisoblanmaydi:
 * `revenueTotal`, `expenseTotal`, `net` — serverning raqamlari. JS'da
 * pul `float64`, shuning uchun yig'indini qayta hisoblash tiyin xatosini
 * keltirib chiqarardi (BillingDtos.cs, 2-qoida).
 *
 * Ikki ko'rinish, bitta manba:
 *   · DAVR — yuqoridagi sana oralig'i bo'yicha toifalar kesimi;
 *   · YIL  — `GET /admin/finance/pnl/matrix?year`, 12 ta oy ustuni, oy
 *     boshidagi va oxiridagi pul qoldig'i bilan. Har katak bosiladi va
 *     uni HOSIL QILGAN jurnal satrlari ochiladi (`LedgerDetailsModal`).
 *
 * Ulush foizi (%) — YAGONA hisob, u ham faqat ko'rsatish uchun: qaysi
 * toifa xarajatning yarmini yeyayotganini ko'rsatadi.
 */
import { useState } from 'react'
import { Download, FileSpreadsheet, TrendingDown, TrendingUp, Wallet } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import {
  downloadProfitLoss,
  getProfitLoss,
  type ProfitLossLine,
} from '@/api/services/financeReports'
import {
  downloadProfitLossMatrix,
  getProfitLossMatrix,
  type ProfitLossMatrixRow,
} from '@/api/services/financeStatements'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { StatCard } from '@/components/ui/StatCard'
import { monthShortNames } from '@/config/constants'
import { cn, exportToCsv, formatDate, formatMoney } from '@/lib/utils'
import { ReportState } from './ReportState'
import { LedgerDetailsModal, type LedgerDetailsRequest } from './LedgerDetailsModal'
import { accountLabel, formatSignedMoney, signClass } from './reportLabels'

interface Props {
  /** "YYYY-MM-DD" */
  from: string
  to: string
}

type Mode = 'period' | 'year'

export function PnlTab({ from, to }: Props) {
  const [mode, setMode] = useState<Mode>('period')
  const [year, setYear] = useState(() => Number(to.slice(0, 4)))

  return (
    <div className="space-y-6">
      <Card className="flex flex-wrap items-center gap-3 p-4">
        <span className="text-sm font-medium text-slate-600">Ko'rinish:</span>
        <div className="flex items-center gap-1 rounded-lg bg-slate-100 p-1">
          <ViewButton active={mode === 'period'} onClick={() => setMode('period')} label="Davr" />
          <ViewButton active={mode === 'year'} onClick={() => setMode('year')} label="Yil" />
        </div>
        {mode === 'year' && (
          <div className="flex items-center gap-2">
            <Button variant="ghost" className="px-2 py-1" onClick={() => setYear((y) => y - 1)}>
              ‹
            </Button>
            <span className="min-w-14 text-center text-sm font-semibold text-slate-700">{year}</span>
            <Button variant="ghost" className="px-2 py-1" onClick={() => setYear((y) => y + 1)}>
              ›
            </Button>
          </div>
        )}
        <p className="text-xs text-slate-400">
          {mode === 'period'
            ? "Yuqoridagi sana oralig'i bo'yicha"
            : 'Yil × oy jadvali · katakni bosing — jurnal satrlari ochiladi'}
        </p>
      </Card>

      {mode === 'period' ? <PnlPeriod from={from} to={to} /> : <PnlYear year={year} />}
    </div>
  )
}

function ViewButton({
  active,
  onClick,
  label,
}: {
  active: boolean
  onClick: () => void
  label: string
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
        active ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
      )}
    >
      {label}
    </button>
  )
}

function PnlPeriod({ from, to }: Props) {
  const { data, loading, error, refetch } = useAsync(() => getProfitLoss(from, to), [from, to])
  const [exporting, setExporting] = useState(false)

  const isEmpty = !!data && data.revenueTotal === 0 && data.expenseTotal === 0

  const handleExport = () => {
    if (!data) return
    exportToCsv(
      `foyda-zarar_${data.from}_${data.to}.csv`,
      ["Yo'nalish", 'Toifa', 'Summa'],
      [
        ...data.revenue.map((l) => ['Daromad', accountLabel(l.account), String(l.amount)]),
        ...data.expense.map((l) => ['Xarajat', accountLabel(l.account), String(l.amount)]),
        ['Jami', 'Daromad', String(data.revenueTotal)],
        ['Jami', 'Xarajat', String(data.expenseTotal)],
        ['Jami', 'Sof natija', String(data.net)],
      ],
    )
  }

  // F5.06 — .xlsx, joriy DAVR bo'yicha (server qatorlari CSV bilan bir xil).
  const handleDownloadXlsx = async () => {
    if (!data) return
    setExporting(true)
    try {
      await downloadProfitLoss(data.from, data.to)
    } finally {
      setExporting(false)
    }
  }

  return (
    <ReportState
      loading={loading}
      error={error}
      isEmpty={isEmpty}
      emptyTitle="Bu davrda moliyaviy harakat yo'q"
      emptyHint="Boshqa davrni tanlang yoki oylik hisoblashni ishga tushiring."
      onRetry={refetch}
    >
      {data && (
        <div className="space-y-6">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <StatCard
              label="Daromad"
              value={formatMoney(data.revenueTotal)}
              icon={TrendingUp}
              iconBg="bg-emerald-50"
              iconColor="text-emerald-600"
              hint={`${formatDate(data.from)} — ${formatDate(data.to)}`}
            />
            <StatCard
              label="Xarajat"
              value={formatMoney(data.expenseTotal)}
              icon={TrendingDown}
              iconBg="bg-red-50"
              iconColor="text-red-600"
              hint={`${data.expense.filter((l) => l.amount !== 0).length} ta toifa`}
            />
            <StatCard
              label="Sof natija"
              value={formatSignedMoney(data.net)}
              icon={Wallet}
              iconBg={data.net >= 0 ? 'bg-emerald-50' : 'bg-red-50'}
              iconColor={data.net >= 0 ? 'text-emerald-600' : 'text-red-600'}
              hint="Daromad − Xarajat"
            />
          </div>

          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={handleExport}>
              <Download className="h-4 w-4" /> CSV
            </Button>
            <Button variant="secondary" onClick={handleDownloadXlsx} disabled={exporting}>
              <FileSpreadsheet className="h-4 w-4" /> {exporting ? 'Tayyorlanmoqda...' : 'Excel'}
            </Button>
          </div>

          <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
            <LinesTable
              title="Daromad — toifalar kesimida"
              lines={data.revenue}
              total={data.revenueTotal}
              positive
            />
            <LinesTable
              title="Xarajat — toifalar kesimida"
              lines={data.expense}
              total={data.expenseTotal}
              positive={false}
            />
          </div>

          <Card className={cn(data.net >= 0 ? 'border-emerald-200' : 'border-red-200')}>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <p className="text-sm font-medium text-slate-600">Davr yakuni</p>
                <p className="text-xs text-slate-400">
                  Storno qilingan yozuvlar avtomatik chiqarib tashlangan.
                </p>
              </div>
              <p className={cn('text-2xl font-semibold', signClass(data.net))}>
                {formatSignedMoney(data.net)}
              </p>
            </div>
          </Card>
        </div>
      )}
    </ReportState>
  )
}

function LinesTable({
  title,
  lines,
  total,
  positive,
}: {
  title: string
  lines: ProfitLossLine[]
  total: number
  positive: boolean
}) {
  // Harakati bo'lmagan hisoblarni yashirmaymiz — ular "0" bo'lib turishi
  // kerak, aks holda ustunlar oydan oyga o'zgarib ketardi va "qayerga
  // ketdi?" degan savol tug'ilardi (FinanceReportQueries.Lines izohi).
  const share = (amount: number) => (total === 0 ? 0 : Math.round((amount / total) * 100))

  return (
    <Card className="p-0">
      <div className="border-b border-slate-100 p-4">
        <h2 className="font-semibold text-slate-800">{title}</h2>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
            <tr>
              <th className="px-4 py-3">Toifa</th>
              <th className="px-4 py-3 text-right">Summa</th>
              <th className="px-4 py-3 text-right">Ulush</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {lines.map((l) => (
              <tr key={l.account} className="hover:bg-slate-50/60">
                <td className="px-4 py-3 text-slate-600">{accountLabel(l.account)}</td>
                <td
                  className={cn(
                    'px-4 py-3 text-right font-medium',
                    l.amount === 0
                      ? 'text-slate-300'
                      : positive
                        ? 'text-emerald-600'
                        : 'text-red-600',
                  )}
                >
                  {formatMoney(l.amount)}
                </td>
                <td className="px-4 py-3 text-right text-slate-400">{share(l.amount)}%</td>
              </tr>
            ))}
            {lines.length === 0 && (
              <tr>
                <td colSpan={3} className="px-4 py-10 text-center text-slate-400">
                  Bu davrda yozuv yo'q
                </td>
              </tr>
            )}
          </tbody>
          <tfoot>
            <tr className="border-t border-slate-200 bg-slate-50/60 text-sm font-semibold">
              <td className="px-4 py-3 text-slate-700">Jami</td>
              <td
                className={cn(
                  'px-4 py-3 text-right',
                  positive ? 'text-emerald-700' : 'text-red-700',
                )}
              >
                {formatMoney(total)}
              </td>
              <td className="px-4 py-3 text-right text-slate-400">100%</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </Card>
  )
}

/* ==================================================================
   YIL × OY MATRITSASI (§2.5 F5.01, F5.02, F5.03)
   ================================================================== */

/**
 * Bir yilning P&L jadvali. Har katak — o'sha oyning P&L satri (serverda
 * `ProfitLossAsync` ning aynan o'zi ishlaydi), qator va ustun yakunlari
 * ham SERVERDAN keladi. Katak bosilganda uni hosil qilgan jurnal satrlari
 * ochiladi va ularning yig'indisi katakka teng bo'ladi.
 */
function PnlYear({ year }: { year: number }) {
  const { data, loading, error, refetch } = useAsync(() => getProfitLossMatrix(year), [year])
  const [details, setDetails] = useState<LedgerDetailsRequest | null>(null)
  const [exporting, setExporting] = useState(false)

  const isEmpty = !!data && data.revenueTotal === 0 && data.expenseTotal === 0

  const handleExport = () => {
    if (!data) return
    exportToCsv(
      `foyda-zarar_${data.year}.csv`,
      ["Yo'nalish", 'Toifa', ...data.months, 'Jami'],
      [
        ...data.revenue.map((r) => [
          'Daromad',
          accountLabel(r.account),
          ...r.months.map(String),
          String(r.total),
        ]),
        ...data.expense.map((r) => [
          'Xarajat',
          accountLabel(r.account),
          ...r.months.map(String),
          String(r.total),
        ]),
        ['Jami', 'Daromad', ...data.revenueMonths.map(String), String(data.revenueTotal)],
        ['Jami', 'Xarajat', ...data.expenseMonths.map(String), String(data.expenseTotal)],
        ['Jami', 'Sof natija', ...data.netMonths.map(String), String(data.netTotal)],
        ['Qoldiq', 'Oy boshida', ...data.startBalance.map(String), String(data.openingBalance)],
        ['Qoldiq', 'Oy oxirida', ...data.endBalance.map(String), String(data.closingBalance)],
      ],
    )
  }

  // F5.06 — .xlsx, joriy YIL bo'yicha, qoldiq qatorlari bilan.
  const handleDownloadXlsx = async () => {
    if (!data) return
    setExporting(true)
    try {
      await downloadProfitLossMatrix(data.year)
    } finally {
      setExporting(false)
    }
  }

  /** Katakning davri: oy ustuni yoki butun yil ("Jami" ustuni). */
  const openCell = (account: string, label: string, monthIndex: number | null) => {
    const month = monthIndex === null ? null : data?.months[monthIndex]
    setDetails({
      source: 'ledger',
      title: label,
      subtitle: month ? monthTitle(month) : `${year}-yil`,
      account,
      from: month ? `${month}-01` : `${year}-01-01`,
      to: month ? lastDayOf(month) : `${year}-12-31`,
    })
  }

  return (
    <>
      <ReportState
        loading={loading}
        error={error}
        isEmpty={isEmpty}
        emptyTitle="Bu yilda moliyaviy harakat yo'q"
        emptyHint="Boshqa yilni tanlang yoki oylik hisoblashni ishga tushiring."
        onRetry={refetch}
      >
        {data && (
          <div className="space-y-4">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              <StatCard
                label="Yillik daromad"
                value={formatMoney(data.revenueTotal)}
                icon={TrendingUp}
                iconBg="bg-emerald-50"
                iconColor="text-emerald-600"
                hint={`${data.year}-yil`}
              />
              <StatCard
                label="Yillik xarajat"
                value={formatMoney(data.expenseTotal)}
                icon={TrendingDown}
                iconBg="bg-red-50"
                iconColor="text-red-600"
              />
              <StatCard
                label="Sof natija"
                value={formatSignedMoney(data.netTotal)}
                icon={Wallet}
                iconBg={data.netTotal >= 0 ? 'bg-emerald-50' : 'bg-red-50'}
                iconColor={data.netTotal >= 0 ? 'text-emerald-600' : 'text-red-600'}
                hint={`Yil oxiridagi pul: ${formatMoney(data.closingBalance)}`}
              />
            </div>

            <div className="flex justify-end gap-2">
              <Button variant="secondary" onClick={handleExport}>
                <Download className="h-4 w-4" /> CSV
              </Button>
              <Button variant="secondary" onClick={handleDownloadXlsx} disabled={exporting}>
                <FileSpreadsheet className="h-4 w-4" /> {exporting ? 'Tayyorlanmoqda...' : 'Excel'}
              </Button>
            </div>

            <Card className="p-0">
              <div className="overflow-x-auto">
                <table className="w-full min-w-[56rem] text-left text-sm">
                  <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="sticky left-0 z-10 bg-slate-50 px-4 py-3">Toifa</th>
                      {data.months.map((m, i) => (
                        <th key={m} className="px-2 py-3 text-right">
                          {monthShortNames[i]}
                        </th>
                      ))}
                      <th className="px-4 py-3 text-right">Jami</th>
                    </tr>
                  </thead>

                  <tbody className="divide-y divide-slate-100">
                    <BalanceRow
                      label="Oy boshidagi qoldiq"
                      values={data.startBalance}
                      total={data.openingBalance}
                    />

                    <SummaryRow
                      label="Daromad"
                      months={data.months}
                      values={data.revenueMonths}
                      total={data.revenueTotal}
                      tone="text-emerald-700"
                      onOpen={(i) => openCell('revenue:*', 'Daromad — barcha toifalar', i)}
                    />
                    {data.revenue.map((row) => (
                      <MatrixRow
                        key={row.account}
                        row={row}
                        months={data.months}
                        tone="text-emerald-600"
                        onOpen={(i) => openCell(row.account, accountLabel(row.account), i)}
                      />
                    ))}

                    <SummaryRow
                      label="Xarajat"
                      months={data.months}
                      values={data.expenseMonths}
                      total={data.expenseTotal}
                      tone="text-red-700"
                      onOpen={(i) => openCell('expense:*', 'Xarajat — barcha toifalar', i)}
                    />
                    {data.expense.map((row) => (
                      <MatrixRow
                        key={row.account}
                        row={row}
                        months={data.months}
                        tone="text-red-600"
                        onOpen={(i) => openCell(row.account, accountLabel(row.account), i)}
                      />
                    ))}

                    <tr className="border-t-2 border-slate-200 bg-slate-50/70 font-semibold">
                      <td className="sticky left-0 z-10 bg-slate-50/70 px-4 py-2.5 text-slate-800">
                        Sof natija
                      </td>
                      {data.netMonths.map((v, i) => (
                        <td
                          key={data.months[i]}
                          className={cn('px-2 py-2.5 text-right', signClass(v))}
                        >
                          {v === 0 ? '—' : formatSignedMoney(v)}
                        </td>
                      ))}
                      <td className={cn('px-4 py-2.5 text-right', signClass(data.netTotal))}>
                        {formatSignedMoney(data.netTotal)}
                      </td>
                    </tr>

                    <BalanceRow
                      label="Oy oxiridagi qoldiq"
                      values={data.endBalance}
                      total={data.closingBalance}
                    />
                  </tbody>
                </table>
              </div>
              <p className="border-t border-slate-100 px-4 py-3 text-xs text-slate-400">
                Qoldiq qatorlari — kassa va bank hisobidagi pul ("Pul oqimi" ekranidagi raqamning
                o'zi). Storno o'z kunida ko'rinadi va yashirilmaydi.
              </p>
            </Card>
          </div>
        )}
      </ReportState>

      <LedgerDetailsModal request={details} onClose={() => setDetails(null)} />
    </>
  )
}

/** Toifa qatori — har katak bosiladi. */
function MatrixRow({
  row,
  months,
  tone,
  onOpen,
}: {
  row: ProfitLossMatrixRow
  months: string[]
  tone: string
  onOpen: (monthIndex: number | null) => void
}) {
  return (
    <tr className="hover:bg-slate-50/60">
      <td className="sticky left-0 z-10 bg-white px-4 py-2 text-slate-600">
        {accountLabel(row.account)}
      </td>
      {row.months.map((value, i) => (
        <MatrixCell key={months[i]} value={value} tone={tone} onOpen={() => onOpen(i)} />
      ))}
      <MatrixCell
        value={row.total}
        tone={cn(tone, 'font-semibold')}
        onOpen={() => onOpen(null)}
        wide
      />
    </tr>
  )
}

/** Yakun qatori (Daromad / Xarajat) — u ham ochiladi, guruh bo'yicha. */
function SummaryRow({
  label,
  months,
  values,
  total,
  tone,
  onOpen,
}: {
  label: string
  months: string[]
  values: number[]
  total: number
  tone: string
  onOpen: (monthIndex: number | null) => void
}) {
  return (
    <tr className="border-t border-slate-200 bg-slate-50/60 font-semibold">
      <td className="sticky left-0 z-10 bg-slate-50/60 px-4 py-2.5 text-slate-800">{label}</td>
      {values.map((value, i) => (
        <MatrixCell key={months[i]} value={value} tone={tone} onOpen={() => onOpen(i)} />
      ))}
      <MatrixCell value={total} tone={tone} onOpen={() => onOpen(null)} wide />
    </tr>
  )
}

/** Qoldiq qatori — bosilmaydi: u jurnal satri emas, HOLAT. */
function BalanceRow({ label, values, total }: { label: string; values: number[]; total: number }) {
  return (
    <tr className="bg-white text-slate-500">
      <td className="sticky left-0 z-10 bg-white px-4 py-2 text-xs uppercase tracking-wide">
        {label}
      </td>
      {values.map((value, i) => (
        <td key={i} className="px-2 py-2 text-right text-xs">
          {formatMoney(value)}
        </td>
      ))}
      <td className="px-4 py-2 text-right text-xs font-medium">{formatMoney(total)}</td>
    </tr>
  )
}

function MatrixCell({
  value,
  tone,
  onOpen,
  wide = false,
}: {
  value: number
  tone: string
  onOpen: () => void
  wide?: boolean
}) {
  if (value === 0) {
    return <td className={cn(wide ? 'px-4' : 'px-2', 'py-2 text-right text-slate-300')}>—</td>
  }

  return (
    <td className={cn(wide ? 'px-4' : 'px-2', 'py-2 text-right')}>
      <button
        type="button"
        onClick={onOpen}
        title="Satrlarini ko'rish"
        className={cn('rounded px-1 underline-offset-2 hover:underline', tone)}
      >
        {formatMoney(value)}
      </button>
    </td>
  )
}

/** "2026-03" → "2026-03-31" */
function lastDayOf(month: string): string {
  const [y, m] = month.split('-').map(Number)
  const last = new Date(Date.UTC(y, m, 0)).getUTCDate()
  return `${month}-${String(last).padStart(2, '0')}`
}

/** "2026-03" → "Mar 2026" */
function monthTitle(month: string): string {
  const [y, m] = month.split('-')
  return `${monthShortNames[Number(m) - 1] ?? m} ${y}`
}
