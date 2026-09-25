import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import {
  AlertTriangle,
  Download,
  Filter,
  Inbox,
  RefreshCw,
  Search,
  ShieldOff,
  Wallet,
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
import type { TransactionType } from '@/api/services/transactionTypes'
import { getTransactionTypes } from '@/api/services/transactionTypes'
import type { StudentContract } from '@/api/services/studentContracts'
import { searchStudentContracts } from '@/api/services/studentContracts'
import { getClasses } from '@/api/services/classes'
import { useAuth } from '@/context/auth-context'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Modal } from '@/components/ui/Modal'
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
import { CashTransactionReceipt } from './CashTransactionReceipt'
import { canUseCashDesk } from './cashDeskRoles'
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
import { DatePicker } from '@/components/ui/DatePicker'
import { formatPhone } from '@/lib/phone'

/** Kassa harakati turlari — "Tranzaksiya turi" filtri shu ro'yxatdan (format.ts dagi kindLabel bilan bir xil to'rttasi). */
const TRANSACTION_KINDS: CashBoxTransactionRow['kind'][] = [
  'pay_in',
  'pay_out',
  'transfer',
  'exchange',
  // 2026-09-22: jurnal endi to'lov, xarajat va qaytarimni ham ko'rsatadi.
  'student_payment',
  'expense',
  'refund',
]

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


/**
 * Kassa qo'shish/tahrirlash — faqat boshqaruvchi. Kirim/chiqim/ko'chirish/
 * ayirboshlash kassirga ham ochiq: bular kassirning kundalik ishi.
 */
const CASH_BOX_MANAGE_ROLES: Role[] = ['admin', 'superadmin']

const METHODS: PaymentMethod[] = ['cash', 'card', 'transfer', 'online']

/**
 * "O'quvchi oylik to'lovi" — seed qilingan tranzaksiya turi
 * (`transaction_types_seed.sql`, barqaror UUID). Kirim shaklida AYNAN SHU
 * tur tanlanganda o'quvchi qidiruvi ochiladi va pul hisob-fakturalarga
 * taqsimlanadi (mijoz, 2026-09-18: "to'lov turi tanlanganda ... o'quvchi
 * oylik to'lovi tanlanganda keyin o'quvchi qidirish joyi chiqishi kerak").
 *
 * Katalogda "bu tur o'quvchiga bog'lanadi" degan BAYROQ yo'q, shuning uchun
 * bog'lanish ikki yo'l bilan topiladi: seed ID (admin nomini o'zgartirsa ham
 * ishlaydi) yoki nomida "o'quvchi" so'zi bo'lgan admin qo'shgan tur
 * ({@link isStudentTuitionType}).
 */
const STUDENT_TUITION_TYPE_ID = '00000000-0000-0000-0000-0000000000a4'

