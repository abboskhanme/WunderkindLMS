/**
 * Z-HISOBOT — kunlik smenalar, kassir kesimida (SPEC §4.2, §4.6) — P1-18.
 *
 * Manba: `GET /api/cash/shifts` (ro'yxat) va `GET /api/cash/shifts/{id}/
 * z-report` (yakun). Direktor va admin hamma kassirni ko'radi; kassir bu
 * tabga umuman kirmaydi (SPEC §4.3).
 *
 * KUTILGAN / SANALGAN / FARQ uchtasi ham serverdan keladi. `variance`
 * bazada generated column — ya'ni yopilgandan keyin uni hech kim, hech
 * qanday endpoint orqali tuzata olmaydi. Ekranda ham u faqat KO'RSATILADI:
 * bu jadvalda tahrirlash tugmasi yo'q.
 */
import { useState } from 'react'
import { Banknote, CreditCard, Receipt, Scale } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import { getCashShifts, getZReport } from '@/api/services/financeReports'
import type { CashShift } from '@/types'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { StatCard } from '@/components/ui/StatCard'
import { cn, formatDate, formatMoney } from '@/lib/utils'
import { ReportState } from './ReportState'
import { formatDateTime, formatSignedMoney, paymentMethodLabel } from './reportLabels'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

const todayStr = new Date().toISOString().slice(0, 10)

/** Smena farqi bor-yo'qligi. Ochiq smenada `variance` yo'q — farq ham yo'q. */
function varianceOf(shift: CashShift): number | null {
  return typeof shift.variance === 'number' ? shift.variance : null
}

