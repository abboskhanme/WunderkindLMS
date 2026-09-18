import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  AlertTriangle,
  Download,
  Filter,
  Inbox,
  RefreshCw,
  Search,
  ShieldOff,
  UserRound,
  Wallet,
  X,
} from 'lucide-react'
import type { AllocationSuggestion, Payment, PaymentMethod, Role, SchoolClass } from '@/types'
import type { CashierStudent } from '@/api/services/cashier'
import {
  MIN_SEARCH_LENGTH,
  PROBE_AMOUNT,
  financeErrorMessage,
  isAborted,
  searchStudents,
  suggestAllocation,
} from '@/api/services/cashier'
import type { CashBox, CashBoxTransactionRow, CashBoxTransactionsResult } from '@/api/services/cashBoxes'
import { cancelCashBoxTransaction, cashBoxIn, getCashBoxTransactions, getCashBoxes } from '@/api/services/cashBoxes'
import type { StudentContract } from '@/api/services/studentContracts'
import { searchStudentContracts } from '@/api/services/studentContracts'
import { getClasses } from '@/api/services/classes'
import { useAuth } from '@/context/auth-context'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Select, Textarea } from '@/components/ui/Input'
import { cn, exportToCsv } from '@/lib/utils'
import { ReasonModal } from '@/pages/admin/billing/ReasonModal'
import { MoneyInput } from './MoneyInput'
import { PaymentSplitModal } from './PaymentSplitModal'
import { ReceiptPreview } from './ReceiptPreview'
import { CashBoxPanel } from './CashBoxPanel'
import { CashBoxFormModal } from './CashBoxFormModal'
import { CashBoxActionModal } from './CashBoxActionModal'
import type { CashBoxActionMode } from './CashBoxCard'
import { CashLedger } from './CashLedger'
import {
  formatDateTime,
  formatPeriod,
  formatSum,
  formatSumWithUnit,
  kindLabel,
  methodLabels,
  parseSum,
  statusLabel,
} from './format'

/** Kassa harakati turlari — "Tranzaksiya turi" filtri shu ro'yxatdan (format.ts dagi kindLabel bilan bir xil to'rttasi). */
const TRANSACTION_KINDS: CashBoxTransactionRow['kind'][] = ['in', 'out', 'transfer', 'exchange']

/* ==========================================================================
   BU SAHIFADA TO'LOVNI TAHRIRLASH VA O'CHIRISH TUGMASI YO'Q — ATAYLAB.
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
   (SPEC §4.3), ya'ni bu ekranga umuman tegishli emas. Kassa tranzaksiyalari
   (kirim/chiqim/ko'chirish/ayirboshlash) ham xuddi shunday — bekor qilinadi,
   o'chirilmaydi (`cancelCashBoxTransaction`).

   SMENA YO'Q (mijoz, 2026-09-18): "bizni tizimda smena degan tushuncha
   umuman bo'lmasin butunlay olib tashla, shunchaki kassa degan narsa
   bo'lsin xolos, bizda bir nechta kassa bo'lishi mumkin, ular har bir
   alohida pul kirim chiqim qilishi va o'zaro o'tkazma qilishi mumkin."
   Ochish/yopish, sanalgan naqd, nomuvofiqlik — hech biri yo'q. O'rniga:
   bir nechta KASSA, har birining o'z qoldig'i va to'rtta amali (Kirim,
   Chiqim, Ko'chirish, Ayirboshlash). Tuzilma EduSchool'nikiga o'xshaydi
   (mijoz screenshot yubordi), ko'rinish esa o'zimiznikicha qoladi.
   ========================================================================== */

/** Kassaga kira oladigan rollar (SPEC §4.3, `FinanceAction.AcceptPayment`). */
const CASH_DESK_ROLES: Role[] = ['cashier', 'admin', 'superadmin']

/**
 * Kassa qo'shish/tahrirlash — faqat boshqaruvchi. Kirim/chiqim/ko'chirish/
 * ayirboshlash kassirga ham ochiq: bular kassirning kundalik ishi.
 */
