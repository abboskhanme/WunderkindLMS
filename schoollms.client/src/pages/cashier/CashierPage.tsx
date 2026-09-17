import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  AlertTriangle,
  Inbox,
  Receipt,
  RefreshCw,
  Search,
  ShieldOff,
  Undo2,
  UserRound,
  Wallet,
} from 'lucide-react'
import type { AllocationSuggestion, CashShift, Payment, PaymentMethod, Role } from '@/types'
import type { CashierStudent } from '@/api/services/cashier'
import {
  MIN_SEARCH_LENGTH,
  PROBE_AMOUNT,
  financeErrorMessage,
  getCurrentShift,
  isAborted,
  searchStudents,
  suggestAllocation,
} from '@/api/services/cashier'
import type { ExpenseInput } from '@/api/services/expenses'
import {
  attachExpenseFile,
  createExpense,
  getExpenseApprovalThreshold,
  needsApproval as expenseNeedsApproval,
} from '@/api/services/expenses'
import { uploadAdminFile } from '@/api/services/students'
import { billingErrorMessage, isEndpointMissing } from '@/api/services/billingError'
import { useAuth } from '@/context/auth-context'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Select, Textarea } from '@/components/ui/Input'
import { formatMoney, cn } from '@/lib/utils'
import { ExpenseFormModal } from '@/pages/admin/billing/ExpenseFormModal'
import { MoneyInput } from './MoneyInput'
import { ShiftBar } from './ShiftBar'
import { PaymentSplitModal } from './PaymentSplitModal'
import { ReceiptPreview } from './ReceiptPreview'
import { formatPeriod, formatSum, formatSumWithUnit, methodLabels, parseSum } from './format'

/* ==========================================================================
   BU SAHIFADA TAHRIRLASH VA O'CHIRISH TUGMASI YO'Q — ATAYLAB.
   --------------------------------------------------------------------------
   Mijoz aytgan xavf (SPEC §4 kirish qismi) aynan shu: kassir pulni oladi va
   yozuvni yo'q qiladi. Shuning uchun himoya uch qavatda, va ekran — eng
   yuqori, eng ko'rinadigan qavati:
     1) bu yerda bunday boshqaruv umuman chizilmagan;
     2) backend'da `payments` uchun mos HTTP fe'llari yozilmagan
        (`PaymentsController.cs` faylining boshidagi izoh) va
        `FinanceMatrix.EditOrDeletePayment` qoidasining rollar ro'yxati bo'sh;
     3) bazada `app_rw` roli `payments` jadvalini o'zgartira olmaydi — 42501.
   Xato to'lov FAQAT storno bilan tuzatiladi, storno esa admin/direktor amali
   (SPEC §4.3), ya'ni bu ekranga umuman tegishli emas.
   ========================================================================== */

/** Kassaga kira oladigan rollar (SPEC §4.3, `FinanceAction.AcceptPayment`). */
const CASH_DESK_ROLES: Role[] = ['cashier', 'admin', 'superadmin']

const METHODS: PaymentMethod[] = ['cash', 'card', 'transfer', 'online']

