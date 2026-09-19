/**
 * Ota-ona / o'quvchi moliya ekrani — P1-19 (SPEC §3.7, §4.7).
 *
 * TO'RTTA QOIDA
 * -------------
 * 1. Hisob-fakturalar OYLAR bo'yicha, har oy ichida TOIFA kesimida:
 *    "Sentabr 2026 → O'qish to'lovi 1 800 000, Avtobus 450 000". Bitta umumiy
 *    raqam yetarli emas — ota-ona nima uchun to'layotganini bilishi kerak.
 * 2. Har to'lov qatorida chek PDF'i bor (SPEC §4.7 — maktab o'zgartira
 *    olmaydigan mustaqil nusxa).
 * 3. Storno qilingan to'lov YASHIRILMAYDI: ustidan chizilgan holda qoladi va
 *    bekor qilish sababi yonida yoziladi.
 * 4. Ekranda birorta ichki id yo'q (uuid, hisob-faktura id, smena id). Chek
 *    manzilidagi id — havolada, matnda emas.
 *
 * PULNI SERVER HISOBLAYDI. Oy jamlari `PortalFinanceController` da `decimal`
 * da qo'shiladi; bu faylda birorta `+` yoki `reduce` yo'q.
 *
 * IKKI JOYDA ISHLAYDI: portal marshruti (`/parent`, `/student`) va admin
 * o'quvchi kartochkasi (`embedded`, `studentId` bilan). Farqi faqat sarlavhada
 * — ma'lumot ham, ruxsat ham serverda hal qilinadi.
 */
import { useState } from 'react'
import {
  AlertTriangle, CalendarClock, FileText, Loader2, RefreshCw,
  ReceiptText, TrendingDown, Wallet, ChevronDown, ChevronRight,
} from 'lucide-react'
import {
  getPortalFinance, openReceiptPdf,
  type PortalCategoryLine, type PortalFinance, type PortalInvoiceStatus,
  type PortalMonth, type PortalPayment, type PortalPaymentMethod,
} from '@/api/services/portalFinance'
import { useAsync } from '@/hooks/useAsync'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { StatCard } from '@/components/ui/StatCard'
import { cn, formatDate, formatMoney } from '@/lib/utils'

const uzMonths = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

/** "2026-09" → "Sentabr 2026" */
function monthLabel(period: string): string {
  const [year, month] = period.split('-')
  return `${uzMonths[Number(month) - 1] ?? month} ${year}`
}

const statusLabels: Record<PortalInvoiceStatus, string> = {
  open: "To'lanmagan",
  partial: 'Qisman',
  paid: "To'langan",
  void: 'Bekor qilingan',
}

const statusStyles: Record<PortalInvoiceStatus, string> = {
  open: 'bg-red-50 text-red-700',
  partial: 'bg-amber-50 text-amber-700',
  paid: 'bg-emerald-50 text-emerald-700',
  void: 'bg-slate-100 text-slate-500',
}

/** To'lov usuli — FAQAT YORLIQ, provayder integratsiyasi yo'q (SPEC §8.1 Q13). */
const methodLabels: Record<PortalPaymentMethod, string> = {
  cash: 'Naqd',
  card: 'Karta',
  transfer: "Bank o'tkazmasi",
  online: 'Onlayn',
}

/** Noma'lum usul kelsa ham qator ko'rinadi — kodning o'zi yozib qo'yiladi. */
function methodLabel(method: string): string {
  return methodLabels[method as PortalPaymentMethod] ?? method
}

/**
 * Ko'rsatiladigan holat — `status` va `remaining` bir-biriga ZID bo'lsa,
 * PULGA ishonamiz.
 *
 * Odatda ular mos keladi: `PaymentService` storno'dan keyin hisob-faktura
 * statusini qayta hisoblaydi. Lekin bu ikkita alohida ustun, va agar ular bir
 * lahzaga ajralib qolsa, ota-ona 1 800 000 qizil qoldiq YONIDA "To'langan"
 * degan yozuvni ko'rardi — bu eng yomon variant: u to'lamay qo'yadi. Bu yerda
 * arifmetika yo'q, faqat server bergan sonni nol bilan solishtirish.
 */
function shownStatus(line: PortalCategoryLine): PortalInvoiceStatus {
  if (line.status === 'void') return 'void'
  if (line.remaining <= 0) return 'paid'
  return line.paid > 0 ? 'partial' : 'open'
}

