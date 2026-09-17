/**
 * Moliya hisobotlari (P&L) 2.0 — §2.6, `FINANCE_ALL.PNL_EXPECTATION`
 * (EduSchool'da `/pnl-expectation`, **beta**).
 *
 * Ilgari bu ekran RAD ETILGAN edi (`docs/modules/existing-module-gaps.md`
 * §3.6: "Verdict: defer... Revisit at the start of the next academic
 * year"). Mijoz 2026-09-18 da shu qarorni bekor qildi.
 *
 * <b>Bu — EduSchool'dagi to'liq besh tabli ekranning ENG KICHIK halol
 * qismi</b> (`finance-parity.md` §2.6.1: `expectation`, `dynamics`,
 * `changes`, `yearly`, `planned`). Shu sahifada FAQAT `expectation` —
 * bitta oy uchun reja/fakt/farq jadvali. Nima yo'q va nega — pastdagi
 * "Nima qurilmagan" bandida.
 *
 * <b>PULNI FRONTEND HISOBLAMAYDI</b> (`financeReports.ts` dagi qoida bu
 * yerda ham): hamma summa, foiz va farq SERVERDAN tayyor keladi
 * (`FinanceReportQueries.RevenueExpectation.cs`). "Fakt" ustunining
 * daromad/chiqim/natija qatorlari — oddiy P&L (`/admin/finance/pnl`) bilan
 * AYNAN bir manbadan (`ProfitLossAsync`), shuning uchun ikkovi hech qachon
 * kelisha olmay qolmaydi.
 */
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Download, FlaskConical, TrendingDown, TrendingUp, Users, Wallet } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import { getRevenueExpectation, type RevenueExpectation } from '@/api/services/financeReports'
import { useAuth } from '@/context/auth-context'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { StatCard } from '@/components/ui/StatCard'
import { formatMonth } from '@/config/constants'
import { cn, exportToCsv, formatMoney } from '@/lib/utils'
import { formatSignedMoney, signClass } from './reportLabels'
import { ReportState } from './ReportState'

/** SPEC §4.3: moliya hisobotlari faqat admin va direktorga ochiq. */
const ALLOWED_ROLES = ['admin', 'superadmin']

/** Joriy oy, "YYYY-MM". */
function currentMonth(): string {
  return new Date().toISOString().slice(0, 7)
}

function pct(value: number | null): string {
  return value === null ? '—' : `${value.toFixed(2)}%`
}

function moneyOrDash(value: number | null): string {
  return value === null ? '—' : formatMoney(value)
}

interface Row {
  label: string
  plan?: string
  fact?: string
  diff?: string
  diffClass?: string
  hint?: string
}

function buildRows(data: RevenueExpectation): { group: string; rows: Row[] }[] {
  const unpaidStudents = data.studentsExpected - data.studentsPaid

  return [
    {
      group: "O'QUVCHILAR",
      rows: [
        { label: "Faol obunali o'quvchilar", plan: String(data.studentsActive) },
        { label: "Shu oy YANGI boshlagan", plan: String(data.studentsAdmitted) },
        { label: 'Shu oy TUGATGAN', plan: String(data.studentsDeparted) },
        {
          label: "Hisob-fakturasi bor o'quvchilar",
          plan: String(data.studentsExpected),
          fact: `${data.studentsPaid} to'liq to'lagan`,
          diff: unpaidStudents > 0 ? `${unpaidStudents} qarzdor` : "hammasi to'liq",
          diffClass: unpaidStudents > 0 ? 'text-red-600' : 'text-emerald-600',
        },
      ],
    },
    {
      group: 'DAROMAD',
      rows: [
        { label: 'Yalpi (chegirmasiz)', plan: formatMoney(data.grossExpected) },
        {
          label: 'Chegirma',
          plan: data.discountAmount === 0 ? formatMoney(0) : `−${formatMoney(data.discountAmount)}`,
          hint: `stavka: ${pct(data.discountRate)}`,
        },
        {
          label: 'Sof daromad',
          plan: formatMoney(data.netExpected),
          fact: formatMoney(data.revenueActual),
          diff: formatSignedMoney(data.revenueDiff),
          diffClass: signClass(data.revenueDiff),
          hint: "reja — hisob-fakturadan, fakt — jurnaldan (P&L bilan bir xil)",
        },
        { label: "O'quvchi boshiga (reja)", plan: moneyOrDash(data.perStudentNet) },
      ],
    },
    {
      group: 'PUL (FAKT)',
      rows: [
        {
          label: "Shu oy uchun yig'ilgan",
          fact: formatMoney(data.collectedForPeriod),
          hint: `yig'ilish darajasi: ${pct(data.collectionRateForPeriod)}`,
        },
        { label: 'Qolgan qarz (shu oy)', fact: formatMoney(data.outstandingForPeriod) },
      ],
    },
    {
      group: 'CHIQIM',
      rows: [
        {
          label: 'Chiqim (jurnaldan, fakt)',
          plan: "— reja yo'q",
          fact: formatMoney(data.expenseActual),
          hint: "rejalashtirilgan chiqim shabloni hali yo'q (F6.01)",
        },
      ],
    },
    {
      group: 'NATIJA',
      rows: [
        {
          label: 'Sof natija (fakt)',
          fact: formatMoney(data.profitActual),
          hint: `marja: ${pct(data.margin)}`,
        },
        { label: "O'quvchi boshiga (fakt)", fact: moneyOrDash(data.profitPerStudent) },
      ],
    },
  ]
}