/** Apostrof shakllari har xil yoziladi ('/’/`) — solishtirishdan oldin tenglashtiriladi. */
const normalizeTypeName = (name: string) =>
  name.toLowerCase().replace(/[\u2018\u2019\u02bc`\u00b4]/g, "'")

const isStudentTuitionType = (type: TransactionType | undefined) =>
  type !== undefined &&
  (type.id === STUDENT_TUITION_TYPE_ID || normalizeTypeName(type.name).includes("o'quvchi"))


const todayStr = () => new Date().toISOString().slice(0, 10)

/**
 * Kirim shaklida tanlash mumkin bo'lgan ENG ESKI sana — bir yil orqaga.
 * Serverdagi chegara bilan bir xil (`CashBoxService.MaxBackdateDays` = 366):
 * u yerdagi qoida — "2026" o'rniga "2025" terib, pulni yopilgan yilga
 * yuborib yubormaslik. Shakl ham shu oraliqdan tashqarisini yubormaydi.
 */
const MIN_ENTRY_DATE = () => {
  const d = new Date()
  d.setDate(d.getDate() - 366)
  return d.toISOString().slice(0, 10)
}

/**
 * Kassir ish o'rni (P1-16 → kassalarga o'tish, 2026-09-18; EduSchool
 * skrinshotiga moslash va Kirim panelining ikkinchi tabi, 2026-09-18 kech).
 *
 * Ikki bo'lim bor:
 *   1) KASSALAR — bir nechta kassa, har birining qoldig'i, kirim/chiqim/
 *      ko'chirish/ayirboshlash va umumiy tranzaksiyalar jadvali (filtr
 *      qatori + ustunlar sozlamasi bilan).
 *   2) KIRIM MODALI — tanlangan kassaning "Kirim" tugmasi ochadi. BITTA
 *      UMUMIY SHAKL (mijoz, 2026-09-18): tab yo'q, o'quvchi esa ixtiyoriy
 *      maydon. Tanlanmasa — oddiy kassa kirimi (`cashBoxIn`); tanlansa —
 *      eski yo'l, O'ZGARTIRILMAGAN: hisob-fakturaga taqsimlanadigan to'lov
 *      ilgarigidek `acceptPayment` (`/cash/payments`) orqali ketadi. Bu
 *      FIFO taqsimotni bilmagan umumiy "Kirim" amalidan TUBDAN farq qiladi
 *      — shuning uchun ikki server yo'li ham bor va bir-biriga
 *      aylantirilmagan, faqat ularga kiradigan ekran bitta.
 */
export function CashierPage() {
  const { user } = useAuth()
  const allowed = canUseCashDesk(user)
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
    // eslint-disable-next-line react-hooks/set-state-in-effect -- ma'lumotni qayta yuklaymiz, yangi so'rovdan oldin "yuklanmoqda" holati (maqsadli)
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
     KIRIM — tanlangan kassaning "Kirim" tugmasi ochadigan modal. EduSchool'da
     o'quvchi to'lovi ham kassaning o'z Kirim amali orqali kiritiladi
     (`finance-parity.md` §2.1); mijoz ham shuni so'radi (2026-09-18):
     "oddiy kirim va o'quvchi to'lovi degan narsalarni olib tashla va bitta
     umumiy bo'lsin". Shuning uchun tab YO'Q — bitta shakl, ichida ixtiyoriy
     "O'quvchi" maydoni bor (`IncomeForm`, fayl oxirida):
       · o'quvchisiz → `cashBoxIn`;
       · o'quvchi bilan → StudentSearch → hisob-fakturalar →
         PaymentSplitModal → ReceiptPreview (eski yo'l, TEGILMAGAN). */
  const [incomePanel, setIncomePanel] = useState<CashBox | null>(null)

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

  /* "Tranzaksiya turi" — jadvaldagi AYNAN shu nomli ustun (Do'ppi uchun,
     Kanselyariya xarajati, ...). Ilgari bu yerda faqat KIND (Kirim/Chiqim/
     Ko'chirish/Ayirboshlash) filtri bor edi va u xato ravishda "Tranzaksiya
     turi" deb nomlangandi — endi kind filtri "Tranzaksiya" (ustun nomi bilan
     bir xil), tur esa o'z filtriga ega. Qator turning NOMINI olib keladi
     (`transactionTypeName`, id emas), shuning uchun tanlov ham nom bo'yicha. */
  const [typeNameFilter, setTypeNameFilter] = useState('')
  const [typeOptions, setTypeOptions] = useState<TransactionType[]>([])

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
  /** Chek oynasi — jadvaldagi printer tugmasi ochadi (`CashTransactionReceipt`). */
  const [receiptRow, setReceiptRow] = useState<CashBoxTransactionRow | null>(null)
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

  /* ---- Tranzaksiya turlari — filtr ro'yxati uchun, bir marta yuklanadi ---- */
  useEffect(() => {
    if (!allowed) return
    let alive = true
    getTransactionTypes()
      .then((rows) => {
        if (alive) setTypeOptions(rows)
      })
      .catch(() => undefined)
    return () => {
      alive = false
    }
  }, [allowed])

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
      if (typeNameFilter && r.transactionTypeName !== typeNameFilter) return false
      if (studentFilter && !(r.contractNo && studentFilter.numbers.has(r.contractNo))) return false
      if (classFilter && !(r.contractNo && classFilterNumbers?.has(r.contractNo))) return false
      return true
    })
  }, [ledger, methodFilter, kindFilter, typeNameFilter, studentFilter, classFilter, classFilterNumbers])

  /** Kartochkalardagi yig'indi davr/kassa bo'yicha — shu to'rttasi ishga tushsa, izoh ko'rsatiladi. */
  const rowsNarrowed =
    methodFilter !== '' ||
    kindFilter !== '' ||
    typeNameFilter !== '' ||
    studentFilter !== null ||
    classFilter !== ''

  const resetExtraFilters = () => {
    setMethodFilter('')
    setKindFilter('')
    setTypeNameFilter('')
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

  /** Oddiy kassa kirimi (o'quvchisiz yo'l) yozilgach. */
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
      [
        '№', 'Sana', 'Kim', 'Shartnoma raqami', 'Miqdor', 'Tranzaksiya',
        'Tranzaksiya turi', "To'lov usuli", 'Holati', 'Kassir', 'Izoh', 'Sabab',
      ],
      source.map((r) => [
        String(r.no),
        formatDateTime(r.date),
        r.who,
        r.contractNo ?? '',
        String(r.amount),
        kindLabel(r.kind),
        r.transactionTypeName ?? '',
        methodLabels[r.method] ?? r.method,
        statusLabel(r.status),
        r.who,
        r.note ?? '',
        r.cancelReason ?? '',
      ]),
    )
  }

  /* ---- Tanlangan o'quvchi va taqsimotga uzatiladigan to'lov ----
     Shaklning O'ZI (summa, usul, izoh) `IncomeForm` ichida — bu yerda faqat
     TAQSIMOT oynasiga uzatiladigan nusxa turadi: o'quvchi tanlangan holda
     "Kirim" bosilsa shakl shu `splitDraft`ni to'ldiradi va
     `PaymentSplitModal` ochiladi. */
  const [student, setStudent] = useState<CashierStudent | null>(null)
  const [splitDraft, setSplitDraft] = useState<{
    amount: number
    method: PaymentMethod
    note: string
    receivedOn: string
    /** Kirim QAYSI kassada bosilgan — to'lov o'sha kassaga yoziladi (mijoz, 2026-09-22). */
    cashBoxId: string
  } | null>(null)
  const [receipt, setReceipt] = useState<Payment | null>(null)

  /* ---- Tanlangan o'quvchining ochiq hisob-fakturalari ---- */
  const [invoices, setInvoices] = useState<AllocationSuggestion[]>([])
  const [invoicesLoading, setInvoicesLoading] = useState(false)
  const [invoicesError, setInvoicesError] = useState<string | null>(null)
  const [invoicesReload, setInvoicesReload] = useState(0)

  useEffect(() => {
    if (!student) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- tanlov bo'shaganda eski natijani tozalaymiz (maqsadli)
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
          <DatePicker
            value={from}
            onChange={(value: string) => setFrom(value)}
            ariaLabel="Davr boshi"
            className="w-40"
          />
          <span className="text-slate-400">—</span>
          <DatePicker
            value={to}
            onChange={(value: string) => setTo(value)}
            ariaLabel="Davr oxiri"
            className="w-40"
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
              label="Tranzaksiya"
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

            {/* Jadvaldagi "TRANZAKSIYA TURI" ustuni bo'yicha filtr — katalogdan
                (`transaction_types`), kirim va chiqim turlari birga. */}
            <Select
              label="Tranzaksiya turi"
              value={typeNameFilter}
              onChange={(e) => setTypeNameFilter(e.target.value)}
              className="max-w-[12rem]"
            >
              <option value="">Barchasi</option>
              {typeOptions.map((t) => (
                <option key={t.id} value={t.name}>
                  {t.name}
                </option>
              ))}
            </Select>

            {rowsNarrowed && (
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
        <div className="grid gap-6 lg:grid-cols-[minmax(360px,420px)_1fr]">
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
                // Kirim — bitta umumiy shakl (izoh: `incomePanel` e'loni).
                setIncomePanel(box)
              } else {
                setActionState({ box, mode })
              }
            }}
          />

          <CashLedger
            totalsByMethod={ledger?.totalsByMethod ?? {}}
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
            onReceiptRow={setReceiptRow}
          />
        </div>
      )}

      {/* ============ KIRIM MODALI ============
          BITTA UMUMIY SHAKL (mijoz, 2026-09-18): "oddiy kirim va o'quvchi
          to'lovi degan narsalarni olib tashla va bitta umumiy bo'lsin".
          Ilgari ikkita tab bor edi; endi farq TABDA emas, MAYDONDA:
          o'quvchi tanlansa to'lov hisob-fakturalarga taqsimlanadi
          (`acceptPayment` → `PaymentSplitModal`), tanlanmasa oddiy kassa
          kirimi bo'ladi (`cashBoxIn`). Ikki server yo'li ham
          O'ZGARTIRILMAGAN — faqat ularga kiradigan eshik bitta. */}
      <Modal
        open={incomePanel !== null}
        onClose={() => {
          setIncomePanel(null)
          setStudent(null)
        }}
        title={incomePanel ? `Kirim — ${incomePanel.name}` : 'Kirim'}
        // Maydonlar ustma-ust turadi, shuning uchun bitta tor o'lcham yetadi.
        size="md"
      >
        {incomePanel && (
          <IncomeForm
            box={incomePanel}
            student={student}
            onSelectStudent={setStudent}
            onClearStudent={() => setStudent(null)}
            invoices={invoices}
            invoicesLoading={invoicesLoading}
            invoicesError={invoicesError}
            onInvoicesRetry={() => setInvoicesReload((n) => n + 1)}
            debt={debt}
            onStudentSubmit={setSplitDraft}
            onDone={handleIncomeDone}
            onCancel={() => {
              setIncomePanel(null)
              setStudent(null)
            }}
          />
        )}
      </Modal>

      {student && splitDraft && (
        <PaymentSplitModal
          open
          student={student}
          amount={splitDraft.amount}
          method={splitDraft.method}
          note={splitDraft.note}
          receivedOn={splitDraft.receivedOn}
          cashBoxId={splitDraft.cashBoxId}
          onClose={() => setSplitDraft(null)}
          onAccepted={(payment) => {
            setSplitDraft(null)
            setReceipt(payment)
          }}
        />
      )}

      {receipt && (
        <ReceiptPreview
          open
          payment={receipt}
          onClose={() => {
            // To'lov yozildi — Kirim oynasi yopiladi va ekran yangilanadi:
            // ochiq qolsa eski o'quvchi va eski summa bilan turib qolardi.
            setReceipt(null)
            setIncomePanel(null)
            setStudent(null)
            setInvoicesReload((n) => n + 1)
            void loadBoxes()
            setLedgerReload((n) => n + 1)
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

      {receiptRow && (
        <CashTransactionReceipt
          row={receiptRow}
          // Jadval qatorida kassa nomi yo'q — filtrlangan kassa (yoki
          // tanlangan kassa) nomini chekka o'zimiz beramiz.
          boxName={
            boxes.find((b) => b.id === (boxFilter || selectedBoxId))?.name ?? ''
          }
          onClose={() => setReceiptRow(null)}
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
  /**
   * `true` — `Card` o'ramisiz chiziladi. Kirim shaklida qidiruv shaklning
   * O'Z kartochkasi ichida, tranzaksiya turidan keyingi oddiy maydon sifatida
   * turadi (mijoz, 2026-09-18: "alohida qismda emas, tranzaksiya turini
   * pastida chiqishi kerak") — kartochka ichida yana kartochka bo'lmasin.
   */
  bare?: boolean
}

/**
 * Qidiruv paneli. To'rtta holat ham chizilgan: yo'l-yo'riq (juda qisqa
 * so'rov), yuklanmoqda, xato va bo'sh natija.
 *
 * Qidiruv 300 ms kechikish bilan yuboriladi va oldingi so'rov uziladi —
 * kassir tez yozadi, har harf uchun so'rov yuborish serverni ham, ro'yxatni
 * ham sakratadi.
 */
function StudentSearch({ selectedId, onSelect, bare = false }: StudentSearchProps) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<CashierStudent[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)

  const term = query.trim()
  const tooShort = term.length < MIN_SEARCH_LENGTH

  useEffect(() => {
    if (tooShort) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- tanlov bo'shaganda eski natijani tozalaymiz (maqsadli)
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

  const Shell = bare ? BareBlock : Card

  return (
    <Shell className="flex h-fit flex-col gap-3">
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
        <ul className="max-h-[22rem] divide-y divide-slate-100 overflow-y-auto">
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
    </Shell>
  )
}

/** `Card` o'rniga ishlatiladigan bo'sh o'ram — `StudentSearch bare` uchun. */
function BareBlock({ className, children }: { className?: string; children: ReactNode }) {
  return <div className={className}>{children}</div>
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
   Kirim shakli — BITTA UMUMIY (tab yo'q)
   ========================================================================== */

interface IncomeFormProps {
  box: CashBox
  /** Tanlangan o'quvchi — faqat "o'quvchi to'lovi" turida so'raladi. */
  student: CashierStudent | null
  onSelectStudent: (student: CashierStudent) => void
  onClearStudent: () => void
  invoices: AllocationSuggestion[]
  invoicesLoading: boolean
  invoicesError: string | null
  onInvoicesRetry: () => void
  debt: number
  /** O'quvchi to'lovi yuborilganda — taqsimot oynasiga uzatiladi. */
  onStudentSubmit: (draft: {
    amount: number
    method: PaymentMethod
    note: string
    receivedOn: string
    cashBoxId: string
  }) => void
  /** Oddiy kassa kirimi yozilgach. */
  onDone: () => void
  onCancel: () => void
  /** Turlar yuklanganda "O'quvchi oylik to'lovi" tanlansin (o'quvchi profilidan ochilganda). */
  preferStudentType?: boolean
  /** O'quvchini almashtirib bo'lmaydi — "O'zgartirish" tugmasi ko'rinmaydi. */
  lockStudent?: boolean
}

/**
 * Mijoz yuborgan EduSchool kassa kirim shaklidagi tartib bilan mos:
 * **Tranzaksiya turi (majburiy) · summa (+ to'lov usuli) · sana · izoh**.
 *
 * BITTA SHAKL, IKKI YO'L (mijoz, 2026-09-18): "oddiy kirim va o'quvchi
 * to'lovi degan narsalarni olib tashla va bitta umumiy bo'lsin" va "to'lov
 * turi tanlanganda ... o'quvchi oylik to'lovi tanlanganda keyin o'quvchi
 * qidirish joyi chiqishi kerak". Ya'ni yo'lni TAB emas, TRANZAKSIYA TURI
 * tanlaydi:
 *   · "O'quvchi oylik to'lovi" ({@link isStudentTuitionType}) → o'quvchi
 *     qidiruvi va uning ochiq hisob-fakturalari ochiladi, "Kirim"
 *     `PaymentSplitModal`ni ochadi va pul eski yo'l bilan yoziladi
 *     (`acceptPayment`, FIFO taqsimot serverda — TEGILMAGAN);
 *   · qolgan barcha turlar → oddiy kassa kirimi (`cashBoxIn`).
 * Ikki server yo'li bir-biriga aylantirilmagan — ular tubdan har xil, faqat
 * ularga kiradigan ekran bitta.
 *
 * TUR O'QUVCHI TO'LOVIDA JO'NATILMAYDI: `acceptPayment` so'rovida bunday
 * maydon yo'q (`cashier.ts`, `AcceptPaymentPayload`) — u yerda yozuvning
 * mazmuni hisob-faktura toifasidan keladi. Bu yerda tur — YO'LNI TANLAYDIGAN
 * savol; uni jo'natish uchun backend kontrakti kengaytirilishi kerak bo'lardi,
 * bu esa alohida ish (docs/ASSUMPTIONS.md, 2026-09-18).
 *
 * TO'LOV USULINI SAQLAB QOLDIK, ULARNING SHAKLIDA U YO'Q BO'LSA HAM:
 * Kassa ekranining har-usul kesimi (`CashLedger.tsx`, `Naqd`/`Klik`/
 * `Terminal`, ...) va jurnalning TO'LOV USULI ustuni shu maydondan keladi —
 * uni olib tashlash o'sha ikkalasini buzardi. Summa bilan bitta qatorda
 * turadi (avvalgidek): ular bir-biriga bog'liq savol ("qancha" + "qanday").
 *
 * SANA — TUZATILMAYDI, KO'RSATILADI: SPEC §4 orqaga sanani taqiqlaydi (bir
 * marta yopilgan hisobot davriga to'lov tushishi). Mijoz shaklida sana
 * TANLANADIGAN maydon, lekin backend `CreatedAt`ni har doim serverda,
 * HOZIR bilan belgilaydi (`CashBoxService.PayInAsync` — so'rov tanasida
 * sana maydoni UMUMAN yo'q) — shu yerda ham faqat bugungi sana O'QISH
 * uchun ko'rsatiladi, tahrirlanmaydi.
 *
 * TRANZAKSIYA TURI — MAJBURIY BU EKRANDA, BACKEND'DA IXTIYORIY: server
 * `CashBoxPayInRequest.TransactionTypeId`ni ixtiyoriy qabul qiladi, chunki
 * uni butun tizim darajasida majburiy qilish `CashBoxActionModal.tsx`ning
 * eski "in" yo'lini va o'nlab mavjud testni (`CashBoxTests.cs`) buzardi.
 * Bu FORMA — mijoz talab qilgan haqiqiy sirtki qatlam — uni majburiy qiladi
 * (pastga: `canSubmit`).
 */
export function IncomeForm({
  box,
  student,
  onSelectStudent,
  onClearStudent,
  invoices,
  invoicesLoading,
  invoicesError,
  onInvoicesRetry,
  debt,
  onStudentSubmit,
  onDone,
  onCancel,
  preferStudentType = false,
  lockStudent = false,
}: IncomeFormProps) {
  const [amountRaw, setAmountRaw] = useState('')
  const [method, setMethod] = useState<PaymentMethod>('cash')
  const [note, setNote] = useState('')
  const [date, setDate] = useState(todayStr())
  const [transactionTypeId, setTransactionTypeId] = useState('')
  const [types, setTypes] = useState<TransactionType[]>([])
  const [typesLoading, setTypesLoading] = useState(true)
  const [typesError, setTypesError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- katalogni bir marta yuklaymiz, yuklanish holati shu yerda boshlanadi (maqsadli)
    setTypesLoading(true)
    setTypesError(null)
    getTransactionTypes('in')
      .then((rows) => {
        if (!alive) return
        // Profildan ochilganda faqat o'quvchi to'lovi turlari: boshqa tur o'quvchisiz kassa kirimiga aylanardi.
        const active = rows.filter((t) => t.isActive && (!lockStudent || isStudentTuitionType(t)))
        setTypes(active)
        const preferred = preferStudentType ? active.find((t) => isStudentTuitionType(t)) : undefined
        setTransactionTypeId((current) => current || preferred?.id || active[0]?.id || '')
      })
      .catch((err: unknown) => {
        if (alive) setTypesError(financeErrorMessage(err, "Tranzaksiya turlarini yuklab bo'lmadi."))
      })
      .finally(() => {
        if (alive) setTypesLoading(false)
      })
    return () => {
      alive = false
    }
  }, [preferStudentType, lockStudent])

  const selectedType = types.find((t) => t.id === transactionTypeId)
  const studentMode = isStudentTuitionType(selectedType)

  /** Tur o'zgarganda o'quvchi yo'li yopilsa, tanlangan o'quvchi ham tushadi. */
  const changeType = (nextId: string) => {
    setTransactionTypeId(nextId)
    if (!isStudentTuitionType(types.find((t) => t.id === nextId))) onClearStudent()
  }

  const amount = parseSum(amountRaw)
  const amountValid = amount !== null && amount > 0
  // O'quvchi to'lovida o'quvchi majburiy — kimning qarziga tushishi noma'lum
  // bo'lsa, taqsimot oynasini ochib ham bo'lmaydi.
  // Sana — bugundan keyingi kun ham, bir yildan uzoq orqadagi kun ham
  // yuborilmaydi: server ikkalasini ham rad etadi (`CashBoxService.
  // MaxBackdateDays`), shuning uchun tanlash ham shu oraliqda.
  const today = todayStr()
  const earliest = MIN_ENTRY_DATE()
  const dateValid = date !== '' && date <= today && date >= earliest
  const canSubmit =
    amountValid && dateValid && transactionTypeId !== '' && (!studentMode || student !== null)

  const submit = async () => {
    if (!canSubmit || amount === null || busy) return

    // O'quvchi to'lovi — pul hisob-fakturalarga taqsimlanadi, yozuvni
    // taqsimot oynasi yozadi (`acceptPayment`).
    if (studentMode && student) {
      onStudentSubmit({ amount, method, note: note.trim(), receivedOn: date, cashBoxId: box.id })
      return
    }

    setBusy(true)
    setError(null)
    try {
      await cashBoxIn(box.id, {
        amount,
        method,
        note: note.trim() || undefined,
        transactionTypeId,
        date,
      })
      onDone()
    } catch (err) {
      setError(financeErrorMessage(err, "Kirimni yozib bo'lmadi."))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-4">
      <Card>
        <div>
          <Select
            label="Tranzaksiya turi"
            required
            autoFocus
            value={transactionTypeId}
            onChange={(e) => changeType(e.target.value)}
            disabled={typesLoading || types.length === 0}
          >
            {typesLoading && <option value="">Yuklanmoqda...</option>}
            {!typesLoading && types.length === 0 && <option value="">Turlar yo'q</option>}
            {!typesLoading &&
              types.length > 0 && [
                <option key="" value="" disabled>
                  Tanlang...
                </option>,
                ...types.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                )),
              ]}
          </Select>
          {typesError && <p className="mt-1 text-xs text-red-600">{typesError}</p>}
        </div>

        {/* --- O'quvchi: FAQAT o'quvchi to'lovi turida, va AYNAN tranzaksiya
            turining ostida (mijoz, 2026-09-18: "o'quvchi tanlash ... alohida
            qismda emas, balki tranzaksiya turini pastida chiqishi kerak") --- */}
        {studentMode && (
          <div className="mt-4">
            {student ? (
              <div className="rounded-xl border border-slate-200 bg-slate-50/70 px-3 py-2.5">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="min-w-0">
                    <p className="truncate font-medium text-slate-800">{student.fullName}</p>
                    <p className="truncate text-xs text-slate-500">
                      {student.className}
                      {student.parentFullName ? ` · ${student.parentFullName}` : ''}
                      {student.parentPhone ? ` · ${formatPhone(student.parentPhone)}` : ''}
                    </p>
                  </div>
                  <div className="ml-auto flex items-center gap-3">
                    {!invoicesLoading && !invoicesError && (
                      <div className="text-right">
                        <p className="text-[11px] uppercase tracking-wide text-slate-400">Jami qarz</p>
                        <p
                          className={cn(
                            'text-sm font-semibold tabular-nums',
                            debt > 0 ? 'text-red-600' : 'text-emerald-600',
                          )}
                        >
                          {formatSumWithUnit(debt)}
                        </p>
                      </div>
                    )}
                    {!lockStudent && (
                      <Button variant="ghost" type="button" onClick={onClearStudent} disabled={busy}>
                        O'zgartirish
                      </Button>
                    )}
                  </div>
                </div>
              </div>
            ) : (
              <StudentSearch bare selectedId={null} onSelect={onSelectStudent} />
            )}
          </div>
        )}

        <div className="mt-4 grid gap-4 sm:grid-cols-2">
          <MoneyInput
            label="Summa (so'm)"
            value={amountRaw}
            onValueChange={setAmountRaw}
            placeholder="0"
            invalid={amountRaw.length > 0 && !amountValid}
            hint={amountRaw.length > 0 && !amountValid ? "Summa noldan katta bo'lishi kerak." : undefined}
          />
          <Select label="To'lov usuli" value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
            {METHODS.map((m) => (
              <option key={m} value={m}>
                {methodLabels[m]}
              </option>
            ))}
          </Select>
        </div>

        {studentMode && student && debt > 0 && !invoicesLoading && !invoicesError && (
          <Button
            variant="ghost"
            className="mt-2"
            type="button"
            onClick={() => setAmountRaw(String(debt))}
          >
            Butun qarzni qo'yish ({formatSum(debt)})
          </Button>
        )}

        {/* SANA TANLANADI (mijoz, 2026-09-18): "oldingi sana uchun tanlash
            mumkin bo'lsin". Ilgari bu maydon qulflangan edi — SPEC §4 orqaga
            sanani taqiqlardi va backend `CreatedAt`ni har doim serverda
            belgilardi. Endi ikkalasi ham o'zgardi: so'rovda ixtiyoriy sana
            bor, server esa uni tekshiradi (kelajak yo'q, bir yildan uzoq
            orqaga yo'q). Kechagi pulni bugun kiritganda u KECHAGI kun
            hisobotiga tushadi. */}
        <div className="mt-4">
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-slate-600">Sana</span>
            <DatePicker
              value={date}
              max={today}
              min={earliest}
              onChange={setDate}
              invalid={!dateValid}
            />
          </label>
          <p className={cn('mt-1 text-xs', dateValid ? 'text-slate-400' : 'text-red-600')}>
            {dateValid
              ? "Sukut bo'yicha bugun — o'tgan kun bilan ham yozish mumkin."
              : "Sana bugundan keyin ham, bir yildan uzoq orqada ham bo'lmasligi kerak."}
          </p>
        </div>

        <div className="mt-4">
          <Textarea
            label="Izoh (ixtiyoriy)"
            rows={2}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder={studentMode ? "Masalan: sentabr uchun, otasi to'ladi" : "Masalan: boshlang'ich mablag'"}
          />
        </div>

        <p className="mt-3 text-xs text-slate-400">Joriy qoldiq: {formatSum(box.balance)} so'm</p>
      </Card>

      {studentMode && student && (
        <InvoiceList
          invoices={invoices}
          loading={invoicesLoading}
          error={invoicesError}
          onRetry={onInvoicesRetry}
        />
      )}

      {error && (
        <div className="rounded-xl border border-red-200 bg-red-50/70 px-3 py-3">
          <div className="flex items-start gap-2 text-sm text-red-700">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{error}</span>
          </div>
        </div>
      )}

      <div className="flex items-center justify-end gap-2">
        <Button variant="secondary" onClick={onCancel} disabled={busy}>
          Bekor qilish
        </Button>
        <Button onClick={() => void submit()} disabled={!canSubmit || busy}>
          {studentMode ? <Wallet className="h-4 w-4" /> : null}
          {busy ? 'Yozilmoqda...' : 'Kirim'}
        </Button>
      </div>
    </div>
  )
}