interface Props {
  /** Admin/xodim ko'rinishi uchun. Portalda berilmaydi — egasini server aniqlaydi. */
  studentId?: string
  /** Boshqa sahifa ichida ishlatilmoqdami (o'quvchi kartochkasi) — sarlavha chiqmaydi. */
  embedded?: boolean
}

export function FinanceView({ studentId, embedded = false }: Props) {
  const { data, loading, error, refetch } = useAsync<PortalFinance>(
    () => getPortalFinance(studentId),
    [studentId],
  )

  /** Chek yuklanayotgan to'lov (tugmani bloklash uchun). */
  const [busyReceipt, setBusyReceipt] = useState<string | null>(null)
  const [receiptError, setReceiptError] = useState<string | null>(null)

  function handleReceipt(paymentId: string) {
    setBusyReceipt(paymentId)
    setReceiptError(null)
    // `openReceiptPdf` oynani `await` dan oldin ochadi — shuning uchun uni
    // bosish hodisasidan to'g'ridan-to'g'ri chaqiramiz.
    openReceiptPdf(paymentId, studentId)
      .catch(() => setReceiptError("Chekni ochib bo'lmadi. Birozdan so'ng qayta urinib ko'ring."))
      .finally(() => setBusyReceipt(null))
  }

  if (loading) return <Loader label="Moliya ma'lumoti yuklanmoqda..." />

  if (error) {
    return (
      <Card className="flex flex-col items-center gap-3 py-12 text-center">
        <AlertTriangle className="h-8 w-8 text-amber-500" />
        <p className="font-medium text-slate-700">Ma'lumotni olib bo'lmadi</p>
        <p className="max-w-md text-sm text-slate-400">
          Internet aloqasini tekshirib, qayta urinib ko'ring. Muammo takrorlansa maktab
          hisobchisiga murojaat qiling.
        </p>
        <Button variant="secondary" onClick={refetch}>
          <RefreshCw className="h-4 w-4" /> Qayta urinish
        </Button>
      </Card>
    )
  }

  if (!data) return null

  const isEmpty = data.months.length === 0 && data.payments.length === 0

  return (
    <div className="space-y-6">
      {!embedded && (
        <div>
          <h1 className="text-xl font-semibold text-slate-800">To'lovlar</h1>
          <p className="text-sm text-slate-400">
            {data.studentName}
            {data.className ? ` · ${data.className}` : ''}
          </p>
        </div>
      )}

      <Summary debt={data.debt} credit={data.credit} />

      {receiptError && (
        <Card className="flex items-center gap-2 border-amber-200 bg-amber-50 py-3 text-sm text-amber-800">
          <AlertTriangle className="h-4 w-4 shrink-0" /> {receiptError}
        </Card>
      )}

      {isEmpty ? (
        <Card className="flex flex-col items-center gap-2 py-14 text-center">
          <ReceiptText className="h-8 w-8 text-slate-300" />
          <p className="font-medium text-slate-600">Hozircha hisob-faktura yo'q</p>
          <p className="max-w-md text-sm text-slate-400">
            Oylik to'lov hisoblanganda shu yerda oy va toifalar bo'yicha ko'rinadi.
          </p>
        </Card>
      ) : (
        <>
          <DebtSection data={data} />
          <MonthsSection months={data.months} />
          <PaymentsSection
            payments={data.payments}
            busyReceipt={busyReceipt}
            onReceipt={handleReceipt}
          />
        </>
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Yuqori qatordagi jamlar                                            */
/* ------------------------------------------------------------------ */

function Summary({ debt, credit }: { debt: number; credit: number }) {
  const hasDebt = debt > 0
  return (
    <div className="grid gap-4 sm:grid-cols-2">
      <StatCard
        label="Jami qarz"
        value={hasDebt ? formatMoney(debt) : formatMoney(0)}
        icon={hasDebt ? TrendingDown : Wallet}
        iconBg={hasDebt ? 'bg-red-50' : 'bg-emerald-50'}
        iconColor={hasDebt ? 'text-red-600' : 'text-emerald-600'}
        hint={hasDebt ? "To'lanmagan qoldiq" : "Qarz yo'q"}
      />
      {credit > 0 && (
        <StatCard
          label="Avans (ortiqcha to'lov)"
          value={formatMoney(credit)}
          icon={Wallet}
          iconBg="bg-brand-50"
          iconColor="text-brand-600"
          hint="Keyingi oylarga o'tkaziladi"
        />
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  Qarz: qancha, qaysi oy uchun, qaysi toifada                        */
/* ------------------------------------------------------------------ */

function DebtSection({ data }: { data: PortalFinance }) {
  if (data.debtLines.length === 0) {
    return (
      <Card className="flex items-center gap-3 border-emerald-200 bg-emerald-50/60">
        <Wallet className="h-5 w-5 shrink-0 text-emerald-600" />
        <p className="text-sm font-medium text-emerald-800">
          Qarz yo'q — barcha hisob-fakturalar to'langan.
        </p>
      </Card>
    )
  }

  return (
    <Card>
      <div className="mb-4 flex items-center gap-2">
        <TrendingDown className="h-5 w-5 text-red-600" />
        <h2 className="font-semibold text-slate-800">To'lanishi kerak</h2>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
            <tr>
              <th className="px-3 py-2">Oy</th>
              <th className="px-3 py-2">Toifa</th>
              <th className="px-3 py-2">Muddat</th>
              <th className="px-3 py-2 text-right">Qoldiq</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {data.debtLines.map((line) => (
              <tr key={`${line.periodMonth}-${line.categoryCode}`} className="hover:bg-slate-50/60">
                <td className="whitespace-nowrap px-3 py-2.5 font-medium text-slate-700">
                  {monthLabel(line.periodMonth)}
                </td>
                <td className="px-3 py-2.5 text-slate-600">{line.categoryName}</td>
                <td className="whitespace-nowrap px-3 py-2.5">
                  <span className="inline-flex items-center gap-1.5 text-slate-500">
                    <CalendarClock className="h-3.5 w-3.5 text-slate-400" />
                    {formatDate(line.dueOn)}
                  </span>
                  {line.isOverdue && (
                    <span className="ml-2 rounded-md bg-red-50 px-1.5 py-0.5 text-xs font-medium text-red-700">
                      Muddati o'tgan
                    </span>
                  )}
                </td>
                <td className="whitespace-nowrap px-3 py-2.5 text-right font-semibold text-red-600">
                  {formatMoney(line.remaining)}
                </td>
              </tr>
            ))}
          </tbody>
          <tfoot className="border-t border-slate-200 bg-slate-50 text-sm font-semibold text-slate-700">
            <tr>
              <td className="px-3 py-2.5" colSpan={3}>
                Jami qarz
              </td>
              <td className="whitespace-nowrap px-3 py-2.5 text-right text-red-700">
                {formatMoney(data.debt)}
              </td>
            </tr>
          </tfoot>
        </table>
      </div>
    </Card>
  )
}

/* ------------------------------------------------------------------ */
/*  Oylar × toifalar                                                   */
/* ------------------------------------------------------------------ */

function MonthsSection({ months }: { months: PortalMonth[] }) {
  // Ochiq/yopiq holat: qoldig'i bor oy ochiq turadi, to'liq to'langani yig'iladi.
  const [toggled, setToggled] = useState<Record<string, boolean>>({})

  if (months.length === 0) {
    return (
      <Card>
        <div className="mb-4 flex items-center gap-2">
          <FileText className="h-5 w-5 text-brand-600" />
          <h2 className="font-semibold text-slate-800">Oylar bo'yicha hisob-fakturalar</h2>
        </div>
        <p className="py-8 text-center text-sm text-slate-400">Hisob-faktura yo'q</p>
      </Card>
    )
  }

  return (
    <Card>
      <div className="mb-4 flex items-center gap-2">
        <FileText className="h-5 w-5 text-brand-600" />
        <h2 className="font-semibold text-slate-800">Oylar bo'yicha hisob-fakturalar</h2>
      </div>

      <div className="space-y-3">
        {months.map((month) => {
          const open = toggled[month.periodMonth] ?? month.remaining > 0
          return (
            <div
              key={month.periodMonth}
              className="overflow-hidden rounded-xl border border-slate-200"
            >
              <button
                type="button"
                onClick={() =>
                  setToggled((prev) => ({ ...prev, [month.periodMonth]: !open }))
                }
                className="flex w-full flex-wrap items-center justify-between gap-3 bg-slate-50 px-4 py-3 text-left hover:bg-slate-100"
              >
                <span className="flex items-center gap-2 font-medium text-slate-800">
                  {open ? (
                    <ChevronDown className="h-4 w-4 text-slate-400" />
                  ) : (
                    <ChevronRight className="h-4 w-4 text-slate-400" />
                  )}
                  {monthLabel(month.periodMonth)}
                  {month.hasOverdue && (
                    <span className="rounded-md bg-red-50 px-1.5 py-0.5 text-xs font-medium text-red-700">
                      Muddati o'tgan
                    </span>
                  )}
                </span>
                <span className="flex items-center gap-4 text-sm">
                  <span className="text-slate-500">
                    To'landi:{' '}
                    <span className="font-medium text-emerald-600">{formatMoney(month.paid)}</span>
                  </span>
                  <span className="text-slate-500">
                    Qoldiq:{' '}
                    <span
                      className={cn(
                        'font-semibold',
                        month.remaining > 0 ? 'text-red-600' : 'text-slate-400',
                      )}
                    >
                      {formatMoney(month.remaining)}
                    </span>
                  </span>
                </span>
              </button>

              {open && <CategoryTable categories={month.categories} month={month} />}
            </div>
          )
        })}
      </div>
    </Card>
  )
}

function CategoryTable({
  categories,
  month,
}: {
  categories: PortalCategoryLine[]
  month: PortalMonth
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead className="whitespace-nowrap bg-white text-xs uppercase tracking-wide text-slate-400">
          <tr>
            <th className="px-4 py-2">Toifa</th>
            <th className="px-4 py-2 text-right">Summa</th>
            <th className="px-4 py-2 text-right">Chegirma</th>
            <th className="px-4 py-2 text-right">To'langan</th>
            <th className="px-4 py-2 text-right">Qoldiq</th>
            <th className="px-4 py-2 text-center">Holat</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {categories.map((line) => {
            const status = shownStatus(line)
            const isVoid = status === 'void'
            return (
              <tr
                key={line.categoryCode}
                className={cn('hover:bg-slate-50/60', isVoid && 'text-slate-400')}
              >
                <td className={cn('px-4 py-2.5 font-medium text-slate-700', isVoid && 'text-slate-400 line-through')}>
                  {line.categoryName}
                </td>
                <td className="whitespace-nowrap px-4 py-2.5 text-right text-slate-600">
                  {formatMoney(line.amount)}
                </td>
                <td
                  className={cn(
                    'whitespace-nowrap px-4 py-2.5 text-right',
                    line.discount > 0 ? 'font-medium text-amber-600' : 'text-slate-300',
                  )}
                >
                  {line.discount > 0 ? `−${formatMoney(line.discount)}` : '—'}
                </td>
                <td className="whitespace-nowrap px-4 py-2.5 text-right font-medium text-emerald-600">
                  {formatMoney(line.paid)}
                </td>
                <td
                  className={cn(
                    'whitespace-nowrap px-4 py-2.5 text-right font-semibold',
                    line.remaining > 0 ? 'text-red-600' : 'text-slate-400',
                  )}
                >
                  {formatMoney(line.remaining)}
                </td>
                <td className="px-4 py-2.5 text-center">
                  <span
                    className={cn(
                      'whitespace-nowrap rounded-md px-2 py-0.5 text-xs font-medium',
                      statusStyles[status],
                    )}
                  >
                    {statusLabels[status]}
                  </span>
                </td>
              </tr>
            )
          })}
        </tbody>
        <tfoot className="border-t border-slate-200 bg-slate-50/70 text-sm font-semibold text-slate-700">
          <tr>
            <td className="px-4 py-2.5">Jami</td>
            <td className="whitespace-nowrap px-4 py-2.5 text-right">{formatMoney(month.amount)}</td>
            <td className="whitespace-nowrap px-4 py-2.5 text-right text-amber-700">
              {month.discount > 0 ? `−${formatMoney(month.discount)}` : '—'}
            </td>
            <td className="whitespace-nowrap px-4 py-2.5 text-right text-emerald-700">
              {formatMoney(month.paid)}
            </td>
            <td
              className={cn(
                'whitespace-nowrap px-4 py-2.5 text-right',
                month.remaining > 0 ? 'text-red-700' : 'text-slate-400',
              )}
            >
              {formatMoney(month.remaining)}
            </td>
            <td className="px-4 py-2.5" />
          </tr>
        </tfoot>
      </table>
    </div>
  )
}

/* ------------------------------------------------------------------ */
/*  To'lovlar + chek                                                   */
/* ------------------------------------------------------------------ */

function PaymentsSection({
  payments,
  busyReceipt,
  onReceipt,
}: {
  payments: PortalPayment[]
  busyReceipt: string | null
  onReceipt: (paymentId: string) => void
}) {
  return (
    <Card>
      <div className="mb-4 flex items-center gap-2">
        <ReceiptText className="h-5 w-5 text-brand-600" />
        <h2 className="font-semibold text-slate-800">To'lovlar tarixi</h2>
      </div>

      {payments.length === 0 ? (
        <p className="py-8 text-center text-sm text-slate-400">
          Hali to'lov qilinmagan. To'lov qabul qilinganda cheki shu yerda paydo bo'ladi.
        </p>
      ) : (
        <div className="space-y-3">
          {payments.map((payment) => (
            <PaymentRow
              key={payment.paymentId}
              payment={payment}
              busy={busyReceipt === payment.paymentId}
              onReceipt={onReceipt}
            />
          ))}
        </div>
      )}
    </Card>
  )
}

function PaymentRow({
  payment,
  busy,
  onReceipt,
}: {
  payment: PortalPayment
  busy: boolean
  onReceipt: (paymentId: string) => void
}) {
  const reversed = payment.reversal !== null

  return (
    <div
      className={cn(
        'rounded-xl border px-4 py-3',
        reversed ? 'border-red-200 bg-red-50/40' : 'border-slate-200',
      )}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="flex flex-wrap items-center gap-2">
            <span
              className={cn(
                'text-lg font-semibold',
                // Storno qilingan to'lov YASHIRILMAYDI — ustidan chiziladi.
                reversed ? 'text-slate-400 line-through' : 'text-emerald-600',
              )}
            >
              {formatMoney(payment.amount)}
            </span>
            {reversed && (
              <span className="rounded-md bg-red-100 px-2 py-0.5 text-xs font-semibold text-red-700">
                Bekor qilingan
              </span>
            )}
          </p>
          <p className="mt-0.5 text-sm text-slate-500">
            {formatDate(payment.receivedAt)} · {methodLabel(payment.method)} · Chek №
            {payment.receiptNo}
          </p>
          {payment.note && !reversed && (
            <p className="mt-0.5 text-xs text-slate-400">{payment.note}</p>
          )}
        </div>

        <Button
          variant="secondary"
          onClick={() => onReceipt(payment.paymentId)}
          disabled={busy}
          className="shrink-0"
        >
          {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <FileText className="h-4 w-4" />}
          Chek (PDF)
        </Button>
      </div>

      {/* Nima uchun to'langani — oy va toifa kesimida */}
      {payment.parts.length > 0 && (
        <ul className="mt-2.5 flex flex-wrap gap-1.5">
          {payment.parts.map((part, index) => (
            <li
              key={`${part.periodMonth}-${part.categoryCode}-${index}`}
              className={cn(
                'rounded-md px-2 py-1 text-xs',
                reversed ? 'bg-slate-100 text-slate-400 line-through' : 'bg-slate-100 text-slate-600',
              )}
            >
              {monthLabel(part.periodMonth)} · {part.categoryName} —{' '}
              <span className="font-medium">{formatMoney(part.amount)}</span>
            </li>
          ))}
        </ul>
      )}

      {payment.unallocated > 0 && !reversed && (
        <p className="mt-2 text-xs text-brand-600">
          Avans: {formatMoney(payment.unallocated)} — keyingi oylarga o'tkaziladi
        </p>
      )}

      {/* Bekor qilish sababi — yonida, yashirilmagan holda */}
      {payment.reversal && (
        <p className="mt-2.5 flex flex-wrap items-baseline gap-x-1.5 gap-y-0.5 border-t border-red-200 pt-2 text-xs text-red-700">
          <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
          <span className="font-semibold">Bekor qilindi:</span>
          <span>{payment.reversal.reason}</span>
          <span className="text-red-400">
            ({formatDate(payment.reversal.reversedAt)} · bekor qilish cheki №
            {payment.reversal.receiptNo})
          </span>
        </p>
      )}
    </div>
  )
}
