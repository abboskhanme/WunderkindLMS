/**
 * Pul oqimi (Cash Flow) — P1-18.
 *
 * Manba: `GET /api/admin/finance/cashflow` — `cash` va `bank` hisoblarining
 * oylar kesimidagi harakati. Harakatsiz oy ham qatorda turadi (server
 * shunday beradi), shuning uchun grafikda uzilish bo'lmaydi.
 *
 * DAVR YAKUNLARI (kirim / chiqim / qoldiq) — serverning ildiz maydonlari,
 * qayta hisoblanmaydi. Faqat "Jami" ko'rinishidagi OYLIK qatorlar ikki
 * hisobning bir oyini qo'shadi: bu yangi ma'no emas, serverning o'zi bergan
 * ikki qatorni ekranda birlashtirish.
 */
import { Fragment, useMemo, useState } from 'react'
import { ArrowDownRight, ArrowUpRight, Download, Landmark, Wallet } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import {
  getCashFlow,
  type CashFlowAccount,
  type CashFlowMonth,
} from '@/api/services/financeReports'
import { getCashFlowStatement } from '@/api/services/financeStatements'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { StatCard } from '@/components/ui/StatCard'
import { CashFlowChart } from '@/components/charts/CashFlowChart'
import { cn, exportToCsv, formatMoney } from '@/lib/utils'
import { ReportState } from './ReportState'
import { LedgerDetailsModal, type LedgerDetailsRequest } from './LedgerDetailsModal'
import { accountLabel, formatMonthLabel, formatSignedMoney, signClass } from './reportLabels'

interface Props {
  /** "YYYY-MM-DD" */
  from: string
  to: string
}

type View = 'all' | 'cash' | 'bank'

const views: { value: View; label: string }[] = [
  { value: 'all', label: 'Jami' },
  { value: 'cash', label: 'Kassa (naqd)' },
  { value: 'bank', label: 'Bank' },
]

/** Ikki hisobning bir oydagi qatorlarini ekran uchun birlashtiradi. */
function combineMonths(accounts: CashFlowAccount[]): CashFlowMonth[] {
  const byMonth = new Map<string, CashFlowMonth>()
  for (const account of accounts) {
    for (const m of account.months) {
      const current = byMonth.get(m.month)
      byMonth.set(
        m.month,
        current
          ? {
              month: m.month,
              opening: current.opening + m.opening,
              inflow: current.inflow + m.inflow,
              outflow: current.outflow + m.outflow,
              net: current.net + m.net,
              closing: current.closing + m.closing,
            }
          : { ...m },
      )
    }
  }
  return [...byMonth.values()].sort((a, b) => (a.month < b.month ? -1 : 1))
}

