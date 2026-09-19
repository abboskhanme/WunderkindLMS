/**
 * P&L 2.0 — "Rejalashtirilgan chiqim" tabi.
 *
 * <b>Bu tab F6.01 ni QURMAYDI</b> — shablonlar CRUD'i (`expense_templates`,
 * yaratish/tahrirlash/o'chirish, eslatmalar) BOSHQA vazifaning ishi
 * (Moliya sozlamalari ekrani). Bu yerda faqat ISTE'MOL: `GET
 * /api/admin/finance/expense-templates` dan kelgan ro'yxatni ko'rsatadi,
 * shunda P&L 2.0 ekrani "to'liq" bo'ladi (har tab biror narsa ko'rsatadi,
 * bo'sh joy qolmaydi).
 *
 * EduSchool'dagi to'liq "planned" tabidan farqi (§2.6.1): u yerda oylik
 * KPI (reja/hisoblangan/hisoblanmagan + stavka) va holat ustuni
 * (over/under/unplanned/corrected) bor — bular OYLIK ACCRUAL holatini
 * bildiradi, hozircha shunday endpoint yo'q (faqat shablon KATALOGI).
 * Shuning uchun bu yerda faqat katalog — soxta KPI chizilmaydi.
 */
import { AlertCircle, Wallet } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import { getExpenseTemplates } from '@/api/services/financeReports'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { formatMoney } from '@/lib/utils'
import { ReportState } from './ReportState'

export function PlannedExpenseTab() {
  const { data, loading, error, refetch } = useAsync(() => getExpenseTemplates(), [])

  if (loading) {
    return (
      <Card>
        <p className="py-12 text-center text-slate-400">Yuklanmoqda...</p>
      </Card>
    )
  }

  if (data && !data.available) {
    return (
      <Card className="border-amber-200 bg-amber-50/60">
        <div className="flex flex-col items-center gap-3 py-8 text-center">
          <AlertCircle className="h-8 w-8 text-amber-500" />
          <div>
            <p className="font-medium text-amber-800">Bu tab hali ulanmagan</p>
            <p className="mt-1 max-w-lg text-sm text-amber-700/80">{data.reason}</p>
          </div>
        </div>
      </Card>
    )
  }

  const templates = data?.available ? data.templates : []
  const activeTotal = templates.filter((t) => t.isActive).reduce((s, t) => s + t.amount, 0)

  return (
    <div className="space-y-5">
      <ReportState
        loading={false}
        error={error}
        isEmpty={!!data?.available && templates.length === 0}
        emptyTitle="Rejalashtirilgan chiqim shabloni yo'q"
        emptyHint="Shablonlar Moliya sozlamalari bo'limida qo'shiladi."
        onRetry={refetch}
      >
        <div className="space-y-5">
          <StatCard
            label="Faol shablonlar bo'yicha jami (oylik)"
            value={formatMoney(activeTotal)}
            icon={Wallet}
            iconBg="bg-brand-50"
            iconColor="text-brand-600"
            hint={`${templates.filter((t) => t.isActive).length} ta faol shablon`}
          />

          <Card className="p-0">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[40rem] text-left text-sm">
                <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-3">Nomi</th>
                    <th className="px-4 py-3">Toifa</th>
                    <th className="px-4 py-3 text-right">Summa</th>
                    <th className="px-4 py-3 text-right">Oyning kuni</th>
                    <th className="px-4 py-3">Holat</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {templates.map((t) => (
                    <tr key={t.id} className="hover:bg-slate-50/60">
                      <td className="px-4 py-2.5 text-slate-700">{t.name}</td>
                      <td className="px-4 py-2.5 text-slate-500">{t.categoryName}</td>
                      <td className="px-4 py-2.5 text-right text-slate-700">{formatMoney(t.amount)}</td>
                      <td className="px-4 py-2.5 text-right text-slate-500">{t.dayOfMonth}</td>
                      <td className="px-4 py-2.5">
                        <span
                          className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${
                            t.isActive ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500'
                          }`}
                        >
                          {t.isActive ? 'Faol' : 'Nofaol'}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>

          <Card className="bg-slate-50/60 text-xs text-slate-500">
            Bu — shablonlar KATALOGI. Oylik hisoblangan/hisoblanmagan holati, eslatmalar va
            "reja vs fakt" solishtiruvi (EduSchool'dagi to'liq "planned" tabi) hali qurilmagan.
          </Card>
        </div>
      </ReportState>
    </div>
  )
}