/**
 * Kassir ish o'rni (P1-16). ATAYLAB TOR EKRAN.
 *
 * Kassir shu yerda faqat bitta ishni qiladi: o'quvchini topadi, uning ochiq
 * hisob-fakturalarini ko'radi, summa kiritadi, toifalar bo'yicha taqsimlaydi
 * va chek beradi. Boshqa hech narsa yo'q — ro'yxatlar, hisobotlar, sozlamalar
 * va o'tgan to'lovlar tarixi ham.
 *
 * SMENASIZ TO'LOV SHAKLI KO'RINMAYDI (SPEC §4.2). Bu "tugma o'chiq" degani
 * emas: ochiq smena bo'lmasa qidiruv ham, summa maydoni ham umuman
 * chizilmaydi, o'rniga "Smenani oching" turadi. Sabab — har bir chek qaysi
 * smenaga tegishli ekani bilan yoziladi va chek raqami smena ichida uzluksiz
 * bo'lishi kerak; smenasiz bu savolning javobi yo'q.
 *
 * CHIQIM VA QAYTARIM — SHU YERDA, FAQAT ADMIN/DIREKTORGA (2026-09-18)
 * ---------------------------------------------------------------------
 * `navigation.ts` Moliya menyusini EduSchool ro'yxatiga aynan moslashtirgach
 * (commit 7270989), "Chiqimlar" va "Qaytarimlar" hech qanday menyuda
 * qolmadi — faqat URL orqali ochiladi edi. EduSchool'da ham xuddi shunday:
 * alohida menyu yozuvi yo'q, ikkalasi ham `/cash` ekranining o'zida —
 * cashbox kartochkasidagi INCOME/EXPENSE tugmalari qatorida (finance-parity.md
 * §2.1.1: "four buttons: INCOME (payIn), EXPENSE (payOut), MOVING, EXCHANGE").
 * Bizning `/cashier` xuddi shu kartochka o'rnini bosadi (§2.1.2: "roles
 * cashier, admin, superadmin"), shuning uchun ular shu yerga qo'shildi —
 * yangi marshrut yo'q, `navigation.ts` ga tegilmagan.
 *
 * "BOSHQA HECH NARSA YO'Q" QOIDASI KASSIRGA TEGISHLI, ADMINGA EMAS. Yuqoridagi
 * "ro'yxatlar, hisobotlar... yo'q" — kassirning pul olib, yozuvni yo'qota
 * olmasligini ta'minlash uchun edi (SPEC §4 xavfi). Chiqim yozish va
 * qaytarim so'rash kassirning vakolati EMAS (`access.ts`: `canRecordExpense`
 * — admin/superadmin; `RefundsPage.tsx`: `canRequest` — admin/superadmin);
 * pastdagi blok FAQAT shu ikki rolga ko'rinadi, oddiy kassir uni umuman
 * ko'rmaydi — ekran unga hamon bitta ishni qiladigan tor ekran bo'lib qoladi.
 *
 * "Yangi chiqim" — mavjud `ExpenseFormModal`ni O'ZGARTIRMAY shu yerga ochadi
 * (EduSchool'ning drawer'i kabi, sahifadan chiqmasdan); yozish yo'li xuddi
 * `ExpensesPage.tsx`dagi bilan bir xil — `createExpense` → fayllar bo'lsa
 * `attachExpenseFile`, ikkalasi ham O'ZGARTIRILMAGAN. To'liq ro'yxat, tasdiq
 * navbati va tarix uchun "Chiqimlar ro'yxati" havolasi `ExpensesPage`ning
 * o'ziga olib boradi — u yerda ikkinchi tasdiq ham beriladi, buni bu yerga
 * ko'chirish shart emas.
 *
 * "Qaytarimlar" — faqat havola, `RefundsPage.tsx`ga. U o'zi to'liq: so'rash,
 * tasdiqlash, rad etish, storno — hammasi bitta sahifada, alohida ulash
 * kerak emas (u yerdagi `RequestRefundModal` sahifadan eksport qilinmagan,
 * shuning uchun bu yerga import qilib bo'lmaydi — va shart ham emas).
 */
