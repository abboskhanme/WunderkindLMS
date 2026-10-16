import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { AlertTriangle, Download, RefreshCw } from 'lucide-react'
import type { CashBox } from '@/api/services/cashBoxes'
import type { CashierStudent } from '@/api/services/cashier'
import { MIN_SEARCH_LENGTH, financeErrorMessage, isAborted, searchStudents } from '@/api/services/cashier'
import type { ShopSalesGroupRow, ShopSalesReport as Report } from '@/api/services/shop'
import { exportShopSalesReport, getShopSalesReport, shopKindLabel } from '@/api/services/shop'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { Loader } from '@/components/ui/Loader'
import { Select } from '@/components/ui/Input'
import { DatePicker } from '@/components/ui/DatePicker'
import { formatDateTime, formatSum, methodLabels } from './format'

/* ==========================================================================
   Savdo hisoboti — mini-magazin (mijoz, 2026-10-15; docs/modules/shop.md).

   Kassa sahifasida, sarlavhadagi tugma ochadi: yangi marshrut ham, Moliya menyusida yangi yozuv
   ham yo'q (`navigation.ts`: "YANGI YOZUV QO'SHMANG"). Ko'radiganlar — Kassa jurnalini
   ko'radiganlarning o'zi (server: `ViewCashBoxes`): jurnalda ko'ringan kirimlarning mahsulot
   kesimi, boshqa hech narsa.

   Bekor qilingan (storno) sotuv hisobotga kirmaydi. Pul serverda hisoblanadi — bu yerda faqat
   ko'rsatiladi (`CashLedger` qoidasi).
   ========================================================================== */

const today = () => new Date().toISOString().slice(0, 10)

interface Props {
  boxes: CashBox[]
  /** The box selected on the page — the report opens on it; "Barcha kassalar" is one click away. */
  initialBoxId?: string
  onClose: () => void
}

