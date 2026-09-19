/**
 * HISOB-FAKTURALAR — har bir hisoblangan oy
 * (docs/modules/finance-parity.md §2.10, F10.01–F10.03).
 *
 * Bugungacha "falon oyda kimga nima hisoblangan" degan savolga javob beradigan
 * ekran YO'Q edi: `InvoiceService.ListAsync` yozilgan, lekin uni chaqiradigan
 * endpoint ham, sahifa ham yo'q edi.
 *
 * RUXSAT (SPEC §4.3): admin va direktor — `BillingGuard` bilan, qo'shni
 * ma'lumotnoma sahifalari kabi. Haqiqiy darvoza serverda.
 *
 * BEKOR QILISH — O'CHIRISH EMAS. Xato hisoblangan oy `void` bo'ladi va
 * qarzga kirmay qoladi; jurnal partiyasi ko'zgu satrlar bilan qaytariladi
 * (SPEC §4.1). To'lov tushgan oyni bekor qilib bo'lmaydi — avval to'lovni
 * tranzaksiyalar jurnalida storno qilish kerak.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  Ban,
  CalendarClock,
  ChevronLeft,
  ChevronRight,
  Coins,
  Download,
  FileText,
  Play,
  RotateCcw,
  Wallet,
} from 'lucide-react'
import type { FeeCategory, Invoice, InvoiceStatus, SchoolClass } from '@/types'
import type { InvoicePage } from '@/api/services/invoices'
import {
  canVoid,
  downloadInvoices,
  invoiceStatusLabels,
  invoiceStatusTone,
  listInvoices,
  runAccrual,
  voidInvoice,
} from '@/api/services/invoices'
import { billingErrorCode, billingErrorMessage } from '@/api/services/billingError'
import { getFeeCategories } from '@/api/services/billingCatalog'
import { getClasses } from '@/api/services/classes'
import { formatMonth } from '@/config/constants'
import { cn, formatDate, formatMoney } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { StatCard } from '@/components/ui/StatCard'
import { BillingGuard, Notice, StatusPill } from './BillingUi'
import { useBillingAccess } from './access'
import { VoidInvoiceModal } from './VoidInvoiceModal'
import { MonthPicker } from '@/components/ui/DatePicker'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

const PAGE_SIZE = 50

const EMPTY: InvoicePage = {
  rows: [],
  page: 1,
  pageSize: PAGE_SIZE,
  total: 0,
  totals: { amount: 0, discount: 0, payable: 0, paid: 0, remaining: 0 },
  classNames: {},
}

/** "YYYY-MM" — joriy oy. Registrning sukut davri (§2.10). */
function thisMonth(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

interface Filters {
  fromMonth: string
  toMonth: string
  status: string
  className: string
  categoryId: string
  onlyOverdue: boolean
  onlyDebtors: boolean
}

const INITIAL: Filters = {
  fromMonth: thisMonth(),
  toMonth: thisMonth(),
  status: 'all',
  className: 'all',
  categoryId: 'all',
  onlyOverdue: false,
  onlyDebtors: false,
}

export function InvoicesPage() {
  return (
    <BillingGuard>
      <InvoicesView />
    </BillingGuard>
  )
}

function InvoicesView() {
  const { canManageSubscriptions } = useBillingAccess()

  const [data, setData] = useState<InvoicePage>(EMPTY)
  const [filters, setFilters] = useState<Filters>(INITIAL)
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [categories, setCategories] = useState<FeeCategory[]>([])

  const [voiding, setVoiding] = useState<Invoice | null>(null)
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [actionCode, setActionCode] = useState<string | null>(null)
  const [accruing, setAccruing] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    getClasses().then(setClasses).catch(() => setClasses([]))
    getFeeCategories().then(setCategories).catch(() => setCategories([]))
  }, [])

  const query = useMemo(
    () => ({
      // Server oyning 1-kunini kutadi ("YYYY-MM-DD").
      fromMonth: filters.fromMonth ? `${filters.fromMonth}-01` : undefined,
      toMonth: filters.toMonth ? `${filters.toMonth}-01` : undefined,
      status: filters.status === 'all' ? undefined : (filters.status as InvoiceStatus),
      className: filters.className === 'all' ? undefined : filters.className,
      categoryId: filters.categoryId === 'all' ? undefined : filters.categoryId,
      onlyOverdue: filters.onlyOverdue || undefined,
      onlyDebtors: filters.onlyDebtors || undefined,
    }),
    [filters],
  )

  useEffect(() => {
    let cancelled = false
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda registrni qayta yuklash (maqsadli, loyihadagi mavjud naqsh)
    setLoading(true)
    listInvoices({ ...query, page, pageSize: PAGE_SIZE })
      .then((result) => {
        if (cancelled) return
        setData(result)
        setError(null)
      })
      .catch((e: unknown) => {
        if (cancelled) return
        setData(EMPTY)
        setError(billingErrorMessage(e, "Hisob-fakturalarni yuklab bo'lmadi"))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [query, page, reloadToken])

  const reload = useCallback(() => setReloadToken((t) => t + 1), [])

  const set = (patch: Partial<Filters>) => {
    setPage(1)
    setFilters((prev) => ({ ...prev, ...patch }))
  }

  const reset = () => {
    setPage(1)
    setFilters(INITIAL)
  }

  const handleVoid = async (reason: string) => {
    if (!voiding) return
    setBusy(true)
    setActionError(null)
    setActionCode(null)
    try {
      await voidInvoice(voiding.id, reason)
      setVoiding(null)
      setNotice('Hisob-faktura bekor qilindi — u qarzga kirmaydi, jurnaldagi yozuv qaytarildi.')
      reload()
    } catch (e: unknown) {
      setActionError(billingErrorMessage(e, "Bekor qilib bo'lmadi"))
      setActionCode(billingErrorCode(e) ?? null)
    } finally {
      setBusy(false)
    }
  }

  /** F10.03 — oyni qo'lda hisoblash. Idempotent: bor qator qayta yozilmaydi. */
  const handleAccrual = async () => {
    setAccruing(true)
    setError(null)
    setNotice(null)
    try {
      const results = await runAccrual(filters.toMonth)
      const created = results.reduce((sum, r) => sum + r.created, 0)
      const skipped = results.reduce((sum, r) => sum + r.skipped, 0)
      setNotice(
        created === 0
          ? `${formatMonth(filters.toMonth)}: yangi hisob-faktura yo'q (${skipped} ta allaqachon bor).`
          : `${formatMonth(filters.toMonth)}: ${created} ta hisob-faktura yozildi, ${skipped} tasi o'tkazib yuborildi.`,
      )
      reload()
    } catch (e: unknown) {
      setError(billingErrorMessage(e, "Oyni hisoblab bo'lmadi"))
    } finally {
      setAccruing(false)
    }
  }

  /** F10.05 — BUTUN filtr bo'yicha .xlsx, ko'rinib turgan sahifa emas. */
  const handleDownload = async () => {
    setExporting(true)
    setError(null)
    try {
      await downloadInvoices(query)
    } catch (e: unknown) {
      setError(billingErrorMessage(e, "Eksport qilib bo'lmadi"))
    } finally {
      setExporting(false)
    }
  }

  const first = data.total === 0 ? 0 : (data.page - 1) * data.pageSize + 1
  const last = Math.min(data.page * data.pageSize, data.total)
  const lastPage = Math.max(1, Math.ceil(data.total / data.pageSize))

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Hisob-fakturalar</h1>
          <p className="text-sm text-slate-400">
            Har o'quvchi × toifa × oy uchun bitta qator. Yakun butun filtr bo'yicha, ko'rinib
            turgan sahifa bo'yicha emas.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={handleDownload}
            disabled={exporting || loading || data.total === 0}
            className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-600 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
          >
            <Download className="h-4 w-4" />
            {exporting ? 'Tayyorlanmoqda...' : 'Excel'}
          </button>
          {canManageSubscriptions && (
            <button
              type="button"
              onClick={handleAccrual}
              disabled={accruing || loading}
              className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-600 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
            >
              <Play className="h-4 w-4" />
              {accruing ? 'Hisoblanmoqda...' : `Oyni hisoblash (${formatMonth(filters.toMonth)})`}
            </button>
          )}
        </div>
      </div>

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatCard label="Qatorlar" value={data.total} icon={FileText} />
        <StatCard
          label="To'lanadi"
          value={formatMoney(data.totals.payable)}
          icon={Coins}
          hint={`Chegirma ${formatMoney(data.totals.discount)}`}
        />
        <StatCard
          label="To'langan"
          value={formatMoney(data.totals.paid)}
          icon={Wallet}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
          hint="Storno qilingan to'lov kirmaydi"
        />
        <StatCard
          label="Qoldiq"
          value={formatMoney(data.totals.remaining)}
          icon={CalendarClock}
          iconBg={data.totals.remaining > 0 ? 'bg-red-50' : 'bg-emerald-50'}
          iconColor={data.totals.remaining > 0 ? 'text-red-600' : 'text-emerald-600'}
        />
      </div>

      {notice && <Notice tone="success">{notice}</Notice>}
      {error && <Notice>{error}</Notice>}

      <Card className="p-0">
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <MonthPicker
            value={filters.fromMonth}
            onChange={(value: string) => set({ fromMonth: value })}
            className="w-44"
          />
          <span className="text-sm text-slate-400">—</span>
          <MonthPicker
            value={filters.toMonth}
            onChange={(value: string) => set({ toMonth: value })}
            className="w-44"
          />
          <select
            value={filters.className}
            onChange={(e) => set({ className: e.target.value })}
            className={control}
          >
            <option value="all">Barcha sinflar</option>
            {classes.map((c) => (
              <option key={c.id} value={c.name}>
                {c.name}
              </option>
            ))}
          </select>
          <select
            value={filters.categoryId}
            onChange={(e) => set({ categoryId: e.target.value })}
            className={control}
          >
            <option value="all">Barcha toifalar</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
          <select
            value={filters.status}
            onChange={(e) => set({ status: e.target.value })}
            className={control}
          >
            <option value="all">Barcha holatlar</option>
            <option value="open">Ochiq</option>
            <option value="partial">Qisman to'langan</option>
            <option value="paid">To'langan</option>
            <option value="void">Bekor qilingan</option>
          </select>
          <label className="inline-flex items-center gap-2 text-xs font-medium text-slate-600">
            <input
              type="checkbox"
              checked={filters.onlyDebtors}
              onChange={(e) => set({ onlyDebtors: e.target.checked })}
              className="h-4 w-4 rounded border-slate-300"
            />
            Faqat qarzdorlar
          </label>
          <label className="inline-flex items-center gap-2 text-xs font-medium text-slate-600">
            <input
              type="checkbox"
              checked={filters.onlyOverdue}
              onChange={(e) => set({ onlyOverdue: e.target.checked })}
              className="h-4 w-4 rounded border-slate-300"
            />
            Faqat muddati o'tgan
          </label>
          <button
            type="button"
            onClick={reset}
            title="Filtrlarni tozalash"
            className="ml-auto inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-medium text-slate-500 transition-colors hover:bg-slate-100"
          >
            <RotateCcw className="h-3.5 w-3.5" />
            Tozalash
          </button>
        </div>

        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              {/* Bir qator — bir satr (mijoz, 2026-09-18). Uzun ism "..." bilan
                  kesiladi, to'lig'i hoverda; sig'masa jadval o'z qutisida
                  yonga suriladi. */}
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">O'quvchi</th>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3">Toifa</th>
                  <th className="px-4 py-3">Oy</th>
                  <th className="px-4 py-3 text-right">Summa</th>
                  <th className="px-4 py-3 text-right">Chegirma</th>
                  <th className="px-4 py-3 text-right">To'lanadi</th>
                  <th className="px-4 py-3 text-right">To'langan</th>
                  <th className="px-4 py-3 text-right">Qoldiq</th>
                  <th className="px-4 py-3">Muddat</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.rows.map((row) => (
                  <tr
                    key={row.id}
                    className={cn('hover:bg-slate-50/60', row.status === 'void' && 'text-slate-400')}
                  >
                    <td className="px-4 py-3 font-medium text-slate-800">
                      <span className="block max-w-[14rem] truncate" title={row.studentName}>
                        {row.studentName}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      {data.classNames[row.studentId] ? (
                        <span className="whitespace-nowrap rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                          {data.classNames[row.studentId]}
                        </span>
                      ) : (
                        <span className="text-slate-400">—</span>
                      )}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-600">{row.categoryName}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-600">
                      {formatMonth(row.periodMonth.slice(0, 7))}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-right text-slate-600">
                      {formatMoney(row.amount)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-right text-slate-500">
                      {row.discount > 0 ? formatMoney(row.discount) : '—'}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-right font-medium text-slate-800">
                      {formatMoney(row.payable)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-right text-emerald-600">
                      {formatMoney(row.paid)}
                    </td>
                    <td
                      className={cn(
                        'whitespace-nowrap px-4 py-3 text-right font-semibold',
                        row.remaining > 0 ? 'text-red-600' : 'text-slate-400',
                      )}
                    >
                      {formatMoney(row.remaining)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-500">
                      {formatDate(row.dueOn)}
                      {row.isOverdue && (
                        <span className="ml-2 rounded-md bg-red-50 px-1.5 py-0.5 text-[10px] font-medium text-red-600">
                          muddati o'tgan
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <StatusPill tone={invoiceStatusTone(row.status)}>
                        {invoiceStatusLabels[row.status]}
                      </StatusPill>
                    </td>
                    <td className="px-4 py-3 text-right">
                      {canManageSubscriptions && row.status !== 'void' && (
                        // To'lovi bor qatorda tugma O'CHIRILADI, yashirilmaydi:
                        // sababi tooltipda turadi ("avval to'lovni storno
                        // qiling"), ya'ni foydalanuvchi nima qilish kerakligini
                        // bosmasdan biladi. Jurnaldagi storno tugmasi esa
                        // butunlay yashiriladi — u yerda "nega yo'q" degan
                        // savol tug'ilmaydi, storno qatorini storno qilish
                        // degan tushuncha umuman yo'q.
                        <button
                          type="button"
                          disabled={!canVoid(row)}
                          title={
                            canVoid(row)
                              ? 'Bekor qilish'
                              : "To'lov taqsimlangan — avval to'lovni storno qiling"
                          }
                          aria-label="Bekor qilish"
                          onClick={() => {
                            setActionError(null)
                            setActionCode(null)
                            setNotice(null)
                            setVoiding(row)
                          }}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600 disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent disabled:hover:text-slate-400"
                        >
                          <Ban className="h-4 w-4" />
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
                {data.rows.length === 0 && (
                  <tr>
                    <td colSpan={12} className="px-4 py-12 text-center text-slate-400">
                      Tanlangan filtr bo'yicha hisob-faktura yo'q
                    </td>
                  </tr>
                )}
              </tbody>
              {data.rows.length > 0 && (
                <tfoot>
                  <tr className="border-t border-slate-200 bg-slate-50/70 font-medium text-slate-700">
                    <td className="px-4 py-2.5" colSpan={4}>
                      Jami (filtr bo'yicha)
                    </td>
                    <td className="whitespace-nowrap px-4 py-2.5 text-right">
                      {formatMoney(data.totals.amount)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-2.5 text-right">
                      {formatMoney(data.totals.discount)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-2.5 text-right">
                      {formatMoney(data.totals.payable)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-2.5 text-right text-emerald-600">
                      {formatMoney(data.totals.paid)}
                    </td>
                    <td
                      className={cn(
                        'whitespace-nowrap px-4 py-2.5 text-right',
                        data.totals.remaining > 0 ? 'text-red-600' : 'text-slate-400',
                      )}
                    >
                      {formatMoney(data.totals.remaining)}
                    </td>
                    <td colSpan={3} />
                  </tr>
                </tfoot>
              )}
            </table>
          </div>
        )}

        <div className="flex items-center justify-between border-t border-slate-100 px-4 py-3">
          <p className="text-xs text-slate-400">
            {data.total === 0 ? "Yozuv yo'q" : `${first}–${last} / ${data.total} ta`}
          </p>
          <div className="flex items-center gap-1">
            <button
              type="button"
              disabled={data.page <= 1 || loading}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-40"
            >
              <ChevronLeft className="h-4 w-4" />
            </button>
            <span className="min-w-[70px] text-center text-xs text-slate-500">
              {data.page} / {lastPage}
            </span>
            <button
              type="button"
              disabled={data.page >= lastPage || loading}
              onClick={() => setPage((p) => p + 1)}
              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-40"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>
        </div>
      </Card>

      <VoidInvoiceModal
        invoice={voiding}
        busy={busy}
        error={actionError}
        errorCode={actionCode}
        onClose={() => setVoiding(null)}
        onConfirm={handleVoid}
      />
    </div>
  )
}
