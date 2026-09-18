/**
 * Chiqim yozish.
 *
 * TO'LOV USULI MAJBURIY (F1.01). Server `method` siz so'rovni rad etadi
 * (`invalid_method`) va ilgari formada bu maydon umuman yo'q edi — ya'ni
 * "Yangi chiqim" tugmasi har safar xato berardi. Usul shunchaki yorliq emas:
 * u jurnalning KREDIT satrini belgilaydi — naqd bo'lsa pul kassadan, qolgan
 * usullarda bankdan chiqadi (SPEC §8.1 Q13 mantig'ining ko'zgusi).
 *
 * Chegara (SPEC §4.5) SERVERDAN keladi (`approvalThreshold`), klientda
 * konstanta sifatida saqlanmaydi. Forma buni summa yozilayotgan paytda
 * AYTADI — chiqim yozilgandan keyin emas: admin nima bo'lishini oldindan
 * bilib turishi kerak, "saqladim, endi nega kutayapti?" degan savol
 * tug'ilmasin.
 *
 * Tahrirlash oynasi YO'Q: yozilgan chiqim o'zgartirilmaydi (SPEC §4.1),
 * xato yozuv storno bilan tuzatiladi.
 *
 * NAQD CHIQIM KASSANI KAMAYTIRADI (F1.03)
 * ----------------------------------------
 * Naqd pul kassaning naqd qoldig'idan chiqadi. Smena tushunchasi mijoz
 * talabi bilan (2026-09-18) butunlay olib tashlandi — ochiq smena
 * shart emas, forma ham buni endi talab qilmaydi.
 *
 * MAOSH — KIMGA (F1.09)
 * ---------------------
 * Toifa "Oylik maosh" bo'lganda o'qituvchi tanlanadi va uning shu oydagi
 * hisoblangan / berilgan / qoldiq raqamlari ko'rsatiladi
 * (`GET /admin/teachers/{id}/salary-ledger`). Usiz maosh hisoboti "falonchi
 * qancha oldi" degan savolga javob bera olmasdi: bog'lanish izoh MATNIDA
 * qolardi, bu esa bog'lanish emas, taxmin.
 *
 * HUJJAT (F1.08)
 * --------------
 * Chek surati yoki shartnoma nusxasi MAVJUD yuklash yo'li bilan boradi
 * (`POST /api/admin/uploads` + UploadGuard), so'ng chiqimga biriktiriladi.
 * Ikkinchi yuklash yo'li ATAYLAB qurilmadi. Fayllar chiqim YOZILGANDAN
 * KEYIN yuklanadi (chiqimning id'si kerak), shuning uchun ularni sahifa
 * yuboradi — forma faqat yig'ib beradi.
 */
import { useEffect, useState } from 'react'
import { AlertTriangle, Banknote, Paperclip, X } from 'lucide-react'
import type { ExpenseInput } from '@/api/services/expenses'
import type { PaymentMethod, SalaryLedger, Teacher } from '@/types'
import { getSalaryLedger, getTeachers } from '@/api/services/teachers'
import { expenseCategories } from '@/config/constants'
import { paymentMethodLabels } from '@/pages/admin/finance/reportLabels'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { formatMoney } from '@/lib/utils'
import { Notice } from './BillingUi'

const today = () => new Date().toISOString().slice(0, 10)

/** Usullar tartibi kassa ekranidagi bilan bir xil (Naqd birinchi — eng ko'p ishlatiladi). */
const methods: PaymentMethod[] = ['cash', 'card', 'transfer', 'online']

/** `Accounts.ExpenseCategories` dagi maosh toifasi — server bilan bir xil satr. */
const SALARY_CATEGORY = 'salary'

interface Props {
  open: boolean
  busy: boolean
  error: string | null
  /** Serverdagi ikkinchi tasdiq chegarasi; hali yuklanmagan bo'lsa `null`. */
  approvalThreshold: number | null
  onClose: () => void
  onSubmit: (values: ExpenseInput, files: File[]) => void
}

