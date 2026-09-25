/**
 * Moliya → Oyma-oy qarzdorlik (arrears pivot).
 *
 * Direktorning kundalik savoli — "kim, qaysi oydan beri to'lamayapti".
 * Qarzdorlar hisoboti (`DebtorsTab`) unga BITTA raqam bilan javob beradi;
 * bu jadval o'sha raqamni oylarga yoyadi, ya'ni qarz qaysi oyda boshlangani
 * va uzilib-uzilib to'langani ko'rinadi.
 *
 * <b>Raqamlarning hammasi SERVERDAN tayyor keladi</b> (`financeReports.ts`
 * dagi qoida): bu yerda na qarz hisoblanadi, na kataklar qo'shiladi.
 * Ekrandagi yagona arifmetika — qidiruv bilan qator yashirilganda yakunni
 * qayta yig'ish, va u ham serverning O'Z katagi ustida bajariladi.
 *
 * <b>Bo'sh katak va nol katak — boshqa-boshqa narsa.</b> Oyda hisob-faktura
 * bo'lmasa (o'quvchi hali kelmagan yoki ketgan) katak kulrang chiziqcha
 * bo'lib qoladi; hisoblanib to'liq to'langan oy esa yashil nol. Ikkovini
 * bitta ko'rinishga qo'shish maktabda bo'lmagan oyni "to'langan" qilib
 * ko'rsatardi.
 */