export function ZReportTab() {
  const [date, setDate] = useState(todayStr)
  const [selected, setSelected] = useState<CashShift | null>(null)

  const { data, loading, error, refetch } = useAsync(
    () => getCashShifts({ from: date, to: date }),
    [date],
  )

  const shifts = data ?? []
  const cashTotal = shifts.reduce((sum, s) => sum + s.cashTotal, 0)
  const nonCashTotal = shifts.reduce((sum, s) => sum + s.nonCashTotal, 0)
  const varianceTotal = shifts.reduce((sum, s) => sum + (varianceOf(s) ?? 0), 0)
  const withVariance = shifts.filter((s) => (varianceOf(s) ?? 0) !== 0).length

  return (
    <div className="space-y-6">
      <Card className="flex flex-wrap items-center gap-3 p-4">
        <span className="text-sm font-medium text-slate-600">Kun:</span>
        <input
          type="date"
          value={date}
          onChange={(e) => setDate(e.target.value)}
          className={control}
        />
        <Button variant="ghost" onClick={() => setDate(todayStr)} disabled={date === todayStr}>
          Bugun
        </Button>
        <span className="text-sm text-slate-400">{formatDate(date)}</span>
      </Card>

      <ReportState
        loading={loading}
        error={error}
        isEmpty={shifts.length === 0}
        emptyTitle="Bu kunda smena ochilmagan"
        emptyHint="Boshqa kunni tanlang — smenalar kassir smenani ochgan kun bo'yicha guruhlanadi."
        onRetry={refetch}
      >
        <div className="space-y-6">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard
              label="Smenalar"
              value={String(shifts.length)}
              icon={Receipt}
              iconBg="bg-brand-50"
              iconColor="text-brand-600"
              hint={`${withVariance} tasida farq bor`}
            />
            <StatCard
              label="Naqd tushum"
              value={formatMoney(cashTotal)}
              icon={Banknote}
              iconBg="bg-emerald-50"
              iconColor="text-emerald-600"
              hint="Smena yopilishida SHU sanaladi"
            />
            <StatCard
              label="Naqdsiz tushum"
              value={formatMoney(nonCashTotal)}
              icon={CreditCard}
              iconBg="bg-slate-100"
              iconColor="text-slate-500"
              hint="Karta, o'tkazma, onlayn — bankka"
            />
            <StatCard
              label="Jami farq"
              value={formatSignedMoney(varianceTotal)}
              icon={Scale}
              iconBg={varianceTotal === 0 ? 'bg-emerald-50' : 'bg-red-50'}
              iconColor={varianceTotal === 0 ? 'text-emerald-600' : 'text-red-600'}
              hint="Sanalgan − kutilgan"
            />
          </div>

          <Card className="p-0">
            <div className="border-b border-slate-100 p-4">
              <h2 className="font-semibold text-slate-800">Smenalar — kassirlar kesimida</h2>
              <p className="text-sm text-slate-400">
                Farqi bor qator ajratib ko'rsatilgan · batafsil yakun uchun "Z-hisobot"
              </p>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-3">Kassir</th>
                    <th className="px-4 py-3">Ochilgan</th>
                    <th className="px-4 py-3">Yopilgan</th>
                    <th className="px-4 py-3 text-right">To'lovlar</th>
                    <th className="px-4 py-3 text-right">Kutilgan</th>
                    <th className="px-4 py-3 text-right">Sanalgan</th>
                    <th className="px-4 py-3 text-right">Farq</th>
                    <th className="px-4 py-3 text-right">Yakun</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {shifts.map((s) => {
                    const variance = varianceOf(s)
                    const flagged = (variance ?? 0) !== 0
                    return (
                      <tr
                        key={s.id}
                        className={cn(
                          flagged
                            ? (variance ?? 0) < 0
                              ? 'bg-red-50/70'
                              : 'bg-amber-50/70'
                            : 'hover:bg-slate-50/60',
                        )}
                      >
                        <td className="px-4 py-3">
                          <p className="font-medium text-slate-800">{s.cashierName}</p>
                          <span
                            className={cn(
                              'mt-0.5 inline-block rounded-md px-2 py-0.5 text-xs font-medium',
                              s.status === 'open'
                                ? 'bg-brand-50 text-brand-700'
                                : 'bg-slate-100 text-slate-600',
                            )}
                          >
                            {s.status === 'open' ? 'Ochiq' : 'Yopilgan'}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-slate-500">{formatDateTime(s.openedAt)}</td>
                        <td className="px-4 py-3 text-slate-500">
                          {s.closedAt ? formatDateTime(s.closedAt) : '—'}
                        </td>
                        <td className="px-4 py-3 text-right text-slate-600">{s.paymentsCount}</td>
                        <td className="px-4 py-3 text-right text-slate-600">
                          {typeof s.expectedCash === 'number' ? formatMoney(s.expectedCash) : '—'}
                        </td>
                        <td className="px-4 py-3 text-right text-slate-600">
                          {typeof s.countedCash === 'number' ? formatMoney(s.countedCash) : '—'}
                        </td>
                        <td
                          className={cn(
                            'px-4 py-3 text-right font-semibold',
                            variance === null
                              ? 'text-slate-300'
                              : variance === 0
                                ? 'text-emerald-600'
                                : variance < 0
                                  ? 'text-red-700'
                                  : 'text-amber-700',
                          )}
                        >
                          {variance === null ? '—' : formatSignedMoney(variance)}
                        </td>
                        <td className="px-4 py-3 text-right">
                          <Button variant="secondary" onClick={() => setSelected(s)}>
                            Z-hisobot
                          </Button>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          </Card>
        </div>
      </ReportState>

      {selected && <ZReportModal shift={selected} onClose={() => setSelected(null)} />}
    </div>
  )
}

/** Bitta smenaning yakuni: usullar, toifalar, chek oralig'i, storno soni. */
function ZReportModal({ shift, onClose }: { shift: CashShift; onClose: () => void }) {
  const { data, loading, error, refetch } = useAsync(() => getZReport(shift.id), [shift.id])

  return (
    <Modal open onClose={onClose} title={`Z-hisobot — ${shift.cashierName}`} size="lg">
      <ReportState loading={loading} error={error} onRetry={refetch}>
        {data && (
          <div className="space-y-5">
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              <Figure label="Ochilish qoldig'i" value={formatMoney(data.shift.openingFloat)} />
              <Figure
                label="Kutilgan naqd"
                value={
                  typeof data.shift.expectedCash === 'number'
                    ? formatMoney(data.shift.expectedCash)
                    : 'Smena ochiq'
                }
              />
              <Figure
                label="Sanalgan naqd"
                value={
                  typeof data.shift.countedCash === 'number'
                    ? formatMoney(data.shift.countedCash)
                    : 'Smena ochiq'
                }
              />
              <Figure
                label="Farq"
                value={
                  typeof data.shift.variance === 'number'
                    ? formatSignedMoney(data.shift.variance)
                    : '—'
                }
                valueClass={
                  typeof data.shift.variance === 'number' && data.shift.variance !== 0
                    ? 'text-red-600'
                    : 'text-slate-800'
                }
              />
            </div>

            <div>
              <h4 className="mb-2 text-sm font-semibold text-slate-700">
                To'lov usullari bo'yicha
              </h4>
              {data.byMethod.length === 0 ? (
                <p className="text-sm text-slate-400">Bu smenada to'lov yo'q</p>
              ) : (
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-3 py-2">Usul</th>
                      <th className="px-3 py-2 text-right">Soni</th>
                      <th className="px-3 py-2 text-right">Summa</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {data.byMethod.map((m) => (
                      <tr key={m.method}>
                        <td className="px-3 py-2 text-slate-600">
                          {paymentMethodLabel(m.method)}
                          {m.method === 'cash' && (
                            <span className="ml-2 rounded bg-emerald-50 px-1.5 py-0.5 text-xs text-emerald-700">
                              sanaladi
                            </span>
                          )}
                        </td>
                        <td className="px-3 py-2 text-right text-slate-500">{m.count}</td>
                        <td className="px-3 py-2 text-right font-medium text-slate-700">
                          {formatMoney(m.amount)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>

            <div>
              <h4 className="mb-2 text-sm font-semibold text-slate-700">Toifalar bo'yicha</h4>
              {data.byCategory.length === 0 ? (
                <p className="text-sm text-slate-400">Taqsimlangan to'lov yo'q</p>
              ) : (
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-3 py-2">Toifa</th>
                      <th className="px-3 py-2 text-right">Summa</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {data.byCategory.map((c) => (
                      <tr key={c.categoryId}>
                        <td className="px-3 py-2 text-slate-600">{c.categoryName}</td>
                        <td className="px-3 py-2 text-right font-medium text-slate-700">
                          {formatMoney(c.amount)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>

            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
              <Figure
                label="Cheklar"
                value={
                  typeof data.receiptFrom === 'number' && typeof data.receiptTo === 'number'
                    ? `№${data.receiptFrom} — №${data.receiptTo}`
                    : "Chek yo'q"
                }
              />
              <Figure label="To'lovlar" value={String(data.shift.paymentsCount)} />
              <Figure
                label="Storno"
                value={String(data.reversalsCount)}
                valueClass={data.reversalsCount > 0 ? 'text-amber-600' : 'text-slate-800'}
              />
            </div>

            {data.shift.closedByName && (
              <p className="text-xs text-slate-400">
                Smenani yopgan: {data.shift.closedByName}
                {data.shift.closedAt && ` · ${formatDateTime(data.shift.closedAt)}`}
              </p>
            )}
          </div>
        )}
      </ReportState>
    </Modal>
  )
}

function Figure({
  label,
  value,
  valueClass = 'text-slate-800',
}: {
  label: string
  value: string
  valueClass?: string
}) {
  return (
    <div className="rounded-xl bg-slate-50 p-3">
      <p className="text-xs font-medium uppercase tracking-wide text-slate-400">{label}</p>
      <p className={cn('mt-0.5 text-sm font-semibold', valueClass)}>{value}</p>
    </div>
  )
}