export function CashFlowTab({ from, to }: Props) {
  const [view, setView] = useState<View>('all')
  const { data, loading, error, refetch } = useAsync(() => getCashFlow(from, to), [from, to])

  const shown = useMemo(() => {
    if (!data) return null
    if (view === 'all') {
      return {
        opening: data.opening,
        inflow: data.inflow,
        outflow: data.outflow,
        net: data.net,
        closing: data.closing,
        months: combineMonths(data.accounts),
      }
    }
    const account = data.accounts.find((a) => a.account === view)
    if (!account) return null
    return {
      opening: account.opening,
      inflow: account.inflow,
      outflow: account.outflow,
      net: account.net,
      closing: account.closing,
      months: account.months,
    }
  }, [data, view])

  const isEmpty =
    !!data && (!shown || (data.inflow === 0 && data.outflow === 0 && data.opening === 0))

  const handleExport = () => {
    if (!data || !shown) return
    exportToCsv(
      `pul-oqimi_${data.from}_${data.to}.csv`,
      ['Oy', "Boshlang'ich qoldiq", 'Kirim', 'Chiqim', 'Sof', 'Yakuniy qoldiq'],
      shown.months.map((m) => [
        formatMonthLabel(m.month),
        String(m.opening),
        String(m.inflow),
        String(m.outflow),
        String(m.net),
        String(m.closing),
      ]),
    )
  }

  return (
    <ReportState
      loading={loading}
      error={error}
      isEmpty={isEmpty}
      emptyTitle="Bu davrda pul harakati yo'q"
      emptyHint="Kassa va bank hisoblarida yozuv topilmadi. Davrni kengaytirib ko'ring."
      onRetry={refetch}
    >
      {data && shown && (
        <div className="space-y-6">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard
              label="Davr boshidagi qoldiq"
              value={formatMoney(shown.opening)}
              icon={Wallet}
              iconBg="bg-slate-100"
              iconColor="text-slate-500"
            />
            <StatCard
              label="Kirim"
              value={formatMoney(shown.inflow)}
              icon={ArrowUpRight}
              iconBg="bg-emerald-50"
              iconColor="text-emerald-600"
            />
            <StatCard
              label="Chiqim"
              value={formatMoney(shown.outflow)}
              icon={ArrowDownRight}
              iconBg="bg-red-50"
              iconColor="text-red-600"
            />
            <StatCard
              label="Davr oxiridagi qoldiq"
              value={formatMoney(shown.closing)}
              icon={Landmark}
              iconBg={shown.closing >= 0 ? 'bg-brand-50' : 'bg-red-50'}
              iconColor={shown.closing >= 0 ? 'text-brand-600' : 'text-red-600'}
              hint={`Sof: ${formatSignedMoney(shown.net)}`}
            />
          </div>

          <Card className="flex flex-wrap items-center justify-between gap-3 p-4">
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-sm font-medium text-slate-600">Hisob:</span>
              {views.map((v) => (
                <button
                  key={v.value}
                  type="button"
                  onClick={() => setView(v.value)}
                  className={cn(
                    'rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
                    view === v.value
                      ? 'bg-brand-50 text-brand-700'
                      : 'text-slate-500 hover:bg-slate-100',
                  )}
                >
                  {v.label}
                </button>
              ))}
            </div>
            <Button variant="secondary" onClick={handleExport} disabled={shown.months.length === 0}>
              <Download className="h-4 w-4" /> CSV
            </Button>
          </Card>

          <Card>
            <h2 className="mb-4 font-semibold text-slate-800">
              Oylar bo'yicha pul harakati va qoldiq
            </h2>
            {shown.months.length === 0 ? (
              <p className="py-12 text-center text-slate-400">Bu hisobda oylik yozuv yo'q</p>
            ) : (
              <CashFlowChart months={shown.months} />
            )}
          </Card>

          <Card className="p-0">
            <div className="border-b border-slate-100 p-4">
              <h2 className="font-semibold text-slate-800">Oylik jadval</h2>
              <p className="text-sm text-slate-400">
                Har oyning boshi va oxiridagi qoldiq — davr boshidan uzluksiz ko'chadi.
              </p>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-3">Oy</th>
                    <th className="px-4 py-3 text-right">Boshlang'ich</th>
                    <th className="px-4 py-3 text-right">Kirim</th>
                    <th className="px-4 py-3 text-right">Chiqim</th>
                    <th className="px-4 py-3 text-right">Sof</th>
                    <th className="px-4 py-3 text-right">Yakuniy</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {shown.months.map((m) => (
                    <tr key={m.month} className="hover:bg-slate-50/60">
                      <td className="px-4 py-3 font-medium text-slate-700">
                        {formatMonthLabel(m.month)}
                      </td>
                      <td className="px-4 py-3 text-right text-slate-500">
                        {formatMoney(m.opening)}
                      </td>
                      <td className="px-4 py-3 text-right text-emerald-600">
                        {m.inflow === 0 ? (
                          <span className="text-slate-300">—</span>
                        ) : (
                          formatMoney(m.inflow)
                        )}
                      </td>
                      <td className="px-4 py-3 text-right text-red-600">
                        {m.outflow === 0 ? (
                          <span className="text-slate-300">—</span>
                        ) : (
                          formatMoney(m.outflow)
                        )}
                      </td>
                      <td className={cn('px-4 py-3 text-right font-medium', signClass(m.net))}>
                        {formatSignedMoney(m.net)}
                      </td>
                      <td
                        className={cn(
                          'px-4 py-3 text-right font-semibold',
                          m.closing < 0 ? 'text-red-600' : 'text-slate-700',
                        )}
                      >
                        {formatMoney(m.closing)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>

          {/* Toifalar kesimi — §2.7 F7.01, F7.03 */}
          <CategoriesCard from={from} to={to} account={view === 'all' ? undefined : view} />

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            {data.accounts.map((a) => (
              <Card key={a.account} className="p-4">
                <p className="text-xs font-medium uppercase tracking-wide text-slate-400">
                  {accountLabel(a.account)}
                </p>
                <p
                  className={cn(
                    'mt-1 text-lg font-semibold',
                    a.closing < 0 ? 'text-red-600' : 'text-slate-800',
                  )}
                >
                  {formatMoney(a.closing)}
                </p>
                <p className="mt-1 text-xs text-slate-400">
                  Kirim {formatMoney(a.inflow)} · Chiqim {formatMoney(a.outflow)}
                </p>
              </Card>
            ))}
          </div>
        </div>
      )}
    </ReportState>
  )
}

/* ==================================================================
   TOIFALAR KESIMI (§2.7 F7.01, F7.03)
   ================================================================== */

/**
 * "Pul QAYERDAN kirdi va QAYERGA ketdi" — oylar bo'yicha.
 *
 * Manba: `GET /admin/finance/cashflow/statement`. Kirim to'lovlarning
 * TAQSIMOTI bo'yicha bo'linadi (taqsimlanmagani — avans), chiqim esa
 * chiqim hisobi bo'yicha. Yig'indisi yuqoridagi "Kirim"/"Chiqim" bilan
 * bir xil bo'ladi — bu server tomonda tuzilish bilan kafolatlangan.
 *
 * Belgi: chiqim qatorlari MANFIY (pul oqimi bo'yicha), storno esa o'z
 * toifasining chiqimi bo'lib turadi.
 *
 * Davr 12 oydan uzun bo'lsa server 400 beradi — o'shanda bu blok
 * xabarni ko'rsatadi, qolgan ekran esa ishlayveradi.
 */
function CategoriesCard({
  from,
  to,
  account,
}: {
  from: string
  to: string
  account?: string
}) {
  const { data, loading, error, refetch } = useAsync(
    () => getCashFlowStatement(from, to, account),
    [from, to, account],
  )
  const [details, setDetails] = useState<LedgerDetailsRequest | null>(null)

  const handleExport = () => {
    if (!data) return
    exportToCsv(
      `pul-oqimi-toifalar_${data.from}_${data.to}.csv`,
      ["Bo'lim", 'Toifa', ...data.months, 'Jami'],
      data.sections.flatMap((section) =>
        section.rows.map((row) => [
          section.label,
          row.label,
          ...row.months.map((m) => String(m.amount)),
          String(row.total.amount),
        ]),
      ),
    )
  }

  return (
    <>
      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 p-4">
          <div>
            <h2 className="font-semibold text-slate-800">Toifalar bo'yicha</h2>
            <p className="text-sm text-slate-400">
              Kirim — to'lov taqsimotidan, chiqim — chiqim toifasidan · katakni bosing
            </p>
          </div>
          <Button variant="secondary" onClick={handleExport} disabled={!data}>
            <Download className="h-4 w-4" /> CSV
          </Button>
        </div>

        {loading ? (
          <p className="p-6 text-center text-sm text-slate-400">Yuklanmoqda…</p>
        ) : error ? (
          <div className="p-6 text-center text-sm text-slate-500">
            <p>{error}</p>
            <button
              type="button"
              onClick={refetch}
              className="mt-2 rounded-lg bg-slate-100 px-3 py-1.5 text-sm font-medium text-slate-600 hover:bg-slate-200"
            >
              Qayta urinish
            </button>
          </div>
        ) : !data || data.sections.length === 0 ? (
          <p className="p-6 text-center text-sm text-slate-400">Bu davrda pul harakati yo'q.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Toifa</th>
                  {data.months.map((m) => (
                    <th key={m} className="px-2 py-3 text-right">
                      {formatMonthLabel(m)}
                    </th>
                  ))}
                  <th className="px-4 py-3 text-right">Jami</th>
                </tr>
              </thead>

              <tbody className="divide-y divide-slate-100">
                {data.sections.map((section) => (
                  <Fragment key={section.kind}>
                    <tr className="border-t border-slate-200 bg-slate-50/60 font-semibold">
                      <td className="px-4 py-2.5 text-slate-800">{section.label}</td>
                      {section.months.map((cell, i) => (
                        <td
                          key={data.months[i]}
                          className={cn('px-2 py-2.5 text-right', signClass(cell.amount))}
                        >
                          {cell.amount === 0 ? '—' : formatSignedMoney(cell.amount)}
                        </td>
                      ))}
                      <td className={cn('px-4 py-2.5 text-right', signClass(section.total.amount))}>
                        {formatSignedMoney(section.total.amount)}
                      </td>
                    </tr>

                    {section.rows.map((row) => (
                      <tr key={row.key} className="hover:bg-slate-50/60">
                        <td className="px-4 py-2 text-slate-600">
                          {row.label}
                          {/*
                            Qarama-qarshi tomon — STORNO: tushum qatorida u
                            chiqim, chiqim qatorida esa kirim bo'lib turadi.
                          */}
                          {row.total.inflow !== 0 && row.total.outflow !== 0 && (
                            <span className="ml-1.5 text-xs text-amber-600">
                              (storno{' '}
                              {formatMoney(
                                row.kind === 'expense' ? row.total.inflow : row.total.outflow,
                              )}
                              )
                            </span>
                          )}
                        </td>
                        {row.months.map((cell, i) => (
                          <td key={data.months[i]} className="px-2 py-2 text-right">
                            {cell.amount === 0 ? (
                              <span className="text-slate-300">—</span>
                            ) : (
                              <button
                                type="button"
                                title="Satrlarini ko'rish"
                                onClick={() =>
                                  setDetails({
                                    source: 'cashflow',
                                    title: row.label,
                                    subtitle: formatMonthLabel(data.months[i]),
                                    key: row.key,
                                    from: `${data.months[i]}-01`,
                                    to: lastDayOfMonth(data.months[i]),
                                    account,
                                  })
                                }
                                className={cn(
                                  'rounded px-1 underline-offset-2 hover:underline',
                                  signClass(cell.amount),
                                )}
                              >
                                {formatSignedMoney(cell.amount)}
                              </button>
                            )}
                          </td>
                        ))}
                        <td className="px-4 py-2 text-right font-medium">
                          <button
                            type="button"
                            title="Davr bo'yicha satrlar"
                            onClick={() =>
                              setDetails({
                                source: 'cashflow',
                                title: row.label,
                                subtitle: `${section.label} · davr bo'yicha`,
                                key: row.key,
                                from: data.from,
                                to: data.to,
                                account,
                              })
                            }
                            className={cn(
                              'rounded px-1 underline-offset-2 hover:underline',
                              signClass(row.total.amount),
                            )}
                          >
                            {formatSignedMoney(row.total.amount)}
                          </button>
                        </td>
                      </tr>
                    ))}
                  </Fragment>
                ))}

                <tr className="border-t-2 border-slate-200 bg-slate-50/70 font-semibold">
                  <td className="px-4 py-2.5 text-slate-800">Sof pul oqimi</td>
                  {data.monthTotals.map((cell, i) => (
                    <td
                      key={data.months[i]}
                      className={cn('px-2 py-2.5 text-right', signClass(cell.amount))}
                    >
                      {formatSignedMoney(cell.amount)}
                    </td>
                  ))}
                  <td className={cn('px-4 py-2.5 text-right', signClass(data.total.amount))}>
                    {formatSignedMoney(data.total.amount)}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <LedgerDetailsModal request={details} onClose={() => setDetails(null)} />
    </>
  )
}

/** "2026-03" → "2026-03-31" */
function lastDayOfMonth(month: string): string {
  const [y, m] = month.split('-').map(Number)
  const last = new Date(Date.UTC(y, m, 0)).getUTCDate()
  return `${month}-${String(last).padStart(2, '0')}`
}