export function PnlExpectationPage() {
  const { user } = useAuth()
  const allowed = user !== null && ALLOWED_ROLES.includes(user.role)

  const [month, setMonth] = useState(currentMonth)

  const { data, loading, error, refetch } = useAsync(() => getRevenueExpectation(month), [month])

  const handleExport = () => {
    if (!data) return
    const groups = buildRows(data)
    exportToCsv(
      `pnl-2.0_${month}.csv`,
      ['Guruh', "Ko'rsatkich", 'Reja', 'Fakt', 'Farq'],
      groups.flatMap((g) => g.rows.map((r) => [g.group, r.label, r.plan ?? '', r.fact ?? '', r.diff ?? ''])),
    )
  }

  if (!allowed) {
    return (
      <Card>
        <p className="py-12 text-center text-slate-400">Bu bo'limga ruxsatingiz yo'q.</p>
      </Card>
    )
  }

  return (
    <div className="space-y-5">
      <div>
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="text-xl font-semibold text-slate-800">Moliya hisobotlari (P&L) 2.0</h1>
          <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2.5 py-0.5 text-xs font-medium text-amber-800">
            <FlaskConical className="h-3.5 w-3.5" /> Beta
          </span>
        </div>
        <p className="mt-0.5 text-sm text-slate-500">
          Bitta oy uchun reja (faol obunalar va hisob-fakturalardan kutilgan daromad) va fakt
          (jurnaldagi haqiqiy daromad/chiqim) — taxminiy hisob-kitob, YOPIQ hisobot emas.{' '}
          <Link to="/admin/finance/pnl" className="text-brand-600 hover:underline">
            Oddiy P&L (1.0)
          </Link>{' '}
          bilan solishtiring.
        </p>
      </div>

      <Card className="flex flex-wrap items-center gap-3 p-4">
        <span className="text-sm font-medium text-slate-600">Oy:</span>
        <input
          type="month"
          value={month}
          max={currentMonth()}
          onChange={(e) => setMonth(e.target.value)}
          aria-label="Oy"
          className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
        />
        <p className="text-xs text-slate-400">{formatMonth(month)}</p>
      </Card>

      <ReportState
        loading={loading}
        error={error}
        isEmpty={!!data && data.studentsExpected === 0 && data.revenueActual === 0 && data.expenseActual === 0}
        emptyTitle="Bu oyda moliyaviy harakat yo'q"
        emptyHint="Boshqa oyni tanlang."
        onRetry={refetch}
      >
        {data && (
          <div className="space-y-6">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <StatCard
                label="Faol o'quvchilar"
                value={data.studentsActive}
                icon={Users}
                iconBg="bg-brand-50"
                iconColor="text-brand-600"
                hint={`${data.studentsPaid}/${data.studentsExpected} to'liq to'lagan`}
              />
              <StatCard
                label="Sof kutilgan daromad"
                value={formatMoney(data.netExpected)}
                icon={TrendingUp}
                iconBg="bg-emerald-50"
                iconColor="text-emerald-600"
                hint={`yig'ilgan: ${formatMoney(data.collectedForPeriod)} (${pct(data.collectionRateForPeriod)})`}
              />
              <StatCard
                label="Fakt daromad (jurnal)"
                value={formatMoney(data.revenueActual)}
                icon={TrendingDown}
                iconBg={data.revenueDiff >= 0 ? 'bg-emerald-50' : 'bg-red-50'}
                iconColor={data.revenueDiff >= 0 ? 'text-emerald-600' : 'text-red-600'}
                hint={`rejadan farq: ${formatSignedMoney(data.revenueDiff)}`}
              />
              <StatCard
                label="Sof natija (fakt)"
                value={formatSignedMoney(data.profitActual)}
                icon={Wallet}
                iconBg={data.profitActual >= 0 ? 'bg-emerald-50' : 'bg-red-50'}
                iconColor={data.profitActual >= 0 ? 'text-emerald-600' : 'text-red-600'}
                hint={`marja: ${pct(data.margin)}`}
              />
            </div>

            <div className="flex justify-end">
              <Button variant="secondary" onClick={handleExport}>
                <Download className="h-4 w-4" /> CSV
              </Button>
            </div>

            <Card className="p-0">
              <div className="overflow-x-auto">
                <table className="w-full min-w-[40rem] text-left text-sm">
                  <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-4 py-3">Ko'rsatkich</th>
                      <th className="px-4 py-3 text-right">Reja</th>
                      <th className="px-4 py-3 text-right">Fakt</th>
                      <th className="px-4 py-3 text-right">Farq</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {buildRows(data).map((group) => (
                      <RowGroup key={group.group} group={group.group} rows={group.rows} />
                    ))}
                  </tbody>
                </table>
              </div>
              <p className="border-t border-slate-100 px-4 py-3 text-xs text-slate-400">
                "Reja" — shu oyning faol obunalari va hisob-fakturalaridan; "Fakt" — jurnaldagi
                haqiqiy harakat (storno hisobga olingan). Chiqim tomonida reja yo'q: rejalashtirilgan
                chiqim shablonlari hali qurilmagan.
              </p>
            </Card>

            <Card className="space-y-2 bg-slate-50/60 text-xs text-slate-500">
              <p className="font-medium text-slate-600">Bu ekranda hali yo'q (EduSchool'da bor):</p>
              <ul className="list-inside list-disc space-y-1">
                <li>Rejalashtirilgan chiqim shablonlari va eslatmalar (yangi jadval kerak).</li>
                <li>To'liq o'zgarishlar jurnali — kim, qachon, nima o'zgartirgani (sahifalash bilan).</li>
                <li>Kunlik dinamika grafigi va bashorat chizig'i.</li>
                <li>Yillik reja/fakt jadvali (eng yaxshi/yomon oy bilan).</li>
                <li>Filial tanlovi — bizda bitta maktab, filial tushunchasi yo'q.</li>
              </ul>
            </Card>
          </div>
        )}
      </ReportState>
    </div>
  )
}

function RowGroup({ group, rows }: { group: string; rows: Row[] }) {
  return (
    <>
      <tr className="bg-slate-50/80">
        <td colSpan={4} className="px-4 py-1.5 text-[11px] font-semibold uppercase tracking-wide text-slate-400">
          {group}
        </td>
      </tr>
      {rows.map((row) => (
        <tr key={row.label} className="hover:bg-slate-50/60">
          <td className="px-4 py-2.5 text-slate-600">
            {row.label}
            {row.hint && <span className="ml-2 text-xs text-slate-400">({row.hint})</span>}
          </td>
          <td className="px-4 py-2.5 text-right text-slate-700">{row.plan ?? '—'}</td>
          <td className="px-4 py-2.5 text-right text-slate-700">{row.fact ?? '—'}</td>
          <td className={cn('px-4 py-2.5 text-right font-medium', row.diffClass ?? 'text-slate-400')}>
            {row.diff ?? '—'}
          </td>
        </tr>
      ))}
    </>
  )
}