export function CashierPage() {
  const { user } = useAuth()

  /* ---- Smena ---- */
  const [shift, setShift] = useState<CashShift | null>(null)
  const [shiftLoading, setShiftLoading] = useState(true)
  const [shiftError, setShiftError] = useState<string | null>(null)

  const loadShift = useCallback(async () => {
    setShiftLoading(true)
    setShiftError(null)
    try {
      setShift(await getCurrentShift())
    } catch (err) {
      setShiftError(financeErrorMessage(err, "Smena holatini aniqlab bo'lmadi."))
    } finally {
      setShiftLoading(false)
    }
  }, [])

  const allowed = user !== null && CASH_DESK_ROLES.includes(user.role)
  /** Chiqim/qaytarim — kassirning vakolati emas (`access.ts`, `RefundsPage.tsx`). */
  const isFinanceAdmin = user !== null && (user.role === 'admin' || user.role === 'superadmin')

  useEffect(() => {
    if (allowed) void loadShift()
  }, [allowed, loadShift])

  /* ---- Chiqim (F1.11 — kassa ekranidan, EduSchool'dagi kabi) ---- */
  const [expenseFormOpen, setExpenseFormOpen] = useState(false)
  const [expenseBusy, setExpenseBusy] = useState(false)
  const [expenseError, setExpenseError] = useState<string | null>(null)
  const [expenseNotice, setExpenseNotice] = useState<string | null>(null)
  const [expenseThreshold, setExpenseThreshold] = useState<number | null>(null)

  useEffect(() => {
    if (!isFinanceAdmin) return
    let alive = true
    getExpenseApprovalThreshold()
      .then((value) => {
        if (alive) setExpenseThreshold(value)
      })
      .catch(() => undefined)
    return () => {
      alive = false
    }
  }, [isFinanceAdmin])

  /**
   * `ExpensesPage.tsx`dagi `handleCreate` bilan AYNAN bir xil yo'l: chiqim
   * yoziladi, so'ng (bo'lsa) hujjatlar biriktiriladi. `createExpense` va
   * `attachExpenseFile` — o'sha bir xil, o'zgartirilmagan servis funksiyalari;
   * bu yerda faqat ULARNI CHAQIRISH takrorlangan, chiqim qanday yozilishi
   * (server so'rovi, maydonlar) emas.
   */
  const handleCreateExpense = async (values: ExpenseInput, files: File[]) => {
    setExpenseBusy(true)
    setExpenseError(null)
    try {
      const created = await createExpense(values)
      let attachError: string | null = null
      for (const file of files) {
        try {
          const uploaded = await uploadAdminFile(file)
          await attachExpenseFile(created.id, {
            fileUrl: uploaded.url,
            fileName: uploaded.name,
            contentType: uploaded.contentType,
            sizeBytes: uploaded.size,
          })
        } catch (e: unknown) {
          attachError = billingErrorMessage(e, `"${file.name}" biriktirilmadi`)
          break
        }
      }
      setExpenseFormOpen(false)
      setExpenseNotice(
        (expenseNeedsApproval(created)
          ? `Chiqim yozildi va tasdiq navbatiga tushdi (${formatMoney(created.amount)}).`
          : `Chiqim yozildi (${formatMoney(created.amount)}).`) +
          (attachError === null ? '' : ` Lekin hujjat biriktirilmadi: ${attachError}`),
      )
    } catch (e: unknown) {
      setExpenseError(
        isEndpointMissing(e)
          ? "Chiqim yozish endpoint'i hali ulanmagan."
          : billingErrorMessage(e, "Chiqimni saqlab bo'lmadi"),
      )
    } finally {
      setExpenseBusy(false)
    }
  }

  /* ---- Tanlangan o'quvchi va to'lov shakli ---- */
  const [student, setStudent] = useState<CashierStudent | null>(null)
  const [amountRaw, setAmountRaw] = useState('')
  const [method, setMethod] = useState<PaymentMethod>('cash')
  const [note, setNote] = useState('')
  const [splitOpen, setSplitOpen] = useState(false)
  const [receipt, setReceipt] = useState<Payment | null>(null)

  const amount = parseSum(amountRaw)

  const resetForm = () => {
    setAmountRaw('')
    setNote('')
    setMethod('cash')
  }

  const selectStudent = (next: CashierStudent) => {
    setStudent(next)
    resetForm()
  }

  /* ---- Tanlangan o'quvchining ochiq hisob-fakturalari ---- */
  const [invoices, setInvoices] = useState<AllocationSuggestion[]>([])
  const [invoicesLoading, setInvoicesLoading] = useState(false)
  const [invoicesError, setInvoicesError] = useState<string | null>(null)
  const [invoicesReload, setInvoicesReload] = useState(0)

  useEffect(() => {
    if (!student) {
      setInvoices([])
      setInvoicesError(null)
      return
    }

    const controller = new AbortController()
    setInvoicesLoading(true)
    setInvoicesError(null)

    // `PROBE_AMOUNT` — ro'yxatni KO'RISH uchun: backend `amount <= 0` da bo'sh
    // ro'yxat qaytaradi, shuning uchun 0 yubora olmaymiz. Bu yerda faqat
    // `remaining` o'qiladi; FIFO taklifi haqiqiy summa bilan taqsimot
    // oynasida so'raladi.
    suggestAllocation(student.id, PROBE_AMOUNT, controller.signal)
      .then((rows) => {
        setInvoices(rows)
        setInvoicesLoading(false)
      })
      .catch((err: unknown) => {
        if (isAborted(err)) return
        setInvoicesError(financeErrorMessage(err, "Hisob-fakturalarni yuklab bo'lmadi."))
        setInvoicesLoading(false)
      })

    return () => controller.abort()
  }, [student, invoicesReload])

  const debt = useMemo(
    () => invoices.reduce((sum, row) => sum + row.remaining, 0),
    [invoices],
  )

  /* ---- Ruxsat: yo'q bo'lsa boshqaruv umuman chizilmaydi ---- */
  if (!allowed) {
    return (
      <Card className="border-slate-200">
        <div className="flex flex-col items-center gap-2 py-12 text-center">
          <ShieldOff className="h-8 w-8 text-slate-300" />
          <p className="font-medium text-slate-600">Kassa bo'limi sizga ochiq emas</p>
          <p className="max-w-sm text-sm text-slate-400">
            To'lov qabul qilish kassir, administrator va direktor uchun (SPEC §4.3).
          </p>
        </div>
      </Card>
    )
  }

  const canSubmit = student !== null && amount !== null && amount > 0

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-xl font-semibold text-slate-800">Kassa</h1>
        <p className="text-sm text-slate-400">
          O'quvchini toping, summani kiriting va chek bering.
        </p>
      </header>

      <ShiftBar
        shift={shift}
        loading={shiftLoading}
        error={shiftError}
        onRetry={() => void loadShift()}
        onShiftChange={(next) => {
          setShift(next)
          if (!next) {
            setStudent(null)
            resetForm()
          }
        }}
      />

      {/*
        Chiqim va qaytarim — faqat admin/direktorga, kassirga umuman
        ko'rinmaydi (yuqoridagi izoh). Smena holatidan qat'i nazar
        chiqariladi: faqat NAQD chiqim ochiq smenani talab qiladi (F1.03),
        boshqa usullar — yo'q, `ExpenseFormModal`ning o'zi buni ogohlantiradi.
      */}
      {isFinanceAdmin && (
        <Card className="flex flex-wrap items-center justify-between gap-3 border-slate-200 bg-slate-50/60">
          <div>
            <p className="text-sm font-medium text-slate-700">Boshqa moliya amallari</p>
            <p className="text-xs text-slate-400">
              EduSchool'da ham chiqim va qaytarim shu — kassa — ekranidan boshlanadi
              (finance-parity.md §2.1).
            </p>
            {expenseNotice && (
              <p className="mt-1 text-xs font-medium text-emerald-600">{expenseNotice}</p>
            )}
          </div>
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" onClick={() => setExpenseFormOpen(true)}>
              <Receipt className="h-4 w-4" /> Yangi chiqim
            </Button>
            <Link
              to="/admin/billing/expenses"
              className="inline-flex items-center justify-center gap-2 rounded-lg border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition-colors hover:bg-slate-50"
            >
              Chiqimlar ro'yxati
            </Link>
            <Link
              to="/admin/finance/refunds"
              className="inline-flex items-center justify-center gap-2 rounded-lg border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition-colors hover:bg-slate-50"
            >
              <Undo2 className="h-4 w-4" /> Qaytarimlar
            </Link>
          </div>
        </Card>
      )}

      {isFinanceAdmin && (
        <ExpenseFormModal
          open={expenseFormOpen}
          busy={expenseBusy}
          error={expenseError}
          approvalThreshold={expenseThreshold}
          onClose={() => setExpenseFormOpen(false)}
          onSubmit={handleCreateExpense}
        />
      )}

      {/* SMENA OCHIQ BO'LMASA — TO'LOV SHAKLI UMUMAN CHIZILMAYDI (SPEC §4.2). */}
      {shift && (
        <div className="grid gap-6 lg:grid-cols-[minmax(320px,380px)_1fr]">
          <StudentSearch selectedId={student?.id ?? null} onSelect={selectStudent} />

          {!student ? (
            <Card>
              <div className="flex flex-col items-center gap-2 py-16 text-center">
                <UserRound className="h-8 w-8 text-slate-300" />
                <p className="font-medium text-slate-600">O'quvchi tanlanmagan</p>
                <p className="max-w-sm text-sm text-slate-400">
                  Chapdagi qidiruvdan o'quvchini toping — uning ochiq hisob-fakturalari
                  va to'lov shakli shu yerda ochiladi.
                </p>
              </div>
            </Card>
          ) : (
            <div className="space-y-4">
              <Card>
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <p className="font-semibold text-slate-800">{student.fullName}</p>
                    <p className="text-sm text-slate-500">
                      {student.className}
                      {student.parentFullName ? ` · ${student.parentFullName}` : ''}
                      {student.parentPhone ? ` · ${student.parentPhone}` : ''}
                    </p>
                  </div>
                  {!invoicesLoading && !invoicesError && (
                    <div className="text-right">
                      <p className="text-xs uppercase tracking-wide text-slate-400">Jami qarz</p>
                      <p
                        className={cn(
                          'text-lg font-semibold tabular-nums',
                          debt > 0 ? 'text-red-600' : 'text-emerald-600',
                        )}
                      >
                        {formatSumWithUnit(debt)}
                      </p>
                    </div>
                  )}
                </div>
              </Card>

              <InvoiceList
                invoices={invoices}
                loading={invoicesLoading}
                error={invoicesError}
                onRetry={() => setInvoicesReload((n) => n + 1)}
              />

              <Card>
                <h2 className="mb-4 font-semibold text-slate-800">To'lov</h2>
                <div className="grid gap-4 sm:grid-cols-2">
                  <MoneyInput
                    label="Summa (so'm)"
                    value={amountRaw}
                    onValueChange={setAmountRaw}
                    placeholder="0"
                    invalid={amountRaw.length > 0 && (amount === null || amount <= 0)}
                    hint={
                      amountRaw.length > 0 && (amount === null || amount <= 0)
                        ? "Summa noldan katta bo'lishi kerak."
                        : undefined
                    }
                  />
                  <Select
                    label="To'lov usuli"
                    value={method}
                    onChange={(e) => setMethod(e.target.value as PaymentMethod)}
                  >
                    {METHODS.map((m) => (
                      <option key={m} value={m}>
                        {methodLabels[m]}
                      </option>
                    ))}
                  </Select>
                </div>

                <div className="mt-4">
                  <Textarea
                    label="Izoh (ixtiyoriy)"
                    rows={2}
                    value={note}
                    onChange={(e) => setNote(e.target.value)}
                    placeholder="Masalan: sentabr uchun, otasi to'ladi"
                  />
                </div>

                <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
                  {debt > 0 && !invoicesLoading && !invoicesError ? (
                    <Button
                      variant="ghost"
                      onClick={() => setAmountRaw(String(debt))}
                      type="button"
                    >
                      Butun qarzni qo'yish ({formatSum(debt)})
                    </Button>
                  ) : (
                    <span />
                  )}

                  <Button disabled={!canSubmit} onClick={() => setSplitOpen(true)}>
                    <Wallet className="h-4 w-4" /> Taqsimlash va qabul qilish
                  </Button>
                </div>
              </Card>
            </div>
          )}
        </div>
      )}

      {student && splitOpen && amount !== null && amount > 0 && (
        <PaymentSplitModal
          open
          student={student}
          amount={amount}
          method={method}
          note={note}
          onClose={() => setSplitOpen(false)}
          onAccepted={(payment) => {
            setSplitOpen(false)
            setReceipt(payment)
          }}
        />
      )}

      {receipt && (
        <ReceiptPreview
          open
          payment={receipt}
          onClose={() => {
            setReceipt(null)
            resetForm()
            setInvoicesReload((n) => n + 1)
            void loadShift()
          }}
        />
      )}
    </div>
  )
}