export function ShopSalesReport({ boxes, initialBoxId, onClose }: Props) {
  const [from, setFrom] = useState(today())
  const [to, setTo] = useState(today())
  const [boxId, setBoxId] = useState(initialBoxId ?? '')
  const [pupil, setPupil] = useState<CashierStudent | null>(null)

  const [report, setReport] = useState<Report | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reload, setReload] = useState(0)
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)

  const filters = { from, to, boxId: boxId || undefined, studentId: pupil?.id }

  useEffect(() => {
    if (to < from) return
    const controller = new AbortController()
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklaymiz (maqsadli)
    setLoading(true)
    setError(null)
    getShopSalesReport({ from, to, boxId: boxId || undefined, studentId: pupil?.id }, controller.signal)
      .then((res) => {
        setReport(res)
        setLoading(false)
      })
      .catch((err: unknown) => {
        if (isAborted(err)) return
        setError(financeErrorMessage(err, "Hisobotni yuklab bo'lmadi."))
        setLoading(false)
      })
    return () => controller.abort()
  }, [from, to, boxId, pupil, reload])

  const handleExport = async () => {
    if (exporting) return
    setExporting(true)
    setExportError(null)
    try {
      await exportShopSalesReport(filters)
    } catch (err) {
      setExportError(financeErrorMessage(err, "Eksport qilib bo'lmadi."))
    } finally {
      setExporting(false)
    }
  }

  const periodInvalid = to < from

  return (
    <Modal
      open
      onClose={onClose}
      title="Savdo hisoboti"
      size="xl"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
          <Button onClick={() => void handleExport()} disabled={exporting || periodInvalid || !report}>
            <Download className="h-4 w-4" /> {exporting ? 'Tayyorlanmoqda…' : 'Excel'}
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        <div className="flex flex-wrap items-end gap-3">
          <div>
            <span className="mb-1 block text-sm font-medium text-slate-600">Davr</span>
            <div className="flex items-center gap-2">
              <DatePicker value={from} onChange={setFrom} ariaLabel="Davr boshi" className="w-40" />
              <span className="text-slate-400">—</span>
              <DatePicker value={to} onChange={setTo} ariaLabel="Davr oxiri" className="w-40" invalid={periodInvalid} />
            </div>
          </div>
          <Select label="Kassa" value={boxId} onChange={(e) => setBoxId(e.target.value)} className="max-w-xs">
            <option value="">Barcha kassalar</option>
            {boxes.map((b) => (
              <option key={b.id} value={b.id}>
                {b.name}
              </option>
            ))}
          </Select>
          <PupilPicker value={pupil} onChange={setPupil} />
        </div>
        {periodInvalid && <p className="text-sm text-red-600">Davr oxiri boshidan oldin bo'lishi mumkin emas.</p>}
        {exportError && <p className="text-sm text-red-600">{exportError}</p>}

        {loading && !periodInvalid && <Loader label="Hisobot yuklanmoqda..." />}

        {!loading && error && (
          <div className="rounded-xl border border-red-200 bg-red-50/70 px-3 py-3">
            <div className="flex items-start gap-2 text-sm text-red-700">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <span>{error}</span>
            </div>
            <Button variant="secondary" className="mt-3" onClick={() => setReload((n) => n + 1)}>
              <RefreshCw className="h-4 w-4" /> Qayta urinish
            </Button>
          </div>
        )}

        {!loading && !error && report && report.salesCount === 0 && (
          <p className="py-10 text-center text-sm text-slate-400">
            Bu davrda mahsulot sotilmagan. Sotuv Kassa → Kirim shaklida, mahsulotli Kirim turini tanlab yoziladi.
          </p>
        )}

        {!loading && !error && report && report.salesCount > 0 && (
          <>
            <div className="grid gap-3 sm:grid-cols-3">
              <Stat label="Savdolar" value={String(report.salesCount)} />
              <Stat label="Sotilgan mahsulot" value={`${formatSum(report.quantity)} ta`} />
              <Stat label="Jami summa" value={`${formatSum(report.amount)} so'm`} />
            </div>

            <Section title="Mahsulotlar bo'yicha">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-3 py-2">Mahsulot</th>
                    <th className="px-3 py-2">Turi</th>
                    <th className="px-3 py-2 text-right">Soni</th>
                    <th className="px-3 py-2 text-right">Summa</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {report.byProduct.map((r) => (
                    <tr key={r.productId}>
                      <td className="px-3 py-2 font-medium text-slate-800">{r.name}</td>
                      <td className="px-3 py-2 text-slate-600">{shopKindLabel(r.kind)}</td>
                      <td className="px-3 py-2 text-right tabular-nums text-slate-700">{formatSum(r.quantity)}</td>
                      <td className="px-3 py-2 text-right font-medium tabular-nums text-slate-800">{formatSum(r.amount)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Section>

            <div className="grid gap-4 lg:grid-cols-3">
              <GroupTable title="Mahsulot turi bo'yicha" rows={report.byKind} label={(key) => shopKindLabel(key)} />
              <GroupTable title="Kirim turi bo'yicha" rows={report.byType} />
              <GroupTable title="Kassir bo'yicha" rows={report.byCashier} />
            </div>

            <Section
              title="Savdolar"
              hint={report.salesTruncated ? `Eng so'nggi ${report.sales.length} tasi ko'rsatilgan — jami raqamlar hammasini qamraydi.` : undefined}
            >
              <table className="w-full text-left text-sm">
                <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-3 py-2">Sana</th>
                    <th className="px-3 py-2">Kassa</th>
                    <th className="px-3 py-2">Kassir</th>
                    <th className="px-3 py-2">O'quvchi</th>
                    <th className="px-3 py-2">Mahsulotlar</th>
                    <th className="px-3 py-2">Usul</th>
                    <th className="px-3 py-2 text-right">Summa</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {report.sales.map((s) => (
                    <tr key={s.transactionId} className="align-top">
                      <td className="whitespace-nowrap px-3 py-2 text-slate-600">{formatDateTime(s.createdAt)}</td>
                      <td className="px-3 py-2 text-slate-600">{s.boxName}</td>
                      <td className="px-3 py-2 text-slate-600">{s.cashierName}</td>
                      <td className="px-3 py-2 text-slate-600">{s.studentName ?? '—'}</td>
                      <td className="px-3 py-2 text-slate-600">
                        {s.lines.map((l) => `${l.productName} × ${l.quantity}`).join(', ')}
                      </td>
                      <td className="whitespace-nowrap px-3 py-2 text-slate-600">{methodLabels[s.method] ?? s.method}</td>
                      <td className="px-3 py-2 text-right font-medium tabular-nums text-slate-800">{formatSum(s.amount)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Section>
          </>
        )}
      </div>
    </Modal>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-slate-200 px-4 py-3">
      <p className="text-xs uppercase tracking-wide text-slate-400">{label}</p>
      <p className="mt-1 text-lg font-semibold tabular-nums text-slate-800">{value}</p>
    </div>
  )
}

function Section({ title, hint, children }: { title: string; hint?: string; children: ReactNode }) {
  return (
    <section>
      <div className="mb-2 flex flex-wrap items-baseline justify-between gap-2">
        <h3 className="text-sm font-semibold text-slate-700">{title}</h3>
        {hint && <p className="text-xs text-slate-400">{hint}</p>}
      </div>
      <div className="overflow-x-auto rounded-xl border border-slate-200">{children}</div>
    </section>
  )
}

function GroupTable({
  title,
  rows,
  label = (_key, name) => name,
}: {
  title: string
  rows: ShopSalesGroupRow[]
  label?: (key: string, name: string) => string
}) {
  return (
    <Section title={title}>
      <table className="w-full text-left text-sm">
        <tbody className="divide-y divide-slate-100">
          {rows.map((r) => (
            <tr key={r.key}>
              <td className="px-3 py-2 text-slate-700">
                {label(r.key, r.name)}
                <span className="block text-xs text-slate-400">
                  {r.sales} ta savdo · {formatSum(r.quantity)} ta
                </span>
              </td>
              <td className="px-3 py-2 text-right font-medium tabular-nums text-slate-800">{formatSum(r.amount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </Section>
  )
}

/** Pupil filter — the cash desk's own minimal search (`/api/cash/students`), open to the cashier. */
function PupilPicker({ value, onChange }: { value: CashierStudent | null; onChange: (s: CashierStudent | null) => void }) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<CashierStudent[]>([])
  const [open, setOpen] = useState(false)
  const term = query.trim()

  useEffect(() => {
    if (term.length < MIN_SEARCH_LENGTH) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- qisqa so'rovda natijani darrov tozalaymiz (StudentSearch naqshi)
      setResults([])
      return
    }
    const controller = new AbortController()
    const timer = setTimeout(() => {
      searchStudents(term, controller.signal)
        .then(setResults)
        .catch(() => undefined)
    }, 300)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [term])

  if (value) {
    return (
      <div>
        <span className="mb-1 block text-sm font-medium text-slate-600">O'quvchi</span>
        <div className="flex items-center gap-2 rounded-lg border border-slate-200 bg-slate-50 px-3 py-1.5 text-sm">
          <span className="font-medium text-slate-800">{value.fullName}</span>
          <button type="button" onClick={() => onChange(null)} className="text-xs font-medium text-brand-600 hover:underline">
            Tozalash
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className="relative w-56">
      <label className="mb-1 block text-sm font-medium text-slate-600">O'quvchi</label>
      <input
        value={query}
        onChange={(e) => {
          setQuery(e.target.value)
          setOpen(true)
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        placeholder="Ism bo'yicha qidirish"
        autoComplete="off"
        className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
      />
      {open && term.length >= MIN_SEARCH_LENGTH && (
        <ul className="absolute z-20 mt-1 max-h-64 w-72 overflow-y-auto rounded-lg border border-slate-200 bg-white p-1 shadow-lg">
          {results.length === 0 && <li className="px-3 py-2 text-sm text-slate-400">Topilmadi</li>}
          {results.map((s) => (
            <li key={s.id}>
              <button
                type="button"
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => {
                  onChange(s)
                  setQuery('')
                  setOpen(false)
                }}
                className="w-full rounded-lg px-3 py-1.5 text-left text-sm hover:bg-slate-50"
              >
                <span className="block font-medium text-slate-800">{s.fullName}</span>
                <span className="block text-xs text-slate-400">{s.className}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
