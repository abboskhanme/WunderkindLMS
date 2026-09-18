/**
 * TRANZAKSIYALAR — bitta ro'yxatda hamma pul harakati
 * (docs/modules/finance-parity.md §2.9, F9.01–F9.05).
 *
 * Bugungacha pul uchta alohida joyda ko'rinardi: to'lovlar (ekranisiz API),
 * chiqimlar ro'yxati va bitta kunning harakatlari (Kassa kuni). "Shu oyda
 * kassaga nima kirdi va nima chiqdi" degan savolga javob berish uchun ularni
 * qo'lda qo'shish kerak edi.
 *
 * RUXSAT (SPEC §4.3): admin va direktor. Kassir bu ro'yxatni ko'rmaydi —
 * u kassirlar KESIMIDAGI ko'rinish, ustiga chiqimlarni ham qo'shadi. Sahifa
 * darvozasi shu yerda; haqiqiy darvoza serverda
 * (`[FinanceRole(ViewBillingReports)]`).
 *
 * PUL ARIFMETIKASI BU YERDA YO'Q. Qator summasi ham, pastdagi yakun ham
 * serverdan keladi; yakun BUTUN FILTR bo'yicha, ko'rinib turgan sahifa
 * bo'yicha emas.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  ArrowDownCircle,
  ArrowUpCircle,
  ChevronLeft,
  ChevronRight,
  Clock3,
  Download,
  Lock,
  Minus,
  Plus,
  RotateCcw,
  Scale,
  Search,
  Undo2,
} from 'lucide-react'
import type { FeeCategory, PaymentMethod, SchoolClass } from '@/types'
import type {
  TransactionDirection,
  TransactionKind,
  TransactionPage,
  TransactionRow,
  TransactionStatus,
} from '@/api/services/transactions'
import {
  canReverse,
  downloadTransactions,
  getTransactions,
  transactionKindLabels,
  transactionStatusLabels,
} from '@/api/services/transactions'
import { reversePayment } from '@/api/services/payments'
import { billingErrorCode, billingErrorMessage } from '@/api/services/billingError'
import { getFeeCategories } from '@/api/services/billingCatalog'
import { getClasses } from '@/api/services/classes'
import { useAuth } from '@/context/auth-context'
import { cn, formatMoney } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { StatCard } from '@/components/ui/StatCard'
import { Notice, StatusPill } from '@/pages/admin/billing/BillingUi'
import { formatDateTime, formatSignedMoney, paymentMethodLabel, signClass } from './reportLabels'
import { ReversePaymentModal } from './ReversePaymentModal'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

const PAGE_SIZE = 50

/** SPEC §4.3 — hisobot admin va direktorniki. */
const ALLOWED_ROLES = ['admin', 'superadmin']

const EMPTY: TransactionPage = {
  rows: [],
  page: 1,
  pageSize: PAGE_SIZE,
  total: 0,
  totals: { totalIn: 0, totalOut: 0, net: 0, pendingOut: 0 },
}

/** "YYYY-MM-DD" — MAHALLIY sana bo'yicha (`toISOString` UTC beradi va kechqurun bir kun orqaga surardi). */
function isoDay(offsetDays = 0): string {
  const d = new Date()
  d.setDate(d.getDate() + offsetDays)
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const dd = String(d.getDate()).padStart(2, '0')
  return `${d.getFullYear()}-${mm}-${dd}`
}