const CASH_BOX_MANAGE_ROLES: Role[] = ['admin', 'superadmin']

const METHODS: PaymentMethod[] = ['cash', 'card', 'transfer', 'online']

const dateInputClass =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

const todayStr = () => new Date().toISOString().slice(0, 10)

/**
 * Kassir ish o'rni (P1-16 → kassalarga o'tish, 2026-09-18; EduSchool
 * skrinshotiga moslash va Kirim panelining ikkinchi tabi, 2026-09-18 kech).
 *
 * Ikki bo'lim bor:
 *   1) KASSALAR — bir nechta kassa, har birining qoldig'i, kirim/chiqim/
 *      ko'chirish/ayirboshlash va umumiy tranzaksiyalar jadvali (filtr
 *      qatori + ustunlar sozlamasi bilan).
 *   2) KIRIM PANELI — tanlangan kassaning "Kirim" tugmasi ochadi, ikki tabi
 *      bor: "Oddiy kirim" (`cashBoxIn`) va "O'quvchidan to'lov" — eski yo'l,
 *      O'ZGARTIRILMAGAN: hisob-fakturaga taqsimlanadigan to'lov ilgarigidek
 *      `acceptPayment` (`/cash/payments`) orqali ketadi. Bu FIFO taqsimotni,
 *      hisob-fakturalarni bilmagan umumiy "Kirim" amalidan TUBDAN farq
 *      qiladi — shuning uchun ikkalasi ham bor va bir-biriga
 *      aylantirilmagan. Sarlavhadagi alohida tugma OLIB TASHLANDI (mijoz,
 *      2026-09-18): "o'quvchidan to'lov degan button ham bu yerda kerak
 *      emas" — lekin OQIM o'zi Kirim panelining tabi sifatida qolmoqda.
 */
