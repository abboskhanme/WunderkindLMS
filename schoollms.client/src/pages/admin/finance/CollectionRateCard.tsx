/**
 * Yig'ilish darajasi — oylar kesimida hisoblangan va yig'ilgan (P1-18).
 *
 * Manba: `GET /api/admin/finance/collection-rate`. OY — hisob-faktura oyi
 * (`period_month`), pul kelgan kun EMAS. Ya'ni oktyabrda to'langan sentyabr
 * puli sentyabr qatoriga tushadi; shu sababli har qatorning qoldig'i
 * qarzdorlar ro'yxatidagi o'sha oyning qarziga to'g'ri keladi.
 *
 * "Qaysi oyda qancha PUL tushdi" degan boshqa savolga "Pul oqimi" tabi
 * javob beradi — ikkovi bir xil emas va shuning uchun bir joyda
 * ko'rsatilmaydi.
 */
import { useAsync } from '@/hooks/useAsync'
import { getCollectionRate } from '@/api/services/financeReports'
import { Card } from '@/components/ui/Card'
import { cn, formatMoney } from '@/lib/utils'
import { ReportState } from './ReportState'
import { formatMonthLabel } from './reportLabels'

/** Oxirgi shuncha oy ko'rsatiladi — undan narisi hisobot emas, arxiv. */
const MONTHS_SHOWN = 12

function rateClass(rate: number | null): string {
  if (rate === null) return 'text-slate-400'
  if (rate >= 90) return 'text-emerald-600'
  if (rate >= 70) return 'text-amber-600'
  return 'text-red-600'
}

export function CollectionRateCard() {
  const { data, loading, error, refetch } = useAsync(() => getCollectionRate(), [])

  const months = (data ?? []).slice(-MONTHS_SHOWN).reverse()

  return (
    <ReportState
      loading={loading}
      error={error}
      isEmpty={months.length === 0}
      emptyTitle="Yig'ilish darajasi uchun ma'lumot yo'q"
      emptyHint="Hali birorta oy hisoblanmagan."
      onRetry={refetch}
    >
      <Card className="p-0">
        <div className="border-b border-slate-100 p-4">
          <h2 className="font-semibold text-slate-800">Yig'ilish darajasi</h2>
          <p className="text-sm text-slate-400">
            Hisob-faktura oyi bo'yicha: hisoblangan va shu oyga tushgan pul
          </p>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-4 py-3">Oy</th>
                <th className="px-4 py-3 text-right">Hisoblangan</th>
                <th className="px-4 py-3 text-right">Yig'ilgan</th>
                <th className="px-4 py-3 text-right">Qoldiq</th>
                <th className="px-4 py-3 text-right">Foiz</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {months.map((m) => (
                <tr key={m.periodMonth} className="hover:bg-slate-50/60">
                  <td className="px-4 py-3 font-medium text-slate-700">
                    {formatMonthLabel(m.periodMonth)}
                  </td>
                  <td className="px-4 py-3 text-right text-slate-600">
                    {formatMoney(m.accrued)}
                  </td>
                  <td className="px-4 py-3 text-right text-emerald-600">
                    {formatMoney(m.collected)}
                  </td>
                  {/* Qoldiq — serverning ikki raqami ayirmasi, faqat ko'rsatish uchun. */}
                  <td className="px-4 py-3 text-right text-red-600">
                    {formatMoney(m.accrued - m.collected)}
                  </td>
                  <td className={cn('px-4 py-3 text-right font-semibold', rateClass(m.collectionRate))}>
                    {m.collectionRate === null ? '—' : `${m.collectionRate}%`}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
    </ReportState>
  )
}