import { hasFinanceAccess } from '@/pages/admin/billing/access'
import { useCallback, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { FileSpreadsheet, Users, Wallet, CalendarRange, AlertTriangle } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import {
  downloadArrearsPivot,
  getArrearsPivot,
  type ArrearsCell,
  type ArrearsPivot,
  type ArrearsRow,
} from '@/api/services/financeReports'
import { getFeeCategories } from '@/api/services/billingCatalog'
import { getClasses } from '@/api/services/classes'
import { api } from '@/api/client'
import { useAuth } from '@/context/auth-context'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { StatCard } from '@/components/ui/StatCard'
import { cn, formatMoney } from '@/lib/utils'
import { formatMonth } from '@/config/constants'
import { ReportState } from './ReportState'
import { MonthPicker } from '@/components/ui/DatePicker'
import { formatPhone } from '@/lib/phone'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/** SPEC §4.3: moliya hisobotlari faqat admin va direktorga ochiq. */
// Moliya — admin/direktor yoki rolida "Moliya" ruxsati bor xodim (Boshqaruv → Rollar).

/** O'quv yili sentyabrda boshlanadi — server sukuti ham shunday. */
const ACADEMIC_YEAR_START_MONTH = 9

/** Joriy oy, "YYYY-MM". */
function currentMonth(): string {
  return new Date().toISOString().slice(0, 7)
}

/**
 * Joriy o'quv yilining sentyabri. Kalendar yili emas: yanvarda ochilgan
 * jadval sentyabr–dekabr qarzini tashlab ketmasligi kerak, aynan o'sha
 * oylar qarzdor bo'ladi.
 */
function academicYearStart(): string {
  const now = new Date()
  const year = now.getMonth() + 1 >= ACADEMIC_YEAR_START_MONTH ? now.getFullYear() : now.getFullYear() - 1
  return `${year}-09`
}

/** Bo'sh katak: shu oyda hisob-faktura bo'lmagan. */
const emptyCell: ArrearsCell = { amount: 0, paid: 0, toBePaid: 0 }

function addCells(a: ArrearsCell, b: ArrearsCell): ArrearsCell {
  return { amount: a.amount + b.amount, paid: a.paid + b.paid, toBePaid: a.toBePaid + b.toBePaid }
}

type SortBy = 'class' | 'debt'

export function ArrearsPage() {
  const { user } = useAuth()
  const allowed = hasFinanceAccess(user)

  const [fromMonth, setFromMonth] = useState(academicYearStart)
  const [toMonth, setToMonth] = useState(currentMonth)
  // F13.01 — ko'p tanlovli sinf (yagona `className` o'rniga).
  const [classNames, setClassNames] = useState<string[]>([])
  const [categoryId, setCategoryId] = useState('')
  // F13.06 — bitta o'quv guruhi (hozirgi a'zolari).
  const [groupId, setGroupId] = useState('')
  // F13.02 — bitta o'quvchi — bitta toifa uchun bitta qator.
  const [splitByCategory, setSplitByCategory] = useState(false)
  const [debtorsOnly, setDebtorsOnly] = useState(false)
  // Sukut bo'yicha YOQIQ: maktabdan ketgan o'quvchining qarzi ham qarz
  // (DebtorsTab bilan bir xil qoida — ikki ekran bir xil javob bersin).
  const [includeArchived, setIncludeArchived] = useState(true)
  const [search, setSearch] = useState('')
  const [sortBy, setSortBy] = useState<SortBy>('class')
  const [exporting, setExporting] = useState(false)

  const { data, loading, error, refetch } = useAsync<ArrearsPivot>(
    () =>
      getArrearsPivot({
        fromMonth,
        toMonth,
        classNames: classNames.length > 0 ? classNames : undefined,
        categoryId: categoryId || undefined,
        groupId: groupId || undefined,
        splitByCategory,
        debtorsOnly,
        includeArchived,
      }),
    [fromMonth, toMonth, classNames, categoryId, groupId, splitByCategory, debtorsOnly, includeArchived],
  )

  // Sinf va toifa ro'yxati jadvaldan EMAS, o'z manbasidan olinadi: chegaraga
  // urilgan so'rov bo'sh qaytsa ham filtr ishlayotgan bo'lishi kerak — aks
  // holda foydalanuvchi chegaradan chiqa olmay qolardi.
  const { data: classes } = useAsync(getClasses, [])
  const { data: categories } = useAsync(() => getFeeCategories(true), [])
  // F13.06 — guruh ro'yxati shu ekranga xos, kichik so'rov: alohida servis
  // fayl yaratish o'rniga (bo'lajak "guruhlar" mijozi bilan to'qnashmasin
  // deb) to'g'ridan-to'g'ri shu yerda o'qiladi.
  const { data: groups } = useAsync<{ id: string; name: string }[]>(async () => {
    const { data: rows } = await api.get<{ id: string; name: string; isArchived: boolean }[]>(
      '/admin/study-groups',
    )
    return rows.filter((g) => !g.isArchived)
  }, [])

  const exportFilters = useMemo(
    () => ({
      fromMonth,
      toMonth,
      classNames: classNames.length > 0 ? classNames : undefined,
      categoryId: categoryId || undefined,
      groupId: groupId || undefined,
      splitByCategory,
      debtorsOnly,
      includeArchived,
    }),
    [fromMonth, toMonth, classNames, categoryId, groupId, splitByCategory, debtorsOnly, includeArchived],
  )

  const handleDownload = useCallback(async () => {
    setExporting(true)
    try {
      await downloadArrearsPivot(exportFilters)
    } finally {
      setExporting(false)
    }
  }, [exportFilters])

  const months = useMemo(() => data?.months ?? [], [data])
  const rows = useMemo(() => data?.rows ?? [], [data])

  const visible = useMemo(() => {
    const q = search.trim().toLowerCase()
    const filtered = q ? rows.filter((r) => r.fullName.toLowerCase().includes(q)) : rows
    if (sortBy === 'debt') {
      return [...filtered].sort((a, b) => b.total.toBePaid - a.total.toBePaid)
    }
    return filtered
  }, [rows, search, sortBy])

  /**
   * Yakun qatori. Qidiruv hech narsani yashirmagan bo'lsa — SERVERNING
   * yakuni o'zgarishsiz ishlatiladi; yashirgan bo'lsa ko'rinadigan
   * qatorlardan qayta yig'iladi, chunki ekranda qo'shilmaydigan ikki raqam
   * turishi mumkin emas.
   */
  const footer = useMemo(() => {
    if (data === null) return { byMonth: {} as Record<string, ArrearsCell>, total: emptyCell }
    if (visible.length === rows.length) return { byMonth: data.footer, total: data.total }

    // Har oy uchun katak BOR, qiymati nol bo'lsa ham — server ham shunday
    // qaytaradi. Aks holda qidiruv yoqilganda yakun qatori boshqacha
    // ko'rinardi (bo'sh oy chiziqcha bo'lib qolardi).
    const byMonth: Record<string, ArrearsCell> = Object.fromEntries(
      months.map((m) => [m, emptyCell]),
    )
    let total = emptyCell
    for (const row of visible) {
      for (const month of months) {
        const cell = row.cells[month]
        if (cell === undefined) continue
        byMonth[month] = addCells(byMonth[month], cell)
      }
      total = addCells(total, row.total)
    }
    return { byMonth, total }
  }, [data, visible, rows.length, months])

  const debtorCount = useMemo(() => visible.filter((r) => r.total.toBePaid > 0).length, [visible])


  if (!allowed) {
    return (
      <Card>
        <p className="py-12 text-center text-slate-400">Bu bo'limga ruxsatingiz yo'q.</p>
      </Card>
    )
  }

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Oyma-oy qarzdorlik</h1>
        <p className="mt-0.5 text-sm text-slate-500">
          Har katakda — o'sha oyga hisoblangan summa va undan qolgan qarz. Oy — hisob-faktura
          oyi, pul kelgan kun emas: oktabrda to'langan sentabr puli sentabr ustunida turadi.
        </p>
      </div>

      <Card className="flex flex-wrap items-center gap-3 p-4">
        <MonthPicker
          value={fromMonth}
          max={toMonth}
          onChange={(value: string) => setFromMonth(value)}
          ariaLabel="Birinchi oy"
          className="w-44"
        />
        <span className="text-slate-400">—</span>
        <MonthPicker
          value={toMonth}
          min={fromMonth}
          onChange={(value: string) => setToMonth(value)}
          ariaLabel="Oxirgi oy"
          className="w-44"
        />

        {/* F13.01 — ko'p tanlovli sinf: ctrl/cmd+bosish bilan bir nechtasi. */}
        <select
          multiple
          value={classNames}
          onChange={(e) =>
            setClassNames([...e.target.selectedOptions].map((o) => o.value))
          }
          aria-label="Sinf (bir nechtasini tanlash mumkin)"
          title="Bir nechtasini tanlash uchun Ctrl (Cmd) bosib turing"
          className={cn(control, 'h-9 min-w-[140px] py-1')}
          size={1}
        >
          {(classes ?? []).map((c) => (
            <option key={c.id} value={c.name}>
              {c.name}
            </option>
          ))}
        </select>

        <select
          value={categoryId}
          onChange={(e) => setCategoryId(e.target.value)}
          aria-label="To'lov toifasi"
          className={control}
        >
          <option value="">Barcha toifalar</option>
          {(categories ?? []).map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>

        {/* F13.06 — bitta o'quv guruhi (hozirgi a'zolari). */}
        <select
          value={groupId}
          onChange={(e) => setGroupId(e.target.value)}
          aria-label="O'quv guruhi"
          className={control}
        >
          <option value="">Barcha guruhlar</option>
          {(groups ?? []).map((g) => (
            <option key={g.id} value={g.id}>
              {g.name}
            </option>
          ))}
        </select>

        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="O'quvchi ismi"
          aria-label="O'quvchi bo'yicha qidirish"
          className={cn(control, 'min-w-[180px] flex-1')}
        />

        <label className="flex items-center gap-2 text-sm text-slate-600">
          <input
            type="checkbox"
            checked={debtorsOnly}
            onChange={(e) => setDebtorsOnly(e.target.checked)}
            className="h-4 w-4 rounded border-slate-300"
          />
          Faqat qarzdorlar
        </label>

        <label className="flex items-center gap-2 text-sm text-slate-600">
          <input
            type="checkbox"
            checked={includeArchived}
            onChange={(e) => setIncludeArchived(e.target.checked)}
            className="h-4 w-4 rounded border-slate-300"
          />
          Arxivdagilar
        </label>

        {/* F13.02 — bitta o'quvchi — bitta toifa uchun bitta qator. */}
        <label className="flex items-center gap-2 text-sm text-slate-600">
          <input
            type="checkbox"
            checked={splitByCategory}
            onChange={(e) => setSplitByCategory(e.target.checked)}
            className="h-4 w-4 rounded border-slate-300"
          />
          Toifalar bo'yicha ajratish
        </label>

        <Button variant="secondary" onClick={handleDownload} disabled={exporting || rows.length === 0}>
          <FileSpreadsheet className="h-4 w-4" /> {exporting ? 'Tayyorlanmoqda...' : 'Excel'}
        </Button>
      </Card>

      <ReportState
        loading={loading}
        error={error}
        isEmpty={rows.length === 0}
        emptyTitle="Bu davrda hisob-faktura yo'q"
        emptyHint="Boshqa oy oralig'ini yoki sinfni tanlab ko'ring."
        onRetry={refetch}
      >
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard
            label="Hisoblangan"
            value={formatMoney(footer.total.amount)}
            icon={CalendarRange}
            hint={`${months.length} oy`}
          />
          <StatCard
            label="To'langan"
            value={formatMoney(footer.total.paid)}
            icon={Wallet}
            iconBg="bg-emerald-50"
            iconColor="text-emerald-600"
          />
          <StatCard
            label="Qoldiq"
            value={formatMoney(footer.total.toBePaid)}
            icon={AlertTriangle}
            iconBg="bg-red-50"
            iconColor="text-red-600"
          />
          <StatCard
            label="Qarzdor o'quvchilar"
            value={`${debtorCount} / ${visible.length}`}
            icon={Users}
          />
        </div>

        <Card className="p-0">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 px-4 py-3">
            <p className="text-sm text-slate-500">
              {visible.length} o'quvchi · {months.length} oy
            </p>
            <div className="flex items-center gap-1 text-xs">
              <span className="text-slate-400">Tartib:</span>
              {(
                [
                  { value: 'class', label: 'Sinf bo’yicha' },
                  { value: 'debt', label: 'Qarz bo’yicha' },
                ] as { value: SortBy; label: string }[]
              ).map((s) => (
                <button
                  key={s.value}
                  type="button"
                  onClick={() => setSortBy(s.value)}
                  className={cn(
                    'rounded-md px-2 py-1 transition',
                    sortBy === s.value
                      ? 'bg-brand-50 font-medium text-brand-700'
                      : 'text-slate-500 hover:bg-slate-50',
                  )}
                >
                  {s.label}
                </button>
              ))}
            </div>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="whitespace-nowrap">
                <tr className="border-b border-slate-100 text-left text-xs uppercase tracking-wide text-slate-400">
                  <th className="sticky left-0 z-10 bg-white px-4 py-2 font-medium">
                    № · O'quvchi
                  </th>
                  <th className="whitespace-nowrap px-3 py-2 font-medium">Telefon</th>
                  {splitByCategory && (
                    <th className="whitespace-nowrap px-3 py-2 font-medium">Toifa</th>
                  )}
                  {months.map((m) => (
                    <th key={m} className="whitespace-nowrap px-3 py-2 text-right font-medium">
                      {formatMonth(m)}
                    </th>
                  ))}
                  <th className="whitespace-nowrap px-4 py-2 text-right font-medium">Jami qoldiq</th>
                </tr>
              </thead>
              <tbody>
                {visible.map((row, i) => (
                  <StudentRow
                    key={`${row.studentId}-${row.categoryCode ?? ''}`}
                    row={row}
                    index={i}
                    months={months}
                    showCategory={splitByCategory}
                  />
                ))}
              </tbody>
              <tfoot>
                <tr className="border-t border-slate-200 bg-slate-50/70 font-medium text-slate-700">
                  <td className="sticky left-0 z-10 bg-slate-50 px-4 py-2.5">Jami</td>
                  <td className="px-3 py-2.5" />
                  {splitByCategory && <td className="px-3 py-2.5" />}
                  {months.map((m) => {
                    const cell = footer.byMonth[m]
                    return (
                      <td key={m} className="whitespace-nowrap px-3 py-2.5 text-right tabular-nums">
                        {cell === undefined ? (
                          <span className="text-slate-300">—</span>
                        ) : (
                          <span className={cell.toBePaid > 0 ? 'text-red-600' : 'text-emerald-600'}>
                            {cell.toBePaid.toLocaleString('ru-RU')}
                          </span>
                        )}
                      </td>
                    )
                  })}
                  <td className="whitespace-nowrap px-4 py-2.5 text-right tabular-nums text-red-600">
                    {footer.total.toBePaid.toLocaleString('ru-RU')}
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>

          <div className="flex flex-wrap items-center gap-4 border-t border-slate-100 px-4 py-2.5 text-xs text-slate-400">
            <span className="flex items-center gap-1.5">
              <i className="h-2.5 w-2.5 rounded-full bg-emerald-500" /> To'langan
            </span>
            <span className="flex items-center gap-1.5">
              <i className="h-2.5 w-2.5 rounded-full bg-amber-500" /> Qisman
            </span>
            <span className="flex items-center gap-1.5">
              <i className="h-2.5 w-2.5 rounded-full bg-red-500" /> To'lanmagan
            </span>
            <span className="flex items-center gap-1.5">
              <i className="h-2.5 w-2.5 rounded-full bg-slate-200" /> Hisob-faktura yo'q
            </span>
          </div>
        </Card>
      </ReportState>
    </div>
  )
}

/** Bitta o'quvchi qatori (F13.02 yoqilganda — bitta o'quvchi × toifa). */
function StudentRow({
  row,
  index,
  months,
  showCategory,
}: {
  row: ArrearsRow
  /** Filtrlangan/saralangan ro'yxatdagi o'rni — "№" ustuni (F13.03). */
  index: number
  months: string[]
  showCategory: boolean
}) {
  return (
    <tr className="border-b border-slate-50 last:border-0 hover:bg-slate-50/60">
      <td className="sticky left-0 z-10 bg-white px-4 py-2">
        <div className="flex items-center gap-2">
          <span className="w-6 shrink-0 text-right text-xs text-slate-400 tabular-nums">
            {index + 1}
          </span>
          {/* F13.03 — o'quvchi kartochkasiga havola. */}
          <Link
            to={`/admin/students/${row.studentId}`}
            className="font-medium text-slate-700 hover:text-brand-600 hover:underline"
          >
            {row.fullName}
          </Link>
          {row.isArchived && (
            <span className="rounded bg-slate-100 px-1.5 py-0.5 text-[11px] text-slate-500">
              arxiv
            </span>
          )}
        </div>
        <p className="pl-8 text-xs text-slate-400">{row.className}</p>
      </td>

      {/* F13.03 — ota-ona telefoni. */}
      <td className="whitespace-nowrap px-3 py-2 text-slate-500">{formatPhone(row.parentPhone) || '—'}</td>

      {showCategory && (
        <td className="whitespace-nowrap px-3 py-2 text-slate-500">{row.categoryName ?? '—'}</td>
      )}

      {months.map((m) => (
        <MonthCell key={m} cell={row.cells[m]} month={m} />
      ))}

      <td className="whitespace-nowrap px-4 py-2 text-right font-medium tabular-nums">
        <span className={row.total.toBePaid > 0 ? 'text-red-600' : 'text-emerald-600'}>
          {row.total.toBePaid.toLocaleString('ru-RU')}
        </span>
      </td>
    </tr>
  )
}

/**
 * Bitta katak. Uch holat uch xil o'qiladi va TO'RTINCHISI ham bor:
 * katakning yo'qligi ("hisob-faktura yo'q") — u nol emas.
 */
function MonthCell({ cell, month }: { cell: ArrearsCell | undefined; month: string }) {
  if (cell === undefined) {
    return (
      <td
        className="px-3 py-2 text-right text-slate-300"
        title={`${formatMonth(month)}: hisob-faktura yo'q`}
      >
        —
      </td>
    )
  }

  const settled = cell.toBePaid <= 0
  const untouched = cell.paid <= 0
  const tone = settled
    ? 'text-emerald-600'
    : untouched
      ? 'text-red-600 font-medium'
      : 'text-amber-600 font-medium'

  return (
    <td
      className="whitespace-nowrap px-3 py-2 text-right tabular-nums"
      title={
        `${formatMonth(month)} — hisoblangan: ${formatMoney(cell.amount)}, ` +
        `to'langan: ${formatMoney(cell.paid)}, qoldiq: ${formatMoney(cell.toBePaid)}`
      }
    >
      <span className={tone}>
        {settled ? '0' : cell.toBePaid.toLocaleString('ru-RU')}
      </span>
    </td>
  )
}