export function CashierPage() {
  const { user } = useAuth()
  const allowed = user !== null && CASH_DESK_ROLES.includes(user.role)
  const canManageBoxes = user !== null && CASH_BOX_MANAGE_ROLES.includes(user.role)

  /* ---- Kassalar ---- */
  const [boxes, setBoxes] = useState<CashBox[]>([])
  const [boxesLoading, setBoxesLoading] = useState(true)
  const [boxesError, setBoxesError] = useState<string | null>(null)
  const [selectedBoxId, setSelectedBoxId] = useState<string | null>(null)

  const loadBoxes = useCallback(async () => {
    setBoxesLoading(true)
    setBoxesError(null)
    try {
      const rows = await getCashBoxes()
      setBoxes(rows)
      setSelectedBoxId((current) => {
        if (current && rows.some((b) => b.id === current)) return current
        return rows.find((b) => b.isDefault)?.id ?? rows[0]?.id ?? null
      })
    } catch (err) {
      setBoxesError(financeErrorMessage(err, "Kassalarni yuklab bo'lmadi."))
    } finally {
      setBoxesLoading(false)
    }
  }, [])

  useEffect(() => {
    if (allowed) void loadBoxes()
  }, [allowed, loadBoxes])

  const [boxFormOpen, setBoxFormOpen] = useState(false)
  const [editingBox, setEditingBox] = useState<CashBox | null>(null)
  // `CashBoxActionModal` endi FAQAT chiqim/ko'chirish/ayirboshlash uchun —
  // Kirim pastdagi `incomePanel` orqali ochiladi (izoh: fayl oxiridagi tab bo'limi).
  const [actionState, setActionState] = useState<{ box: CashBox; mode: Exclude<CashBoxActionMode, 'in'> } | null>(
    null,
  )

  /* ==========================================================================
     KIRIM PANELI — mijoz "O'quvchidan to'lov" tugmasini bosh sahifadan olib
     tashlashni so'radi (2026-09-18, ikkinchi xat): "o'quvchidan to'lov degan
     button ham bu yerda kerak emas". Lekin IMKONIYAT qolishi kerak — bugun
     sinaladigan yagona oqim shu. EduSchool'da o'quvchi to'lovi ham kassaning
     o'z Kirim amali orqali kiritiladi (`finance-parity.md` §2.1), shuning
     uchun bu yerda ham Kirim ikki rejimli: "Oddiy kirim" (mavjud
     `cashBoxIn`) va "O'quvchidan to'lov" (eski yo'l — StudentSearch →
     hisob-fakturalar → PaymentSplitModal → ReceiptPreview — TEGILMAGAN).

     MODAL EMAS, PANEL: agar Kirim ham `CashBoxActionModal` (Modal) bo'lib
     qolsa, "O'quvchidan to'lov" rejimida `PaymentSplitModal` uning USTIGA
     ochilib, ikki oyna bir-birining ustiga chiqadi — bunga yo'l qo'ymaslik
     so'ralgan edi. Shuning uchun Kirim sahifa ichidagi ODDIY BO'LIM
     (avvalgi "O'QUVCHIDAN TO'LOV" bo'limi bilan bir xil joyda), va
     `PaymentSplitModal` sahifa ustiga ochiladi — hech qachon ikkinchi oyna
     ustiga emas. */
  const [incomePanel, setIncomePanel] = useState<CashBox | null>(null)
  const [incomeTab, setIncomeTab] = useState<'plain' | 'student'>('plain')

  /* ---- Tranzaksiyalar jadvali ---- */
  const [from, setFrom] = useState(todayStr())
  const [to, setTo] = useState(todayStr())
  // Filtr qatori — EduSchool skrinshotida doim ko'rinadi, lekin mijoz keyin
  // (2026-09-18, soatlar farqi bilan) fikrini o'zgartirdi: "filter buttoni
  // bosilsa filterlar tushib chiqadi, yani bekinadi yoki ko'rinadi" — demak
  // tugma bilan YOPIQ/OCHIQ bo'lishi kerak, oldingi "Filtr" tugmasi kabi.
  const [filtersOpen, setFiltersOpen] = useState(false)
  const [boxFilter, setBoxFilter] = useState('')
  const [q, setQ] = useState('')

  /* ---- Qo'shimcha filtrlar (EduSchool skrinshoti): To'lov usuli, Tranzaksiya
     turi, O'quvchi, Sinf. Backend `/cash-boxes/transactions` faqat
     `from/to/boxId/q` qabul qiladi (CashBoxesController.cs), shuning uchun
     bu to'rttasi JADVAL QATORLARI ustida MIJOZ TOMONDA filtrlanadi —
     `filteredRows` pastda. Yig'indi kartochkalari esa serverdan kelgan
     davr/kassa yig'indisi bo'lib qoladi (`CashLedger`: "Pulni bu yerda
     HECH KIM hisoblamaydi" qoidasi) — shu to'rttasi ishga tushganda
     kartochkalar ostida ogohlantiruvchi izoh chiqadi. */
  const [methodFilter, setMethodFilter] = useState<PaymentMethod | ''>('')
  const [kindFilter, setKindFilter] = useState<CashBoxTransactionRow['kind'] | ''>('')

  // "O'quvchi" filtri — jadval qatorida faqat `contractNo` bor (o'quvchining
  // ismi YO'Q, faqat shartnoma raqami). Shuning uchun o'quvchi tanlanganda
  // uning shartnoma raqami(lari) `/admin/student-contracts` orqali olinadi
  // va qatorlar shu raqamlarga qarab filtrlanadi.
  const [studentQuery, setStudentQuery] = useState('')
  const [studentResults, setStudentResults] = useState<StudentContract[]>([])
  const [studentSearchOpen, setStudentSearchOpen] = useState(false)
  const [studentFilter, setStudentFilter] = useState<{ label: string; numbers: Set<string> } | null>(null)

  // "Sinf" filtri — xuddi shu naqsh: sinf tanlanganda o'sha sinfdagi barcha
  // o'quvchilarning shartnoma raqamlari yig'ib olinadi.
  const [classList, setClassList] = useState<SchoolClass[]>([])
  const [classFilter, setClassFilter] = useState('')
  const [classFilterNumbers, setClassFilterNumbers] = useState<Set<string> | null>(null)
  const [classFilterLoading, setClassFilterLoading] = useState(false)

  const [ledger, setLedger] = useState<CashBoxTransactionsResult | null>(null)
  const [ledgerLoading, setLedgerLoading] = useState(true)
  const [ledgerError, setLedgerError] = useState<string | null>(null)
  const [ledgerReload, setLedgerReload] = useState(0)
  const [selectedRows, setSelectedRows] = useState<Set<string>>(new Set())
  const [cancelRow, setCancelRow] = useState<CashBoxTransactionRow | null>(null)
  const [cancelBusy, setCancelBusy] = useState(false)
  const [cancelError, setCancelError] = useState<string | null>(null)

  useEffect(() => {
    if (!allowed) return
    const controller = new AbortController()
    // Har bir qidiruv/filtr o'zgarishida 300 ms kutamiz — kassir tez yozadi.
    const timer = setTimeout(() => {
      setLedgerLoading(true)
      setLedgerError(null)
      getCashBoxTransactions(
        { from, to, boxId: boxFilter || undefined, q: q.trim() || undefined },
        controller.signal,
      )
        .then((res) => {
          setLedger(res)
          setSelectedRows(new Set())
          setLedgerLoading(false)
        })
        .catch((err: unknown) => {
          if (isAborted(err)) return
          setLedgerError(financeErrorMessage(err, "Tranzaksiyalarni yuklab bo'lmadi."))
          setLedgerLoading(false)
        })
    }, 300)

    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [allowed, from, to, boxFilter, q, ledgerReload])

  /* ---- Sinflar ro'yxati — "Sinf" filtri uchun, bir marta yuklanadi ---- */
  useEffect(() => {
    if (!allowed) return
    let alive = true
    getClasses()
      .then((rows) => {
        if (alive) setClassList(rows)
      })
      .catch(() => undefined)
    return () => {
      alive = false
    }
  }, [allowed])

  /* ---- "O'quvchi" filtri — nom bo'yicha qidiruv, natijada shartnoma
     raqami(lari) keladi (`searchStudentContracts`, mavjud endpoint). ---- */
  useEffect(() => {
    const term = studentQuery.trim()
    if (term.length < MIN_SEARCH_LENGTH) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- qidiruv so'zi qisqarganda natijalar ro'yxatini darrov tozalash (StudentSearch'dagi bilan bir xil naqsh)
      setStudentResults([])
      return
    }
    const controller = new AbortController()
    const timer = setTimeout(() => {
      searchStudentContracts({ search: term, pageSize: 20 })
        .then((page) => {
          setStudentResults(page.items)
        })
        .catch(() => undefined)
    }, 300)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [studentQuery])

  /* ---- "Sinf" filtri — tanlangan sinfdagi barcha o'quvchilarning shartnoma
     raqamlarini yig'ib oladi (jadval qatorida sinf nomi YO'Q — faqat shu
     yo'l bilan bog'lanadi). ---- */
  useEffect(() => {
    if (!classFilter) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- sinf filtri tozalanganda shartnoma raqamlari to'plamini darrov bo'shatish
      setClassFilterNumbers(null)
      return
    }
    let alive = true
    setClassFilterLoading(true)
    searchStudentContracts({ className: classFilter, pageSize: 1000 })
      .then((page) => {
        if (!alive) return
        setClassFilterNumbers(new Set(page.items.map((c) => c.number).filter((n): n is string => !!n)))
      })
      .catch(() => {
        if (alive) setClassFilterNumbers(new Set())
      })
      .finally(() => {
        if (alive) setClassFilterLoading(false)
      })
    return () => {
      alive = false
    }
  }, [classFilter])

  /**
   * Ko'rsatilayotgan qatorlar — server javobi (`ledger.rows`, davr/kassa/`q`
   * bo'yicha) ustiga TO'RTTA qo'shimcha filtrni (usul, turi, o'quvchi, sinf)
   * MIJOZ TOMONDA qo'llaydi (sabab — fayl boshidagi izoh).
   */
  const filteredRows = useMemo(() => {
    const rows = ledger?.rows ?? []
    return rows.filter((r) => {
      if (methodFilter && r.method !== methodFilter) return false
      if (kindFilter && r.kind !== kindFilter) return false
      if (studentFilter && !(r.contractNo && studentFilter.numbers.has(r.contractNo))) return false
      if (classFilter && !(r.contractNo && classFilterNumbers?.has(r.contractNo))) return false
      return true
    })
  }, [ledger, methodFilter, kindFilter, studentFilter, classFilter, classFilterNumbers])

  /** Kartochkalardagi yig'indi davr/kassa bo'yicha — shu to'rttasi ishga tushsa, izoh ko'rsatiladi. */
  const rowsNarrowed = methodFilter !== '' || kindFilter !== '' || studentFilter !== null || classFilter !== ''

  const resetExtraFilters = () => {
    setMethodFilter('')
    setKindFilter('')
    setStudentFilter(null)
    setStudentQuery('')
    setClassFilter('')
  }

  const toggleSelectRow = (id: string) => {
    setSelectedRows((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const toggleSelectAll = () => {
    const rows = filteredRows
    setSelectedRows((prev) => {
      const allSelected = rows.length > 0 && rows.every((r) => prev.has(r.id))
      return allSelected ? new Set() : new Set(rows.map((r) => r.id))
    })
  }

  const handleActionDone = () => {
    setActionState(null)
    void loadBoxes()
    setLedgerReload((n) => n + 1)
  }

  /** Oddiy kirim (Kirim panelining "Oddiy kirim" tabi) yozilgach. */
  const handleIncomeDone = () => {
    setIncomePanel(null)
    void loadBoxes()
    setLedgerReload((n) => n + 1)
  }

  const handleBoxSaved = () => {
    setBoxFormOpen(false)
    setEditingBox(null)
    void loadBoxes()
  }

  const submitCancel = async (reason: string) => {
    if (!cancelRow || cancelBusy) return
    setCancelBusy(true)
    setCancelError(null)
    try {
      await cancelCashBoxTransaction(cancelRow.id, reason)
      setCancelRow(null)
      void loadBoxes()
      setLedgerReload((n) => n + 1)
    } catch (err) {
      setCancelError(financeErrorMessage(err, "Bekor qilib bo'lmadi."))
    } finally {
      setCancelBusy(false)
    }
  }

  const handleExport = () => {
    // Ekranda TURGAN qatorlar eksport qilinadi — filtrlangan ro'yxat, ekrandagi bilan bir xil.
    const rows = filteredRows
    const source = selectedRows.size > 0 ? rows.filter((r) => selectedRows.has(r.id)) : rows
    exportToCsv(
      `kassa-tranzaksiyalari-${from}_${to}.csv`,
      ['№', 'Sana', 'Kim', 'Shartnoma raqami', 'Miqdor', 'Tranzaksiya', "To'lov usuli", 'Holati', 'Kassir'],
      source.map((r) => [
        String(r.no),
        formatDateTime(r.date),
        r.who,
        r.contractNo ?? '',
        String(r.amount),
        kindLabel(r.kind),
        methodLabels[r.method] ?? r.method,
        statusLabel(r.status),
        r.who,
      ]),
    )
  }

  /* ---- Tanlangan o'quvchi va to'lov shakli (o'zgarmagan yo'l) ---- */
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
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Kassa</h1>
          <p className="text-sm text-slate-400">
            Kassalar, ularning harakati va o'quvchi to'lovlari.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <input
            type="date"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
            aria-label="Davr boshi"
            className={dateInputClass}
          />
          <span className="text-slate-400">—</span>
          <input
            type="date"
            value={to}
            onChange={(e) => setTo(e.target.value)}
            aria-label="Davr oxiri"
            className={dateInputClass}
          />
          <Button variant="secondary" onClick={() => setFiltersOpen((v) => !v)}>
            <Filter className="h-4 w-4" /> Filtr
          </Button>
          <Button variant="secondary" onClick={handleExport} disabled={filteredRows.length === 0}>
            <Download className="h-4 w-4" /> Export
          </Button>
        </div>
      </header>

      {filtersOpen && (
        <Card className="flex flex-col gap-3 p-4">
          <div className="flex flex-wrap items-end gap-3">
            <Select
              label="Kassa bo'yicha"
              value={boxFilter}
              onChange={(e) => setBoxFilter(e.target.value)}
              className="max-w-xs"
            >
              <option value="">Barcha kassalar</option>
              {boxes.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </Select>

            {/* O'quvchi — nomi bo'yicha qidiruv, tanlansa shartnoma raqami(lari) bilan filtrlaydi. */}
            <div className="relative w-48">
              <label className="mb-1 block text-sm font-medium text-slate-600">O'quvchi</label>
              <input
                value={studentFilter ? studentFilter.label : studentQuery}
                onChange={(e) => {
                  setStudentFilter(null)
                  setStudentQuery(e.target.value)
                  setStudentSearchOpen(true)
                }}
                onFocus={() => setStudentSearchOpen(true)}
                onBlur={() => setTimeout(() => setStudentSearchOpen(false), 150)}
                placeholder="Ism bo'yicha qidirish"
                autoComplete="off"
                className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
              />
              {studentSearchOpen && studentQuery.trim().length >= MIN_SEARCH_LENGTH && !studentFilter && (
                <ul className="absolute z-20 mt-1 max-h-64 w-72 overflow-y-auto rounded-lg border border-slate-200 bg-white p-1 shadow-lg">
                  {studentResults.length === 0 && (
                    <li className="px-3 py-2 text-sm text-slate-400">Topilmadi</li>
                  )}
                  {studentResults.map((c) => (
                    <li key={c.id}>
                      <button
                        type="button"
                        onMouseDown={(e) => e.preventDefault()}
                        onClick={() => {
                          setStudentFilter({ label: c.studentName, numbers: new Set(c.number ? [c.number] : []) })
                          setStudentSearchOpen(false)
                        }}
                        className="w-full rounded-lg px-3 py-1.5 text-left text-sm hover:bg-slate-50"
                      >
                        <span className="block font-medium text-slate-800">{c.studentName}</span>
                        <span className="block text-xs text-slate-400">
                          {c.className}
                          {c.number ? ` · ${c.number}` : ' · shartnomasiz'}
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </div>

            <Select
              label="Sinf"
              value={classFilter}
              onChange={(e) => setClassFilter(e.target.value)}
              className="max-w-[10rem]"
            >
              <option value="">Barcha sinflar</option>
              {classList.map((c) => (
                <option key={c.id} value={c.name}>
                  {c.name}
                </option>
              ))}
            </Select>

            <Select
              label="To'lov usuli"
              value={methodFilter}
              onChange={(e) => setMethodFilter(e.target.value as PaymentMethod | '')}
              className="max-w-[10rem]"
            >
              <option value="">Barchasi</option>
              {METHODS.map((m) => (
                <option key={m} value={m}>
                  {methodLabels[m]}
                </option>
              ))}
            </Select>

            <Select
              label="Tranzaksiya turi"
              value={kindFilter}
              onChange={(e) => setKindFilter(e.target.value as CashBoxTransactionRow['kind'] | '')}
              className="max-w-[10rem]"
            >
              <option value="">Barchasi</option>
              {TRANSACTION_KINDS.map((k) => (
                <option key={k} value={k}>
                  {kindLabel(k)}
                </option>
              ))}
            </Select>

            {(methodFilter || kindFilter || studentFilter || classFilter) && (
              <button
                type="button"
                onClick={resetExtraFilters}
                className="mb-0.5 text-sm font-medium text-brand-600 hover:underline"
              >
                Filtrni tozalash
              </button>
            )}
          </div>

          {classFilterLoading && <p className="text-xs text-slate-400">Sinf bo'yicha yuklanmoqda...</p>}
        </Card>
      )}

      {/* ============ KASSALAR ============ */}
      {boxesError && (
        <Card className="border-red-200 bg-red-50/60">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex items-start gap-2 text-sm text-red-700">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <span>{boxesError}</span>
            </div>
            <Button variant="secondary" onClick={() => void loadBoxes()}>
              <RefreshCw className="h-4 w-4" /> Qayta urinish
            </Button>
          </div>
        </Card>
      )}

      {boxesLoading ? (
        <Loader label="Kassalar yuklanmoqda..." />
      ) : (
        <div className="grid gap-6 lg:grid-cols-[minmax(300px,360px)_1fr]">
          <CashBoxPanel
            boxes={boxes}
            selectedId={selectedBoxId}
            canManage={canManageBoxes}
            onSelect={setSelectedBoxId}
            onAdd={() => {
              setEditingBox(null)
              setBoxFormOpen(true)
            }}
            onEdit={(box) => {
              setEditingBox(box)
              setBoxFormOpen(true)
            }}
            onAction={(box, mode) => {
              if (mode === 'in') {
                // Kirim — modal emas, pastdagi panel (izoh: `incomePanel` e'loni).
                setIncomePanel(box)
                setIncomeTab('plain')
              } else {
                setActionState({ box, mode })
              }
            }}
          />

          <CashLedger
            totalsByMethod={ledger?.totalsByMethod ?? {}}
            inTotal={ledger?.inTotal ?? 0}
            outTotal={ledger?.outTotal ?? 0}
            rows={filteredRows}
            narrowed={rowsNarrowed}
            loading={ledgerLoading}
            error={ledgerError}
            onRetry={() => setLedgerReload((n) => n + 1)}
            q={q}
            onQChange={setQ}
            selected={selectedRows}
            onToggleSelect={toggleSelectRow}
            onToggleSelectAll={toggleSelectAll}
            onCancelRow={setCancelRow}
          />
        </div>
      )}

      {/* ============ KIRIM PANELI ============
          Ikki tab: "Oddiy kirim" (cashBoxIn) va "O'quvchidan to'lov" (eski
          yo'l, TEGILMAGAN — quyidagi bo'lim ilgarigi "O'QUVCHIDAN TO'LOV"
          bo'limi bilan AYNAN bir xil, faqat shart o'zgargan). */}
      {incomePanel && (
      <section className="space-y-4">
        <Card>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <h2 className="font-semibold text-slate-800">Kirim — {incomePanel.name}</h2>
              <p className="text-sm text-slate-400">Oddiy kirim yozing yoki o'quvchidan to'lov qabul qiling.</p>
            </div>
            <button
              type="button"
              onClick={() => setIncomePanel(null)}
              title="Yopish"
              aria-label="Kirim panelini yopish"
              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
            >
              <X className="h-4 w-4" />
            </button>
          </div>
          <div className="mt-4 inline-flex rounded-lg border border-slate-200 p-1">
            <button
              type="button"
              onClick={() => setIncomeTab('plain')}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                incomeTab === 'plain' ? 'bg-brand-600 text-white' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              Oddiy kirim
            </button>
            <button
              type="button"
              onClick={() => setIncomeTab('student')}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                incomeTab === 'student' ? 'bg-brand-600 text-white' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              O'quvchidan to'lov
            </button>
          </div>
        </Card>

        {incomeTab === 'plain' && (
          <PlainIncomeForm box={incomePanel} onDone={handleIncomeDone} onCancel={() => setIncomePanel(null)} />
        )}

        {incomeTab === 'student' && (
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
      </section>
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
          }}
        />
      )}

      {boxFormOpen && (
        <CashBoxFormModal
          box={editingBox}
          onClose={() => {
            setBoxFormOpen(false)
            setEditingBox(null)
          }}
          onSaved={handleBoxSaved}
        />
      )}

      {actionState && (
        <CashBoxActionModal
          box={actionState.box}
          otherBoxes={boxes.filter((b) => b.id !== actionState.box.id && b.isActive)}
          mode={actionState.mode}
          onClose={() => setActionState(null)}
          onDone={handleActionDone}
        />
      )}

      <ReasonModal
        open={cancelRow !== null}
        title="Tranzaksiyani bekor qilish"
        description={
          cancelRow
            ? `№${cancelRow.no} — ${formatSum(cancelRow.amount)} so'm. Yozuv o'chmaydi, holati "bekor qilindi" bo'ladi.`
            : ''
        }
        confirmLabel="Bekor qilish"
        busy={cancelBusy}
        error={cancelError}
        onClose={() => {
          setCancelRow(null)
          setCancelError(null)
        }}
        onConfirm={submitCancel}
      />
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

/* ==========================================================================
   Kirim panelining "Oddiy kirim" tabi
   ========================================================================== */

interface PlainIncomeFormProps {
  box: CashBox
  onDone: () => void
  onCancel: () => void
}

/**
 * `CashBoxActionModal`dagi "Kirim" (mode `in`) shakli bilan AYNAN BIR XIL
 * maydonlar va chaqiruv (`cashBoxIn`) — faqat `Modal` ichida emas, sahifa
 * ichidagi oddiy `Card`da, chunki bu yerda ikkinchi tab (`PaymentSplitModal`
 * ochadigan "O'quvchidan to'lov") bilan bitta panelni bo'lishadi (izoh:
 * `incomePanel` e'loni, `CashierPage` boshida).
 */
function PlainIncomeForm({ box, onDone, onCancel }: PlainIncomeFormProps) {
  const [amountRaw, setAmountRaw] = useState('')
  const [method, setMethod] = useState<PaymentMethod>('cash')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const amount = parseSum(amountRaw)
  const amountValid = amount !== null && amount > 0

  const submit = async () => {
    if (!amountValid || amount === null || busy) return
    setBusy(true)
    setError(null)
    try {
      await cashBoxIn(box.id, { amount, method, note: note.trim() || undefined })
      onDone()
    } catch (err) {
      setError(financeErrorMessage(err, "Kirimni yozib bo'lmadi."))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <div className="grid gap-4 sm:grid-cols-2">
        <MoneyInput
          label="Summa (so'm)"
          value={amountRaw}
          onValueChange={setAmountRaw}
          placeholder="0"
          invalid={amountRaw.length > 0 && !amountValid}
          hint={amountRaw.length > 0 && !amountValid ? "Summa noldan katta bo'lishi kerak." : undefined}
          autoFocus
        />
        <Select label="To'lov usuli" value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
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
          placeholder="Masalan: boshlang'ich mablag'"
        />
      </div>

      <p className="mt-3 text-xs text-slate-400">Joriy qoldiq: {formatSum(box.balance)} so'm</p>

      {error && (
        <div className="mt-3 rounded-xl border border-red-200 bg-red-50/70 px-3 py-3">
          <div className="flex items-start gap-2 text-sm text-red-700">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{error}</span>
          </div>
        </div>
      )}

      <div className="mt-4 flex items-center justify-end gap-2">
        <Button variant="secondary" onClick={onCancel} disabled={busy}>
          Bekor qilish
        </Button>
        <Button onClick={() => void submit()} disabled={!amountValid || busy}>
          {busy ? 'Yozilmoqda...' : 'Kirim'}
        </Button>
      </div>
    </Card>
  )
}
