/**
 * Qarzdorlar — TOIFA KESIMIDA (P1-18) + ULAR BILAN ISHLASH (§3.5).
 *
 * Manba: `GET /api/admin/finance/debtors`. Qarz har safar `invoices` va
 * `payment_allocations` dan hisoblanadi — o'quvchi qatoridagi saqlangan
 * qoldiqdan EMAS (P1-13 qoidasi). Server jami qarz bo'yicha kamayish
 * tartibida qaytaradi va bu tartib bu yerda BUZILMAYDI.
 *
 * ASOSIY TALAB: maktab / avtobus / yotoqxona qarzi ARALASHMAYDI. Har toifa
 * o'z ustuniga tushadi va yuqorida o'z yig'indisi bor — direktorga "avtobus
 * pulini kim to'lamadi" degan savol bitta ustunda ko'rinadi.
 *
 * §3.5 — QARZ HAQIDA NIMA QILINGANI. Ro'yxat endi faqat "kim qancha qarzdor"
 * emas, "u bilan nima qilindi" ham: joriy holat, oxirgi amal sanasi va
 * ota-ona va'da qilgan to'lov sanasi. Ular IKKINCHI so'rovdan keladi
 * (`GET /admin/finance/debtors/workflow`) va bu yerda `studentId` bo'yicha
 * birlashtiriladi.
 *
 * Nega ikkita so'rov, bitta emas: qarz arifmetikasi (`/debtors`) va ish
 * oqimi (`/debtors/workflow`) — ikki xil vazifa va ikki xil fayl egasi.
 * Bitta endpoint ularni birlashtirsa, bir raqam ikki joyda hisoblanishi
 * mumkin bo'lgan joy paydo bo'lardi.
 *
 * "Va'da buzildi" hukmini SERVER chiqaradi (`promiseBroken`): u sanani ham,
 * qarz hali ochiqligini ham BIRGA tekshiradi. Brauzer bu yerda faqat
 * qizil nishon chizadi.
 */
import { useMemo, useState } from 'react'
import { AlertTriangle, CalendarClock, Download, MessageSquarePlus, Users, Wallet } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import { getDebtors } from '@/api/services/financeReports'
import { getDebtorWorkflow, type DebtorWorkflowRow } from '@/api/services/debtorWorkflow'
import type { DebtorRow } from '@/types'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { StatCard } from '@/components/ui/StatCard'
import { cn, exportToCsv, formatMoney } from '@/lib/utils'
import { ReportState } from './ReportState'
import { CollectionRateCard } from './CollectionRateCard'
import { DebtorActionModal } from './DebtorActionModal'
import { formatDateTime, formatMonthLabel } from './reportLabels'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/** Toifalarning ekrandagi tartibi — mijoz shu ketma-ketlikda o'ylaydi. */
const categoryOrder = ['tuition', 'bus', 'dormitory', 'meals', 'other']

interface CategoryColumn {
  code: string
  name: string
}

/** Jadval ustunlari: qaysi toifada haqiqatan qarz bor — o'sha ustun chiqadi. */
function categoryColumns(rows: DebtorRow[]): CategoryColumn[] {
  const found = new Map<string, string>()
  for (const row of rows) {
    for (const c of row.byCategory) {
      if (!found.has(c.categoryCode)) found.set(c.categoryCode, c.categoryName)
    }
  }
  return [...found.entries()]
    .map(([code, name]) => ({ code, name }))
    .sort((a, b) => {
      const ai = categoryOrder.indexOf(a.code)
      const bi = categoryOrder.indexOf(b.code)
      if (ai !== -1 && bi !== -1) return ai - bi
      if (ai !== -1) return -1
      if (bi !== -1) return 1
      return a.name.localeCompare(b.name)
    })
}

function debtOf(row: DebtorRow, code: string): number {
  return row.byCategory.find((c) => c.categoryCode === code)?.debt ?? 0
}

