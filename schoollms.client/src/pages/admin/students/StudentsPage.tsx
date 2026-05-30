import { useEffect, useRef, useState } from 'react'
import { Plus, Search, Eye, Pencil, Trash2, Send, Download, X, Wallet, History, Archive, RotateCcw } from 'lucide-react'
import type { Gender, Student } from '@/types'
import type { StudentPayload } from '@/api/services/students'
import {
  getStudents,
  getArchivedStudents,
  archiveStudent,
  restoreStudent,
  createStudent,
  updateStudent,
  deleteStudent,
  addPayment,
} from '@/api/services/students'
import { getClasses } from '@/api/services/classes'
import { genderLabels } from '@/config/constants'
import { formatDate, formatMoney, exportToCsv, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { StudentFormModal } from './StudentFormModal'
import { StudentViewModal } from './StudentViewModal'
import { SmsModal } from './SmsModal'
import { PaymentModal } from './PaymentModal'
import { PaymentHistoryModal } from './PaymentHistoryModal'

type BalanceFilter = 'all' | 'debt' | 'paid'
type Tab = 'active' | 'archived'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

export function StudentsPage() {
  const [tab, setTab] = useState<Tab>('active')
  const [students, setStudents] = useState<Student[]>([])
  const [archived, setArchived] = useState<Student[]>([])
  const [classNames, setClassNames] = useState<string[]>([])
  const [loading, setLoading] = useState(true)
  /** Arxivlash modali — sabab kiritish uchun. */
  const [archiveTarget, setArchiveTarget] = useState<Student | null>(null)
  const [archiveReason, setArchiveReason] = useState('')

  // filtrlar
  const [search, setSearch] = useState('')
  const [classFilter, setClassFilter] = useState('all')
  const [genderFilter, setGenderFilter] = useState<'all' | Gender>('all')
  const [balanceFilter, setBalanceFilter] = useState<BalanceFilter>('all')

  // tanlash
  const [selected, setSelected] = useState<Set<string>>(new Set())

  // modallar
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Student | null>(null)
  const [viewing, setViewing] = useState<Student | null>(null)
  const [smsOpen, setSmsOpen] = useState(false)
  const [paying, setPaying] = useState<Student | null>(null)
  const [historyOf, setHistoryOf] = useState<Student | null>(null)

  // Chegirma o'zgarganda — yangi chegirmani joriy oyga qo'llashni so'rash
  const [discountPrompt, setDiscountPrompt] = useState<{
    id: string
    values: StudentPayload
    oldPct: number
    oldAmount: number
    newPct: number
    newAmount: number
  } | null>(null)

  useEffect(() => {
    setLoading(true)
    Promise.all([getStudents(), getArchivedStudents()])
      .then(([active, arch]) => {
        setStudents(active)
        setArchived(arch)
      })
      .finally(() => setLoading(false))
    getClasses().then((cs) => setClassNames(cs.map((c) => c.name)))
  }, [])

  // Joriy tab manbai.
  const source = tab === 'active' ? students : archived

  const filtered = source.filter((s) => {
    const q = search.trim().toLowerCase()
    const matchSearch =
      !q ||
      s.fullName.toLowerCase().includes(q) ||
      s.parentFullName.toLowerCase().includes(q)
    const matchClass = classFilter === 'all' || s.className === classFilter
    const matchGender = genderFilter === 'all' || s.gender === genderFilter
    const matchBalance =
      balanceFilter === 'all' ||
      (balanceFilter === 'debt' ? s.balance < 0 : s.balance >= 0)
    return matchSearch && matchClass && matchGender && matchBalance
  })

  const selectedStudents = source.filter((s) => selected.has(s.id))

  // hammasini tanlash holati
  const allSelected = filtered.length > 0 && filtered.every((s) => selected.has(s.id))
  const someSelected = filtered.some((s) => selected.has(s.id)) && !allSelected
  const headerCbRef = useRef<HTMLInputElement>(null)
  useEffect(() => {
    if (headerCbRef.current) headerCbRef.current.indeterminate = someSelected
  })

  const toggleOne = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const toggleAll = () =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (allSelected) filtered.forEach((s) => next.delete(s.id))
      else filtered.forEach((s) => next.add(s.id))
      return next
    })

  const clearSelection = () => setSelected(new Set())

  const handleExport = () => {
    exportToCsv(
      'oquvchilar.csv',
      ['F.I.SH', 'Sinf', 'Jinsi', "Tug'ilgan kun", 'Manzil', 'Ota-ona', 'Telefon', 'Balans', 'Chegirma'],
      selectedStudents.map((s) => [
        s.fullName,
        s.className,
        genderLabels[s.gender],
        formatDate(s.birthDate),
        s.address,
        s.parentFullName,
        s.parentPhone,
        formatMoney(s.balance),
        s.discountPct > 0 || s.discountAmount > 0
          ? [
              s.discountPct > 0 ? `${s.discountPct}%` : null,
              s.discountAmount > 0 ? formatMoney(s.discountAmount) : null,
            ]
              .filter(Boolean)
              .join(' + ') + (s.discountNote ? ` — ${s.discountNote}` : '')
          : '',
      ]),
    )
  }

  const applyUpdate = (id: string, values: StudentPayload, applyDiscount: boolean) => {
    updateStudent(id, values, applyDiscount)
    // balansni saqlab qolib, qolgan maydonlarni yangilaymiz
    setStudents((prev) => prev.map((s) => (s.id === id ? { ...s, ...values } : s)))
  }

  const resolveDiscountPrompt = (applyDiscount: boolean) => {
    if (!discountPrompt) return
    applyUpdate(discountPrompt.id, discountPrompt.values, applyDiscount)
    setDiscountPrompt(null)
  }

  const handleFormSubmit = (values: StudentPayload) => {
    if (editing) {
      const id = editing.id
      const newPct = values.discountPct ?? 0
      const newAmount = values.discountAmount ?? 0
      const oldPct = editing.discountPct
      const oldAmount = editing.discountAmount
      const discountChanged = newPct !== oldPct || newAmount !== oldAmount
      if (discountChanged) {
        // "Ha/Yo'q" tasdiq dialog'i — joriy oyga qo'llash yoki keyingi oydan?
        setDiscountPrompt({ id, values, oldPct, oldAmount, newPct, newAmount })
      } else {
        applyUpdate(id, values, false)
      }
    } else {
      createStudent(values).then((created) => {
        setStudents((prev) => [created, ...prev])
        // Yangi o'quvchining login/parolini darrov ko'rsatamiz.
        setViewing(created)
      })
    }
    setFormOpen(false)
    setEditing(null)
  }

  const handlePayment = (amount: number, month: string) => {
    if (!paying) return
    const id = paying.id
    addPayment(id, amount, month)
    setStudents((prev) =>
      prev.map((s) => (s.id === id ? { ...s, balance: s.balance + amount } : s)),
    )
    setPaying(null)
  }

  const handleDelete = (s: Student) => {
    if (!confirm(`"${s.fullName}" o'quvchini BUTUNLAY o'chirishni tasdiqlaysizmi? Bu amal qaytarib bo'lmaydi.`)) return
    deleteStudent(s.id).then(() => {
      setStudents((prev) => prev.filter((x) => x.id !== s.id))
      setArchived((prev) => prev.filter((x) => x.id !== s.id))
      setSelected((prev) => {
        const next = new Set(prev)
        next.delete(s.id)
        return next
      })
    })
  }

  /** Arxivga ko'chirish — sabab so'raydi va backend'ga uzatadi. */
  const openArchive = (s: Student) => {
    setArchiveTarget(s)
    setArchiveReason('')
  }
  const confirmArchive = () => {
    if (!archiveTarget) return
    const s = archiveTarget
    archiveStudent(s.id, archiveReason.trim()).then(() => {
      // Faol ro'yxatdan olib tashlab, arxivga qo'shamiz (yangi sana bilan).
      const updated: Student = {
        ...s,
        isArchived: true,
        archivedAt: new Date().toISOString().slice(0, 10),
        archiveReason: archiveReason.trim() || null,
      }
      setStudents((prev) => prev.filter((x) => x.id !== s.id))
      setArchived((prev) => [updated, ...prev])
      setSelected((prev) => {
        const next = new Set(prev)
        next.delete(s.id)
        return next
      })
      setArchiveTarget(null)
    })
  }
  /** Arxivdan qaytarish. */
  const handleRestore = (s: Student) => {
    if (!confirm(`"${s.fullName}" o'quvchini arxivdan qaytarish? Login bloklangicha qoladi — keyin parol generatsiya qiling.`)) return
    restoreStudent(s.id).then(() => {
      const updated: Student = { ...s, isArchived: false, archivedAt: null, archiveReason: null }
      setArchived((prev) => prev.filter((x) => x.id !== s.id))
      setStudents((prev) => [updated, ...prev])
    })
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">O'quvchilar</h1>
          <p className="text-sm text-slate-400">
            {tab === 'active'
              ? `Faol: ${students.length} ta · Arxivda: ${archived.length} ta`
              : `Arxivda: ${archived.length} ta o'quvchi`}
          </p>
        </div>
        <div className="flex items-center gap-2">
          {/* Faol/Arxiv tab toggle */}
          <div className="flex gap-1 rounded-lg bg-slate-100 p-1">
            <button
              type="button"
              onClick={() => {
                setTab('active')
                clearSelection()
              }}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                tab === 'active'
                  ? 'bg-white text-brand-700 shadow-sm'
                  : 'text-slate-500 hover:text-slate-700',
              )}
            >
              Faol
            </button>
            <button
              type="button"
              onClick={() => {
                setTab('archived')
                clearSelection()
              }}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                tab === 'archived'
                  ? 'bg-white text-amber-700 shadow-sm'
                  : 'text-slate-500 hover:text-slate-700',
              )}
            >
              <Archive className="mr-1 inline h-4 w-4" />
              Arxiv ({archived.length})
            </button>
          </div>
          {tab === 'active' && (
            <Button
              onClick={() => {
                setEditing(null)
                setFormOpen(true)
              }}
            >
              <Plus className="h-4 w-4" /> Yangi qo'shish
            </Button>
          )}
        </div>
      </div>

      <Card className="p-0">
        {/* Filtrlar */}
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <div className="relative flex-1 min-w-[200px]">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="F.I.SH yoki ota-ona bo'yicha qidirish..."
              className={cn(control, 'w-full pl-9')}
            />
          </div>
          <select
            value={classFilter}
            onChange={(e) => setClassFilter(e.target.value)}
            className={control}
          >
            <option value="all">Barcha sinflar</option>
            {classNames.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
          <select
            value={genderFilter}
            onChange={(e) => setGenderFilter(e.target.value as 'all' | Gender)}
            className={control}
          >
            <option value="all">Barcha jinslar</option>
            <option value="male">{genderLabels.male}</option>
            <option value="female">{genderLabels.female}</option>
          </select>
          <select
            value={balanceFilter}
            onChange={(e) => setBalanceFilter(e.target.value as BalanceFilter)}
            className={control}
          >
            <option value="all">Barcha balans</option>
            <option value="debt">Qarzdorlar</option>
            <option value="paid">Qarzsizlar</option>
          </select>
        </div>

        {/* Tanlanganlar uchun amal paneli */}
        {selected.size > 0 && (
          <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 bg-brand-50/60 px-4 py-3">
            <span className="text-sm font-medium text-brand-700">
              {selected.size} ta tanlandi
            </span>
            <Button variant="secondary" onClick={() => setSmsOpen(true)}>
              <Send className="h-4 w-4" /> SMS yuborish
            </Button>
            <Button variant="secondary" onClick={handleExport}>
              <Download className="h-4 w-4" /> Yuklab olish (CSV)
            </Button>
            <button
              onClick={clearSelection}
              className="ml-auto inline-flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700"
            >
              <X className="h-4 w-4" /> Bekor qilish
            </button>
          </div>
        )}

        {/* Jadval */}
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-10 px-4 py-3">
                    <input
                      ref={headerCbRef}
                      type="checkbox"
                      checked={allSelected}
                      onChange={toggleAll}
                      className="h-4 w-4 accent-brand-600"
                    />
                  </th>
                  <th className="w-10 px-2 py-3">#</th>
                  <th className="px-4 py-3">F.I.SH</th>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3">Jinsi</th>
                  <th className="px-4 py-3">Tug'ilgan kun</th>
                  <th className="px-4 py-3">Ota-ona</th>
                  <th className="px-4 py-3">Telefon</th>
                  <th className="px-4 py-3">Balans</th>
                  {tab === 'archived' && <th className="px-4 py-3">Arxiv sanasi</th>}
                  {tab === 'archived' && <th className="px-4 py-3">Sabab</th>}
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((s, i) => (
                  <tr key={s.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3">
                      <input
                        type="checkbox"
                        checked={selected.has(s.id)}
                        onChange={() => toggleOne(s.id)}
                        className="h-4 w-4 accent-brand-600"
                      />
                    </td>
                    <td className="px-2 py-3 text-slate-400">{i + 1}</td>
                    <td className="px-4 py-3 font-medium text-slate-800">{s.fullName}</td>
                    <td className="px-4 py-3">
                      <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                        {s.className}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-slate-600">{genderLabels[s.gender]}</td>
                    <td className="px-4 py-3 text-slate-600">{formatDate(s.birthDate)}</td>
                    <td className="px-4 py-3 text-slate-600">{s.parentFullName}</td>
                    <td className="px-4 py-3 text-slate-600">{s.parentPhone}</td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'font-medium',
                          s.balance < 0
                            ? 'text-red-600'
                            : s.balance > 0
                              ? 'text-emerald-600'
                              : 'text-slate-500',
                        )}
                      >
                        {s.balance > 0 ? `+${formatMoney(s.balance)}` : formatMoney(s.balance)}
                      </span>
                    </td>
                    {tab === 'archived' && (
                      <td className="px-4 py-3 text-slate-600">{s.archivedAt ? formatDate(s.archivedAt) : '—'}</td>
                    )}
                    {tab === 'archived' && (
                      <td className="px-4 py-3 text-slate-600 max-w-[18rem] truncate" title={s.archiveReason ?? ''}>
                        {s.archiveReason || '—'}
                      </td>
                    )}
                    <td className="px-4 py-3">
                      <div className="flex items-center justify-end gap-0.5">
                        {tab === 'active' ? (
                          <>
                            <IconBtn icon={Wallet} title="To'lov kiritish" onClick={() => setPaying(s)} />
                            <IconBtn icon={History} title="To'lov tarixi" onClick={() => setHistoryOf(s)} />
                            <IconBtn icon={Eye} title="Ko'rish" onClick={() => setViewing(s)} />
                            <IconBtn
                              icon={Pencil}
                              title="Tahrirlash"
                              onClick={() => {
                                setEditing(s)
                                setFormOpen(true)
                              }}
                            />
                            <IconBtn icon={Archive} title="Arxivga ko'chirish" onClick={() => openArchive(s)} />
                          </>
                        ) : (
                          <>
                            <IconBtn icon={Eye} title="Ko'rish" onClick={() => setViewing(s)} />
                            <IconBtn icon={RotateCcw} title="Arxivdan qaytarish" onClick={() => handleRestore(s)} />
                            <IconBtn
                              icon={Trash2}
                              title="Butunlay o'chirish"
                              danger
                              onClick={() => handleDelete(s)}
                            />
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
                {filtered.length === 0 && (
                  <tr>
                    <td colSpan={tab === 'archived' ? 12 : 10} className="px-4 py-12 text-center text-slate-400">
                      {tab === 'archived' ? 'Arxivda o\'quvchi yo\'q' : 'Hech narsa topilmadi'}
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {/* Modallar */}
      <StudentFormModal
        open={formOpen}
        onClose={() => {
          setFormOpen(false)
          setEditing(null)
        }}
        onSubmit={handleFormSubmit}
        initial={editing}
      />
      <StudentViewModal student={viewing} onClose={() => setViewing(null)} />
      <SmsModal open={smsOpen} onClose={() => setSmsOpen(false)} recipients={selectedStudents} />
      <PaymentModal student={paying} onClose={() => setPaying(null)} onSubmit={handlePayment} />
      <PaymentHistoryModal student={historyOf} onClose={() => setHistoryOf(null)} />

      {/* Arxivga ko'chirish modali — sabab kiritish */}
      <Modal
        open={!!archiveTarget}
        onClose={() => setArchiveTarget(null)}
        title="O'quvchini arxivga ko'chirish"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setArchiveTarget(null)}>
              Bekor qilish
            </Button>
            <Button variant="danger" onClick={confirmArchive}>
              <Archive className="h-4 w-4" /> Arxivga ko'chirish
            </Button>
          </>
        }
      >
        {archiveTarget && (
          <div className="space-y-3 text-sm text-slate-600">
            <p>
              <span className="font-medium text-slate-800">{archiveTarget.fullName}</span>{' '}
              o'quvchini arxivga ko'chirasiz. Tarixiy ma'lumotlar (jurnal, davomat, to'lovlar)
              saqlanadi, lekin:
            </p>
            <ul className="ml-5 list-disc space-y-0.5 text-slate-500">
              <li>Faol ro'yxatdan yashirinadi (jurnal/davomat/dashboardda ko'rinmaydi)</li>
              <li>Oylik to'lov hisoblanmaydi</li>
              <li>Login bloklanadi (akkaunt paroli o'chiriladi)</li>
            </ul>
            <div>
              <span className="mb-1 block text-sm font-medium text-slate-700">
                Sababi (ixtiyoriy)
              </span>
              <input
                value={archiveReason}
                onChange={(e) => setArchiveReason(e.target.value)}
                placeholder="masalan: Boshqa maktabga ko'chdi, oilaviy sabab..."
                className={cn(control, 'w-full')}
                autoFocus
              />
            </div>
          </div>
        )}
      </Modal>

      <Modal
        open={!!discountPrompt}
        onClose={() => setDiscountPrompt(null)}
        title="Chegirmani joriy oyga qo'llash"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => resolveDiscountPrompt(false)}>
              Yo'q — keyingi oydan
            </Button>
            <Button onClick={() => resolveDiscountPrompt(true)}>Ha — joriy oydan</Button>
          </>
        }
      >
        {discountPrompt && (
          <div className="space-y-3 text-sm text-slate-600">
            <p>
              <span className="font-medium text-slate-800">
                {discountPrompt.values.fullName}
              </span>{' '}
              o'quvchisining chegirmasi{' '}
              <span className="font-medium">
                {discountPrompt.oldPct}% / {formatMoney(discountPrompt.oldAmount)}
              </span>{' '}
              →{' '}
              <span className="font-medium">
                {discountPrompt.newPct}% / {formatMoney(discountPrompt.newAmount)}
              </span>{' '}
              ga o'zgardi. Yangi chegirma qachondan qo'llansin?
            </p>
            <div className="rounded-lg bg-slate-50 px-3 py-2 text-slate-500">
              <p>
                <b className="text-slate-700">Ha</b> — joriy oy hisobi yangi chegirma bilan qayta
                hisoblanadi (balans farqqa moslab to'g'rilanadi).
              </p>
              <p className="mt-1">
                <b className="text-slate-700">Yo'q</b> — joriy oy eski hisobda qoladi, yangi
                chegirma keyingi oydan amal qiladi.
              </p>
            </div>
          </div>
        )}
      </Modal>
    </div>
  )
}

interface IconBtnProps {
  icon: typeof Eye
  title: string
  onClick: () => void
  danger?: boolean
}

function IconBtn({ icon: Icon, title, onClick, danger }: IconBtnProps) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className={cn(
        'rounded-lg p-1.5 transition-colors',
        danger
          ? 'text-slate-400 hover:bg-red-50 hover:text-red-600'
          : 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
      )}
    >
      <Icon className="h-4 w-4" />
    </button>
  )
}