export function ExpenseFormModal({
  open,
  busy,
  error,
  approvalThreshold,
  onClose,
  onSubmit,
}: Props) {
  const [onDate, setOnDate] = useState(today())
  const [category, setCategory] = useState(expenseCategories[0]?.value ?? 'other')
  const [method, setMethod] = useState<PaymentMethod>('cash')
  const [amount, setAmount] = useState('')
  const [note, setNote] = useState('')
  const [teacherId, setTeacherId] = useState('')
  const [files, setFiles] = useState<File[]>([])

  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [ledger, setLedger] = useState<SalaryLedger | null>(null)

  useEffect(() => {
    if (!open) return
    /* eslint-disable react-hooks/set-state-in-effect -- oyna ochilganda formani tozalash (maqsadli) */
    setOnDate(today())
    setCategory(expenseCategories[0]?.value ?? 'other')
    setMethod('cash')
    setAmount('')
    setNote('')
    setTeacherId('')
    setFiles([])
    setLedger(null)
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [open])

  const isSalary = category === SALARY_CATEGORY

  // O'qituvchilar ro'yxati faqat maosh toifasida va faqat BIR MARTA o'qiladi.
  // Olinmasa forma ISHLAYVERADI — o'qituvchi ixtiyoriy maydon.
  useEffect(() => {
    if (!open || !isSalary || teachers.length > 0) return
    let alive = true
    getTeachers()
      .then((rows) => {
        if (alive) setTeachers(rows)
      })
      .catch(() => undefined)
    return () => {
      alive = false
    }
  }, [open, isSalary, teachers.length])

  // Tanlangan o'qituvchining SHU OY dagi maosh holati. Chiqim sanasi
  // o'zgarsa ham oy o'zgaradi, shuning uchun sana ham bog'liqlikda.
  useEffect(() => {
    if (!isSalary || teacherId === '') {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- o'qituvchi olib tashlanganda eski raqamlar qolib ketmasin
      setLedger(null)
      return
    }
    const month = onDate.slice(0, 7)
    let alive = true
    getSalaryLedger(teacherId, `${month}-01`, `${month}-31`)
      .then((data) => {
        if (alive) setLedger(data)
      })
      .catch(() => {
        if (alive) setLedger(null)
      })
    return () => {
      alive = false
    }
  }, [isSalary, teacherId, onDate])

  const amountNumber = Number(amount)
  const amountValid = amount.trim() !== '' && Number.isFinite(amountNumber) && amountNumber > 0
  const needsApproval =
    amountValid && approvalThreshold !== null && amountNumber > approvalThreshold
  const valid = amountValid && onDate !== '' && category !== ''

  const addFiles = (list: FileList | null) => {
    if (!list) return
    setFiles((current) => [...current, ...Array.from(list)])
  }

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!valid || busy) return
    const trimmedNote = note.trim()
    onSubmit(
      {
        onDate,
        category,
        amount: amountNumber,
        method,
        note: trimmedNote === '' ? undefined : trimmedNote,
        // Server o'qituvchini FAQAT maosh toifasida qabul qiladi
        // (`teacher_not_allowed`), shuning uchun boshqa toifada umuman
        // yuborilmaydi.
        teacherId: isSalary && teacherId !== '' ? teacherId : undefined,
      },
      files,
    )
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Yangi chiqim"
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="expense-form" disabled={!valid || busy}>
            {busy ? 'Saqlanmoqda...' : needsApproval ? 'Tasdiqqa yuborish' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="expense-form" onSubmit={handleSubmit} className="space-y-4">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input
            label="Sana"
            required
            type="date"
            value={onDate}
            max={today()}
            onChange={(e) => setOnDate(e.target.value)}
          />
          <Select
            label="Toifa"
            required
            value={category}
            onChange={(e) => setCategory(e.target.value)}
          >
            {expenseCategories.map((c) => (
              <option key={c.value} value={c.value}>
                {c.label}
              </option>
            ))}
          </Select>
        </div>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div>
            <Input
              label="Summa (so'm)"
              required
              type="number"
              min={1}
              step={1000}
              inputMode="numeric"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
            />
            {amountValid && (
              <p className="mt-1 text-xs text-slate-500">{formatMoney(amountNumber)}</p>
            )}
            {amount.trim() !== '' && !amountValid && (
              <p className="mt-1 text-xs text-red-600">Summa noldan katta bo'lishi kerak.</p>
            )}
          </div>

          <div>
            <Select
              label="To'lov usuli"
              required
              value={method}
              onChange={(e) => setMethod(e.target.value as PaymentMethod)}
            >
              {methods.map((m) => (
                <option key={m} value={m}>
                  {paymentMethodLabels[m]}
                </option>
              ))}
            </Select>
            <p className="mt-1 text-xs text-slate-500">
              {method === 'cash' ? 'Pul kassadan chiqadi.' : 'Pul bank hisobidan chiqadi.'}
            </p>
          </div>
        </div>

        {/* ---- F1.03: naqd chiqim kassaning naqd qoldig'idan yoziladi ---- */}
        {method === 'cash' && !needsApproval && (
          <div className="flex items-start gap-2 rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-600">
            <Banknote className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" />
            <p>
              Naqd chiqim <b>kassaning naqd qoldig'idan</b> yoziladi va uni kamaytiradi.
            </p>
          </div>
        )}

        {/* ---- F1.09: maosh kimga ---- */}
        {isSalary && (
          <div>
            <Select
              label="O'qituvchi"
              value={teacherId}
              onChange={(e) => setTeacherId(e.target.value)}
            >
              <option value="">Ko'rsatilmasin</option>
              {teachers.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.fullName}
                </option>
              ))}
            </Select>
            {ledger ? (
              <dl className="mt-2 grid grid-cols-3 gap-2 rounded-lg bg-slate-50 p-2 text-center text-xs">
                <SalaryFigure label="Hisoblangan" value={ledger.totalExpected} />
                <SalaryFigure label="Berilgan" value={ledger.totalPaid} />
                <SalaryFigure label="Qoldiq" value={ledger.remaining} />
              </dl>
            ) : (
              <p className="mt-1 text-xs text-slate-500">
                O'qituvchi tanlansa, shu oydagi hisoblangan va berilgan maosh ko'rinadi.
              </p>
            )}
          </div>
        )}

        {/* ---- F1.08: hujjat ---- */}
        <div>
          <span className="text-sm font-medium text-slate-600">Hujjat (chek, shartnoma)</span>
          <div className="mt-1 flex flex-wrap items-center gap-2">
            <label className="inline-flex cursor-pointer items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-600 hover:bg-slate-50">
              <Paperclip className="h-4 w-4" />
              Fayl qo'shish
              <input
                type="file"
                multiple
                className="hidden"
                disabled={busy}
                onChange={(e) => {
                  addFiles(e.target.files)
                  e.target.value = ''
                }}
              />
            </label>
            <span className="text-xs text-slate-400">
              Rasm yoki PDF, 20 MB gacha. Biriktirilgan hujjat keyin o'chirilmaydi.
            </span>
          </div>
          {files.length > 0 && (
            <ul className="mt-2 space-y-1">
              {files.map((file, index) => (
                <li
                  key={`${file.name}-${index}`}
                  className="flex items-center justify-between rounded-lg bg-slate-50 px-3 py-1.5 text-xs text-slate-600"
                >
                  <span className="truncate">{file.name}</span>
                  <button
                    type="button"
                    className="ml-2 shrink-0 text-slate-400 hover:text-red-600"
                    disabled={busy}
                    onClick={() => setFiles((current) => current.filter((_, i) => i !== index))}
                    aria-label="Faylni ro'yxatdan olib tashlash"
                  >
                    <X className="h-3.5 w-3.5" />
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>

        {needsApproval && approvalThreshold !== null && (
          <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <p>
              Summa {formatMoney(approvalThreshold)} dan yuqori — chiqim{' '}
              <b>ikkinchi tasdiqni</b> talab qiladi va tasdiq navbatiga tushadi. Tasdiqni siz
              emas, boshqa mas'ul beradi va to'lov usulini o'sha tasdiqlovchi belgilaydi.
            </p>
          </div>
        )}

        <Textarea
          label="Izoh"
          rows={2}
          placeholder="masalan: Sentabr oyi elektr to'lovi"
          value={note}
          onChange={(e) => setNote(e.target.value)}
        />

        {error && <Notice>{error}</Notice>}
      </form>
    </Modal>
  )
}

/** Maosh raqami — "hisoblangan / berilgan / qoldiq" uchligi uchun (F1.09). */
function SalaryFigure({ label, value }: { label: string; value: number }) {
  return (
    <div>
      <dt className="text-slate-400">{label}</dt>
      <dd className="mt-0.5 font-semibold tabular-nums text-slate-700">{formatMoney(value)}</dd>
    </div>
  )
}