/* ==========================================================================
   O'quvchi qidiruvi
   ========================================================================== */

interface StudentSearchProps {
  selectedId: string | null
  onSelect: (student: CashierStudent) => void
}

/**
 * Qidiruv paneli. To'rtta holat ham chizilgan: yo'l-yo'riq (juda qisqa
 * so'rov), yuklanmoqda, xato va bo'sh natija.
 *
 * Qidiruv 300 ms kechikish bilan yuboriladi va oldingi so'rov uziladi —
 * kassir tez yozadi, har harf uchun so'rov yuborish serverni ham, ro'yxatni
 * ham sakratadi.
 */
function StudentSearch({ selectedId, onSelect }: StudentSearchProps) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<CashierStudent[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)

  const term = query.trim()
  const tooShort = term.length < MIN_SEARCH_LENGTH

  useEffect(() => {
    if (tooShort) {
      setResults([])
      setError(null)
      setLoading(false)
      return
    }

    const controller = new AbortController()
    const timer = setTimeout(() => {
      setLoading(true)
      setError(null)
      searchStudents(term, controller.signal)
        .then((rows) => {
          setResults(rows)
          setLoading(false)
        })
        .catch((err: unknown) => {
          if (isAborted(err)) return
          setError(financeErrorMessage(err, "Qidiruvni bajarib bo'lmadi."))
          setLoading(false)
        })
    }, 300)

    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [term, tooShort, attempt])

  return (
    <Card className="flex h-fit flex-col gap-3">
      <label className="block">
        <span className="mb-1 block text-sm font-medium text-slate-600">O'quvchini qidirish</span>
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Ism, ota-ona yoki telefon"
            autoComplete="off"
            className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
          />
        </div>
      </label>

      {tooShort && (
        <p className="py-6 text-center text-sm text-slate-400">
          Kamida {MIN_SEARCH_LENGTH} ta belgi yozing.
        </p>
      )}

      {!tooShort && loading && <Loader className="py-8" label="Qidirilmoqda..." />}

      {!tooShort && !loading && error && (
        <div className="rounded-xl border border-red-200 bg-red-50/70 px-3 py-3">
          <div className="flex items-start gap-2 text-sm text-red-700">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{error}</span>
          </div>
          <Button
            variant="secondary"
            className="mt-3"
            onClick={() => setAttempt((n) => n + 1)}
          >
            <RefreshCw className="h-4 w-4" /> Qayta urinish
          </Button>
        </div>
      )}

      {!tooShort && !loading && !error && results.length === 0 && (
        <div className="flex flex-col items-center gap-2 py-8 text-center">
          <Inbox className="h-7 w-7 text-slate-300" />
          <p className="text-sm text-slate-500">"{term}" bo'yicha o'quvchi topilmadi.</p>
        </div>
      )}

      {!tooShort && !loading && !error && results.length > 0 && (
        <ul className="max-h-[28rem] divide-y divide-slate-100 overflow-y-auto">
          {results.map((s) => (
            <li key={s.id}>
              <button
                type="button"
                onClick={() => onSelect(s)}
                className={cn(
                  'w-full rounded-lg px-3 py-2 text-left transition-colors',
                  s.id === selectedId ? 'bg-brand-50' : 'hover:bg-slate-50',
                )}
              >
                <span
                  className={cn(
                    'block text-sm font-medium',
                    s.id === selectedId ? 'text-brand-800' : 'text-slate-800',
                  )}
                >
                  {s.fullName}
                </span>
                <span className="block text-xs text-slate-400">
                  {s.className}
                  {s.parentFullName ? ` · ${s.parentFullName}` : ''}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}

/* ==========================================================================
   Ochiq hisob-fakturalar
   ========================================================================== */

interface InvoiceListProps {
  invoices: AllocationSuggestion[]
  loading: boolean
  error: string | null
  onRetry: () => void
}

function InvoiceList({ invoices, loading, error, onRetry }: InvoiceListProps) {
  return (
    <Card>
      <h2 className="mb-3 font-semibold text-slate-800">Ochiq hisob-fakturalar</h2>

      {loading && <Loader className="py-8" label="Yuklanmoqda..." />}

      {!loading && error && (
        <div className="rounded-xl border border-red-200 bg-red-50/70 px-3 py-3">
          <div className="flex items-start gap-2 text-sm text-red-700">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{error}</span>
          </div>
          <Button variant="secondary" className="mt-3" onClick={onRetry}>
            <RefreshCw className="h-4 w-4" /> Qayta urinish
          </Button>
        </div>
      )}

      {!loading && !error && invoices.length === 0 && (
        <div className="flex flex-col items-center gap-2 py-8 text-center">
          <Inbox className="h-7 w-7 text-slate-300" />
          <p className="text-sm text-slate-500">Qarz yo'q — barcha hisob-fakturalar yopilgan.</p>
        </div>
      )}

      {!loading && !error && invoices.length > 0 && (
        <ul className="divide-y divide-slate-100">
          {invoices.map((row) => (
            <li
              key={row.invoiceId}
              className="flex items-center justify-between gap-3 py-2 text-sm"
            >
              <div>
                <span className="font-medium text-slate-800">{row.categoryName}</span>
                <span className="ml-2 text-xs text-slate-400">
                  {formatPeriod(row.periodMonth)}
                </span>
              </div>
              <span className="font-semibold tabular-nums text-slate-700">
                {formatSum(row.remaining)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