/** Joriy oyning 1-kuni — EduSchool'dagi sukut davr (§2.9). */
function firstOfMonth(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`
}

interface Filters {
  from: string
  to: string
  direction: string
  kind: string
  method: string
  status: string
  className: string
  category: string
  receiptNo: string
  firstPaymentOnly: boolean
}

const INITIAL: Filters = {
  from: firstOfMonth(),
  to: isoDay(),
  direction: 'all',
  kind: 'all',
  method: 'all',
  status: 'all',
  className: 'all',
  category: 'all',
  receiptNo: '',
  firstPaymentOnly: false,
}

const statusTones: Record<TransactionStatus, 'success' | 'danger' | 'warning'> = {
  active: 'success',
  reversed: 'danger',
  pending: 'warning',
}

export function TransactionsPage() {
  const { user } = useAuth()
  const allowed = user !== null && ALLOWED_ROLES.includes(user.role)

  const [data, setData] = useState<TransactionPage>(EMPTY)
  const [filters, setFilters] = useState<Filters>(INITIAL)
  const [receiptInput, setReceiptInput] = useState('')
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [categories, setCategories] = useState<FeeCategory[]>([])

  const [reversing, setReversing] = useState<TransactionRow | null>(null)
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [actionCode, setActionCode] = useState<string | null>(null)
  const [done, setDone] = useState<string | null>(null)
  const [exporting, setExporting] = useState(false)

  // Ma'lumotnomalar bir marta: sinflar va toifalar filtri uchun.
  useEffect(() => {
    if (!allowed) return
    getClasses().then(setClasses).catch(() => setClasses([]))
    getFeeCategories().then(setCategories).catch(() => setCategories([]))
  }, [allowed])

  // Chek raqami serverda qidiriladi — har bosilgan raqamga so'rov ketmasin.
  useEffect(() => {
    if (receiptInput === filters.receiptNo) return
    const timer = setTimeout(() => {
      setPage(1)
      setFilters((prev) => ({ ...prev, receiptNo: receiptInput }))
    }, 350)
    return () => clearTimeout(timer)
  }, [receiptInput, filters.receiptNo])

  const query = useMemo(
    () => ({
      from: filters.from || undefined,
      to: filters.to || undefined,
      direction:
        filters.direction === 'all' ? undefined : (filters.direction as TransactionDirection),
      kind: filters.kind === 'all' ? undefined : (filters.kind as TransactionKind),
      method: filters.method === 'all' ? undefined : (filters.method as PaymentMethod),
      status: filters.status === 'all' ? undefined : (filters.status as TransactionStatus),
      className: filters.className === 'all' ? undefined : filters.className,
      category: filters.category === 'all' ? undefined : filters.category,
      receiptNo: filters.receiptNo.trim() ? Number(filters.receiptNo.trim()) : undefined,
      firstPaymentOnly: filters.firstPaymentOnly || undefined,
    }),
    [filters],
  )

  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    if (!allowed) return
    let cancelled = false
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda ro'yxatni qayta yuklash (maqsadli, loyihadagi mavjud naqsh)
    setLoading(true)
    getTransactions({ ...query, page, pageSize: PAGE_SIZE })
      .then((result) => {
        if (cancelled) return
        setData(result)
        setError(null)
      })
      .catch((e: unknown) => {
        if (cancelled) return
        setData(EMPTY)
        setError(billingErrorMessage(e, "Tranzaksiyalarni yuklab bo'lmadi"))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [allowed, query, page, reloadToken])

  const reload = useCallback(() => setReloadToken((t) => t + 1), [])

  // Filtr o'zgarsa — birinchi sahifaga qaytamiz, aks holda 3-sahifada "bo'sh" ko'rinardi.
  const set = (patch: Partial<Filters>) => {
    setPage(1)
    setFilters((prev) => ({ ...prev, ...patch }))
  }

  const setRange = (days: number) => set({ from: isoDay(-days), to: isoDay() })

  const reset = () => {
    setPage(1)
    setReceiptInput('')
    setFilters(INITIAL)
  }

  const handleReverse = async (reason: string) => {
    if (!reversing) return
    setBusy(true)
    setActionError(null)
    setActionCode(null)
    try {
      const storno = await reversePayment(reversing.id, reason)
      setReversing(null)
      setDone(`Storno qilindi — yangi chek №${storno.receiptNo}. Ikkala qator ham ro'yxatda qoldi.`)
      reload()
    } catch (e: unknown) {
      setActionError(billingErrorMessage(e, "Storno qilib bo'lmadi"))
      setActionCode(billingErrorCode(e) ?? null)
    } finally {
      setBusy(false)
    }
  }

  const handleExport = async () => {
    setExporting(true)
    setActionError(null)
    try {
      await downloadTransactions(query)
    } catch (e: unknown) {
      setError(billingErrorMessage(e, "Eksport qilib bo'lmadi"))
    } finally {
      setExporting(false)
    }
  }

  if (!allowed) {
    return (
      <Card className="mx-auto max-w-lg text-center">
        <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-xl bg-slate-100 text-slate-400">
          <Lock className="h-6 w-6" />
        </div>
        <h2 className="text-base font-semibold text-slate-800">Bu bo'lim sizga yopiq</h2>
        <p className="mt-2 text-sm text-slate-500">
          Tranzaksiyalar jurnali — kassirlar kesimidagi hisobot, u faqat administrator va
          direktorga ochiq (SPEC §4.3).
        </p>
      </Card>
    )
  }

  const first = data.total === 0 ? 0 : (data.page - 1) * data.pageSize + 1
  const last = Math.min(data.page * data.pageSize, data.total)
  const lastPage = Math.max(1, Math.ceil(data.total / data.pageSize))

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Tranzaksiyalar</h1>
          <p className="text-sm text-slate-400">
            To'lovlar, stornolar va chiqimlar — bitta ro'yxatda. Pastdagi yakun butun filtr
            bo'yicha, ko'rinib turgan sahifa bo'yicha emas.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Link
            to="/cashier"
            className="inline-flex items-center gap-1.5 rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-sm font-medium text-emerald-700 transition-colors hover:bg-emerald-100"
          >
            <Plus className="h-4 w-4" />
            Kirim
          </Link>
          <Link
            to="/admin/billing/expenses"
            className="inline-flex items-center gap-1.5 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm font-medium text-red-700 transition-colors hover:bg-red-100"
          >
            <Minus className="h-4 w-4" />
            Chiqim
          </Link>
          <button
            type="button"
            onClick={handleExport}
            disabled={exporting || loading}
            className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-600 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
          >
            <Download className="h-4 w-4" />
            {exporting ? 'Tayyorlanmoqda...' : 'Excel'}
          </button>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatCard
          label="Kirim"
          value={formatMoney(data.totals.totalIn)}
          icon={ArrowUpCircle}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
          hint="Storno qilinmagan to'lovlar"
        />
        <StatCard
          label="Chiqim"
          value={formatMoney(data.totals.totalOut)}
          icon={ArrowDownCircle}
          iconBg="bg-red-50"
          iconColor="text-red-600"
          hint="Stornolar va chiqimlar"
        />
        <StatCard
          label="Sof"
          value={formatSignedMoney(data.totals.net)}
          icon={Scale}
          iconBg={data.totals.net < 0 ? 'bg-red-50' : 'bg-emerald-50'}
          iconColor={data.totals.net < 0 ? 'text-red-600' : 'text-emerald-600'}
          hint="Filtr bo'yicha"
        />
        <StatCard
          label="Tasdiq kutmoqda"
          value={formatMoney(data.totals.pendingOut)}
          icon={Clock3}
          iconBg="bg-amber-50"
          iconColor="text-amber-600"
          hint="Pul hali chiqmagan — yakunga kirmaydi"
        />
      </div>

      {done && <Notice tone="success">{done}</Notice>}
      {error && <Notice>{error}</Notice>}

      <Card className="p-0">
        <div className="space-y-3 border-b border-slate-100 p-4">
          <div className="flex flex-wrap items-center gap-2">
            <input
              type="date"
              value={filters.from}
              onChange={(e) => set({ from: e.target.value })}
              className={control}
            />
            <span className="text-sm text-slate-400">—</span>
            <input
              type="date"
              value={filters.to}
              onChange={(e) => set({ to: e.target.value })}
              className={control}
            />
            <div className="flex items-center gap-1">
              {[
                { label: 'Bugun', days: 0 },
                { label: '7 kun', days: 7 },
                { label: '30 kun', days: 30 },
              ].map((r) => (
                <button
                  key={r.label}
                  type="button"
                  onClick={() => setRange(r.days)}
                  className="rounded-lg border border-slate-200 px-2.5 py-1.5 text-xs font-medium text-slate-600 transition-colors hover:bg-slate-50"
                >
                  {r.label}
                </button>
              ))}
            </div>
            <label className="ml-2 inline-flex items-center gap-2 text-xs font-medium text-slate-600">
              <input
                type="checkbox"
                checked={filters.firstPaymentOnly}
                onChange={(e) => set({ firstPaymentOnly: e.target.checked })}
                className="h-4 w-4 rounded border-slate-300"
              />
              Faqat birinchi to'lov
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

          <div className="flex flex-wrap items-center gap-3">
            <div className="relative min-w-[180px]">
              <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
              <input
                value={receiptInput}
                onChange={(e) => setReceiptInput(e.target.value.replace(/\D/g, ''))}
                placeholder="Chek raqami..."
                inputMode="numeric"
                className={cn(control, 'w-full pl-9')}
              />
            </div>
            <select
              value={filters.direction}
              onChange={(e) => set({ direction: e.target.value })}
              className={control}
            >
              <option value="all">Kirim va chiqim</option>
              <option value="in">Faqat kirim</option>
              <option value="out">Faqat chiqim</option>
            </select>
            <select
              value={filters.kind}
              onChange={(e) => set({ kind: e.target.value })}
              className={control}
            >
              <option value="all">Barcha turlar</option>
              <option value="payment">To'lov</option>
              <option value="reversal">Storno</option>
              <option value="expense">Chiqim</option>
            </select>
            <select
              value={filters.method}
              onChange={(e) => set({ method: e.target.value })}
              className={control}
            >
              <option value="all">Barcha usullar</option>
              <option value="cash">Naqd</option>
              <option value="card">Karta</option>
              <option value="transfer">O'tkazma</option>
              <option value="online">Onlayn</option>
            </select>
            <select
              value={filters.status}
              onChange={(e) => set({ status: e.target.value })}
              className={control}
            >
              <option value="all">Barcha holatlar</option>
              <option value="active">Faol</option>
              <option value="reversed">Storno qilingan</option>
              <option value="pending">Tasdiq kutmoqda</option>
            </select>
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
              value={filters.category}
              onChange={(e) => set({ category: e.target.value })}
              className={control}
            >
              <option value="all">Barcha toifalar</option>
              {categories.map((c) => (
                <option key={c.id} value={c.code}>
                  {c.name}
                </option>
              ))}
            </select>
          </div>
        </div>

        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Sana</th>
                  <th className="px-4 py-3">Turi</th>
                  <th className="px-4 py-3">Kim</th>
                  <th className="px-4 py-3">Chek №</th>
                  <th className="px-4 py-3 text-right">Summa</th>
                  <th className="px-4 py-3">Toifa</th>
                  <th className="px-4 py-3">Usul</th>
                  <th className="px-4 py-3">Kassir / yozgan</th>
                  <th className="px-4 py-3">Izoh</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.rows.map((row) => (
                  <tr
                    key={`${row.kind}-${row.id}`}
                    className={cn(
                      'hover:bg-slate-50/60',
                      row.kind === 'reversal' && 'bg-amber-50/40',
                      row.status === 'reversed' && 'text-slate-400',
                    )}
                  >
                    <td className="whitespace-nowrap px-4 py-3 text-slate-500">
                      {formatDateTime(row.occurredAt)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-700">
                      {transactionKindLabels[row.kind]}
                    </td>
                    <td className="px-4 py-3 font-medium text-slate-800">
                      {row.personName ?? '—'}
                      {row.className && (
                        <span className="ml-2 rounded-md bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-slate-500">
                          {row.className}
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-slate-500">{row.receiptNo ?? '—'}</td>
                    <td
                      className={cn(
                        'whitespace-nowrap px-4 py-3 text-right font-semibold',
                        row.status === 'reversed' ? 'text-slate-400 line-through' : signClass(row.amount),
                      )}
                    >
                      {formatSignedMoney(row.amount)}
                    </td>
                    <td className="px-4 py-3 text-slate-600">
                      {row.categoryLabel ?? row.category ?? '—'}
                    </td>
                    <td className="px-4 py-3 text-slate-600">
                      {row.method ? paymentMethodLabel(row.method) : '—'}
                    </td>
                    <td className="px-4 py-3 text-slate-500">{row.actorName ?? '—'}</td>
                    <td className="max-w-[220px] truncate px-4 py-3 text-slate-500">
                      {row.note || '—'}
                    </td>
                    <td className="px-4 py-3">
                      <StatusPill tone={statusTones[row.status]}>
                        {transactionStatusLabels[row.status]}
                      </StatusPill>
                    </td>
                    <td className="px-4 py-3 text-right">
                      {canReverse(row) && (
                        <button
                          type="button"
                          title="Storno qilish"
                          aria-label="Storno qilish"
                          onClick={() => {
                            setActionError(null)
                            setActionCode(null)
                            setDone(null)
                            setReversing(row)
                          }}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                        >
                          <Undo2 className="h-4 w-4" />
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
                {data.rows.length === 0 && (
                  <tr>
                    <td colSpan={11} className="px-4 py-12 text-center text-slate-400">
                      Tanlangan filtr bo'yicha pul harakati yo'q
                    </td>
                  </tr>
                )}
              </tbody>
              {data.rows.length > 0 && (
                <tfoot>
                  <tr className="border-t border-slate-200 bg-slate-50/70 text-slate-700">
                    <td className="px-4 py-2.5 font-medium" colSpan={4}>
                      Jami (filtr bo'yicha)
                    </td>
                    <td
                      className={cn(
                        'whitespace-nowrap px-4 py-2.5 text-right font-semibold',
                        signClass(data.totals.net),
                      )}
                    >
                      {formatSignedMoney(data.totals.net)}
                    </td>
                    <td className="px-4 py-2.5 text-xs text-slate-500" colSpan={6}>
                      Kirim {formatMoney(data.totals.totalIn)} · Chiqim{' '}
                      {formatMoney(data.totals.totalOut)}
                    </td>
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

      <ReversePaymentModal
        row={reversing}
        busy={busy}
        error={actionError}
        errorCode={actionCode}
        onClose={() => setReversing(null)}
        onConfirm={handleReverse}
      />
    </div>
  )
}