export function DebtorsTab() {
  const [onlyOverdue, setOnlyOverdue] = useState(false)
  // Sukut bo'yicha YOQIQ: maktabdan ketgan o'quvchining qarzi ham qarz
  // (FinanceReportQueries.DebtorReportQuery izohi). Uni yashirish —
  // direktorning ongli qarori bo'lishi kerak, sukut emas.
  const [includeArchived, setIncludeArchived] = useState(true)
  const [className, setClassName] = useState('')
  const [search, setSearch] = useState('')

  const [acting, setActing] = useState<DebtorRow | null>(null)

  const { data, loading, error, refetch } = useAsync(
    () => getDebtors({ onlyOverdue, includeArchived }),
    [onlyOverdue, includeArchived],
  )

  // Ish oqimi — ALOHIDA so'rov (§3.5). Sinf filtri serverga BERILMAYDI:
  // qarzdorlar ro'yxati ham to'liq keladi va filtr ekranda qo'llanadi, ya'ni
  // ikkovi har doim bir xil qatorlarni ko'rsatadi.
  const workflow = useAsync(() => getDebtorWorkflow(), [])

  const rows = useMemo(() => data ?? [], [data])

  /** `studentId` → ish oqimi qatori. Amali yo'q o'quvchi xaritada BO'LMAYDI. */
  const workflowBy = useMemo(() => {
    const map = new Map<string, DebtorWorkflowRow>()
    for (const w of workflow.data ?? []) map.set(w.studentId, w)
    return map
  }, [workflow.data])

  const classes = useMemo(
    () => [...new Set(rows.map((r) => r.className))].sort((a, b) => a.localeCompare(b)),
    [rows],
  )

  const visible = useMemo(() => {
    const q = search.trim().toLowerCase()
    return rows.filter(
      (r) =>
        (!className || r.className === className) &&
        (!q || r.fullName.toLowerCase().includes(q) || r.parentPhone.includes(q)),
    )
  }, [rows, className, search])

  const columns = useMemo(() => categoryColumns(visible), [visible])

  // Ro'yxat yig'indilari — ekranda ko'rinib turgan qatorlar bo'yicha.
  // Har bir qator summasi serverdan tayyor keladi, bu yerda faqat qo'shiladi.
  const totals = useMemo(() => {
    const byCategory = new Map<string, number>()
    let debt = 0
    let overdueDebt = 0
    // "Buzilgan va'da" — SERVER hukmi (`promiseBroken`): sana ham, qarz ham
    // u yerda tekshirilgan. Bu yerda faqat ekrandagi qatorlar sanaladi.
    let brokenPromises = 0
    for (const r of visible) {
      debt += r.debt
      if (r.daysOverdue > 0) overdueDebt += r.debt
      if (workflowBy.get(r.studentId)?.promiseBroken) brokenPromises += 1
      for (const c of r.byCategory) {
        byCategory.set(c.categoryCode, (byCategory.get(c.categoryCode) ?? 0) + c.debt)
      }
    }
    return { debt, overdueDebt, byCategory, brokenPromises, count: visible.length }
  }, [visible, workflowBy])

  const handleExport = () => {
    exportToCsv(
      'qarzdorlar.csv',
      [
        "O'quvchi",
        'Sinf',
        'Telefon',
        ...columns.map((c) => c.name),
        'Jami qarz',
        'Kechikish (kun)',
        'Eng eski oy',
        'Holat',
        'Oxirgi amal',
        "Va'da",
      ],
      visible.map((r) => {
        const w = workflowBy.get(r.studentId)
        return [
          r.fullName,
          r.className,
          r.parentPhone,
          ...columns.map((c) => String(debtOf(r, c.code))),
          String(r.debt),
          String(r.daysOverdue),
          r.oldestUnpaidMonth ? formatMonthLabel(r.oldestUnpaidMonth) : '',
          w?.statusName ?? '',
          w?.lastActionAt ? formatDateTime(w.lastActionAt) : '',
          w?.promisedOn ?? '',
        ]
      }),
    )
  }

  return (
    <div className="space-y-6">
      <Card className="flex flex-wrap items-center gap-3 p-4">
        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="O'quvchi yoki telefon"
          className={cn(control, 'min-w-56')}
        />
        <select
          value={className}
          onChange={(e) => setClassName(e.target.value)}
          className={control}
        >
          <option value="">Barcha sinflar</option>
          {classes.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </select>
        <label className="flex items-center gap-2 text-sm text-slate-600">
          <input
            type="checkbox"
            checked={onlyOverdue}
            onChange={(e) => setOnlyOverdue(e.target.checked)}
            className="h-4 w-4 rounded border-slate-300 text-brand-600"
          />
          Faqat muddati o'tganlar
        </label>
        <label className="flex items-center gap-2 text-sm text-slate-600">
          <input
            type="checkbox"
            checked={includeArchived}
            onChange={(e) => setIncludeArchived(e.target.checked)}
            className="h-4 w-4 rounded border-slate-300 text-brand-600"
          />
          Arxivdagilar ham
        </label>
        <div className="ml-auto">
          <Button variant="secondary" onClick={handleExport} disabled={visible.length === 0}>
            <Download className="h-4 w-4" /> CSV
          </Button>
        </div>
      </Card>

      <ReportState
        loading={loading}
        error={error}
        isEmpty={visible.length === 0}
        emptyTitle="Qarzdor yo'q"
        emptyHint="Tanlangan filtr bo'yicha qarzi bor o'quvchi topilmadi."
        onRetry={refetch}
      >
        <div className="space-y-6">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <StatCard
              label="Jami qarz"
              value={formatMoney(totals.debt)}
              icon={Wallet}
              iconBg="bg-red-50"
              iconColor="text-red-600"
            />
            <StatCard
              label="Qarzdorlar"
              value={String(totals.count)}
              icon={Users}
              iconBg="bg-brand-50"
              iconColor="text-brand-600"
              hint="Ekrandagi filtr bo'yicha"
            />
            <StatCard
              label="Muddati o'tgan qarz"
              value={formatMoney(totals.overdueDebt)}
              icon={AlertTriangle}
              iconBg="bg-amber-50"
              iconColor="text-amber-600"
              hint="To'lov muddati sozlamasi bo'yicha"
            />
            {/* §3.5 — va'da berilgan, sana o'tgan, qarz esa hali ochiq. */}
            <StatCard
              label="Buzilgan va'da"
              value={String(totals.brokenPromises)}
              icon={CalendarClock}
              iconBg="bg-red-50"
              iconColor="text-red-600"
              hint="Sanasi o'tdi, qarz yopilmadi"
            />
          </div>

          {/* Toifalar kesimi — maktab / avtobus / yotoqxona ALOHIDA */}
          <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
            {columns.map((c) => (
              <Card key={c.code} className="p-4">
                <p className="text-xs font-medium uppercase tracking-wide text-slate-400">
                  {c.name}
                </p>
                <p className="mt-1 text-lg font-semibold text-slate-800">
                  {formatMoney(totals.byCategory.get(c.code) ?? 0)}
                </p>
              </Card>
            ))}
          </div>

          {/* Yig'ilish darajasi — "qancha hisoblandi, qanchasi keldi" savoli. */}
          <CollectionRateCard />

          <Card className="p-0">
            <div className="border-b border-slate-100 p-4">
              <h2 className="font-semibold text-slate-800">Qarzdorlar ro'yxati</h2>
              <p className="text-sm text-slate-400">
                Jami qarz bo'yicha kamayish tartibida · qizil qator — muddati o'tgan
              </p>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-3">O'quvchi</th>
                    <th className="px-4 py-3">Sinf</th>
                    <th className="px-4 py-3">Telefon</th>
                    {columns.map((c) => (
                      <th key={c.code} className="px-4 py-3 text-right">
                        {c.name}
                      </th>
                    ))}
                    <th className="px-4 py-3 text-right">Jami qarz</th>
                    <th className="px-4 py-3 text-right">Kechikish</th>
                    <th className="px-4 py-3">Eng eski oy</th>
                    {/* §3.5 — qarz haqida NIMA QILINGANI */}
                    <th className="px-4 py-3">Holat</th>
                    <th className="px-4 py-3">Oxirgi amal</th>
                    <th className="px-4 py-3">Va'da</th>
                    <th className="px-4 py-3" />
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {visible.map((r) => {
                    const w = workflowBy.get(r.studentId)
                    return (
                      <tr
                        key={r.studentId}
                        className={cn(
                          'hover:bg-slate-50/60',
                          r.daysOverdue > 0 && 'bg-red-50/50 hover:bg-red-50',
                        )}
                      >
                        <td className="px-4 py-3 font-medium text-slate-800">{r.fullName}</td>
                        <td className="px-4 py-3">
                          <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                            {r.className}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-slate-500">{r.parentPhone || '—'}</td>
                        {columns.map((c) => {
                          const value = debtOf(r, c.code)
                          return (
                            <td
                              key={c.code}
                              className={cn(
                                'px-4 py-3 text-right',
                                value > 0
                                  ? 'text-slate-700'
                                  : value < 0
                                    ? 'text-emerald-600'
                                    : 'text-slate-300',
                              )}
                            >
                              {value === 0 ? '—' : formatMoney(value)}
                            </td>
                          )
                        })}
                        <td className="px-4 py-3 text-right font-semibold text-red-600">
                          {formatMoney(r.debt)}
                        </td>
                        <td className="px-4 py-3 text-right">
                          {r.daysOverdue > 0 ? (
                            <span className="rounded-md bg-red-100 px-2 py-0.5 text-xs font-semibold text-red-700">
                              {r.daysOverdue} kun
                            </span>
                          ) : (
                            <span className="text-xs text-slate-400">Muddatida</span>
                          )}
                        </td>
                        <td className="px-4 py-3 text-slate-500">
                          {r.oldestUnpaidMonth ? formatMonthLabel(r.oldestUnpaidMonth) : '—'}
                        </td>

                        {/* Joriy holat — eng oxirgi amalniki (server hisoblaydi). */}
                        <td className="px-4 py-3">
                          {w?.statusName ? (
                            <span
                              className="rounded-md px-2 py-0.5 text-xs font-medium"
                              style={
                                w.statusColor
                                  ? { backgroundColor: `${w.statusColor}1a`, color: w.statusColor }
                                  : undefined
                              }
                              title={w.lastComment ?? undefined}
                            >
                              {w.statusName}
                            </span>
                          ) : (
                            <span className="text-xs text-slate-300">—</span>
                          )}
                        </td>

                        <td className="px-4 py-3 text-slate-500">
                          {w?.lastActionAt ? (
                            <span title={w.lastComment ?? undefined}>
                              {formatDateTime(w.lastActionAt)}
                            </span>
                          ) : (
                            <span className="text-xs text-slate-300">Ish boshlanmagan</span>
                          )}
                        </td>

                        <td className="px-4 py-3">
                          {w?.promisedOn ? (
                            <span
                              className={cn(
                                'inline-flex items-center gap-1 text-xs font-medium',
                                w.promiseBroken ? 'text-red-600' : 'text-amber-600',
                              )}
                            >
                              {w.promiseBroken ? (
                                <AlertTriangle className="h-3.5 w-3.5" />
                              ) : (
                                <CalendarClock className="h-3.5 w-3.5" />
                              )}
                              {w.promisedOn}
                            </span>
                          ) : (
                            <span className="text-xs text-slate-300">—</span>
                          )}
                        </td>

                        <td className="px-4 py-3 text-right">
                          <Button
                            variant="ghost"
                            className="px-2 py-1"
                            onClick={() => setActing(r)}
                            title="Amal qo'shish"
                          >
                            <MessageSquarePlus className="h-4 w-4" />
                          </Button>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
                <tfoot>
                  <tr className="border-t border-slate-200 bg-slate-50/60 text-sm font-semibold">
                    <td className="px-4 py-3 text-slate-700" colSpan={3}>
                      Jami — {totals.count} ta o'quvchi
                    </td>
                    {columns.map((c) => (
                      <td key={c.code} className="px-4 py-3 text-right text-slate-700">
                        {formatMoney(totals.byCategory.get(c.code) ?? 0)}
                      </td>
                    ))}
                    <td className="px-4 py-3 text-right text-red-700">
                      {formatMoney(totals.debt)}
                    </td>
                    {/* Kechikish, eng eski oy + §3.5 ning to'rtta ustuni. */}
                    <td className="px-4 py-3" colSpan={6} />
                  </tr>
                </tfoot>
              </table>
            </div>
          </Card>
        </div>
      </ReportState>

      {acting && (
        <DebtorActionModal
          studentId={acting.studentId}
          studentName={acting.fullName}
          className={acting.className}
          debt={acting.debt}
          onClose={() => setActing(null)}
          onSaved={() => workflow.refetch()}
        />
      )}
    </div>
  )
}
