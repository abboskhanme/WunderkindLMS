import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { StudentViewModal } from './StudentViewModal'
import {
  Plus,
  Pencil,
  Trash2,
  Send,
  Download,
  X,
  History,
  Archive,
  RotateCcw,
  Upload,
  MessageSquare,
  ChevronUp,
  ChevronDown,
  FileSpreadsheet,
  FileSignature,
} from 'lucide-react'
import type { Student } from '@/types'
import type { StudentPayload } from '@/api/services/students'
import {
  createStudent,
  updateStudent,
  restoreStudent,
  deleteStudent,
  downloadStudentCredentials,
} from '@/api/services/students'
import {
  searchStudents,
  exportStudents,
  deleteStudentsMany,
  type StudentListFilter,
  type StudentListPage,
  type StudentListRow,
  type StudentSortKey,
} from '@/api/services/studentSearch'
import { getStudentStatuses, setStudentStatus, type StudentStatusTag } from '@/api/services/studentStatuses'
import { getArchiveReasons, type ArchiveReason } from '@/api/services/archiveReasons'
import { getClasses } from '@/api/services/classes'
import {
  getCertificateTypes,
  getIssuingTeachers,
  type CertificateType,
  type IssuingTeacher,
} from '@/api/services/certificates'
import { genderLabels } from '@/config/constants'
import { formatDate, formatMoney, exportToCsv, cn } from '@/lib/utils'
import { useAuth } from '@/context/auth-context'
import { BILLING_ROLES } from '@/pages/admin/billing/access'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { StudentFormModal } from './StudentFormModal'
import { SmsModal } from './SmsModal'
import { AttachContractModal } from './AttachContractModal'
import { PaymentHistoryModal } from './PaymentHistoryModal'
import { ArchiveStudentsModal } from './ArchiveStudentsModal'
import { StudentListFilters } from './StudentListFilters'
import { StudentImportModal } from './StudentImportModal'
import { StudentCommentsModal } from './StudentCommentsModal'
import { StatusChip } from './StatusChip'

/**
 * O'quvchilar ro'yxati — docs/modules/students-parity.md §2.3 (S-1..S-7, K-4)
 * va §2.4 (A-1..A-3).
 *
 * FILTR VA TARTIB ENDI SERVERDA. Ilgari sahifa BUTUN registrni brauzerga
 * tortib, keyin uni JavaScript'da filtrlardi. Endi `GET students/search`
 * filtrni, tartibni va sahifani o'zi bajaradi; brauzerga faqat bitta sahifa
 * keladi. Eski `GET students` endpoint'i TEGILMAGAN — uni boshqa ekranlar
 * o'qiydi.
 *
 * FILTRSIZ KO'RINISH BUGUNGIDEK: hech qanday filtr qo'yilmasa server aynan
 * eski ro'yxatni, aynan eski tartibda qaytaradi (`StudentListQuery` izohi va
 * `StudentListTests`).
 *
 * TANLOV SAHIFADAN SAHIFAGA SAQLANADI: tanlangan qator obyekti bilan birga
 * eslab qolinadi, shuning uchun ommaviy amal AYNAN tanlanganlarga tegadi —
 * ko'rinmay qolgan qator ham, ko'rinib turgan-u tanlanmagan qator ham emas.
 */

type Tab = 'active' | 'archived'

/** Bir so'rovda keladigan qatorlar. Maktab hajmida ro'yxat odatda bitta sahifaga sig'adi. */
const PAGE_SIZE = 200

const EMPTY_PAGE: StudentListPage = {
  items: [],
  total: 0,
  page: 1,
  pageSize: PAGE_SIZE,
  totalDebt: 0,
  totalCredit: 0,
}

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

/**
 * Ro'yxat qatoridan tahrirlash formasi kutadigan obyekt. Formada yo'q
 * maydonlar (`parentPassportUrl`) `null` bo'lib ketadi va server `null` ni
 * "tegma" deb o'qiydi (StudentsController.Update) — ya'ni hech narsa
 * yo'qolmaydi.
 */
function toStudent(row: StudentListRow): Student {
  return {
    id: row.id,
    fullName: row.fullName,
    birthDate: row.birthDate,
    birthCertificateUrl: row.photoUrl,
    address: row.address,
    gender: row.gender,
    parentFullName: row.parentFullName,
    parentPhone: row.parentPhone,
    className: row.className,
    enrollmentDate: row.enrollmentDate,
    isArchived: row.isArchived,
    archivedAt: row.archivedAt,
    archiveReason: row.archiveReason,
    balance: row.balance,
  }
}

/**
 * Arxiv EduSchool'da alohida menyu yozuvi (`Arxiv o'quvchilar`), bizda esa
 * o'sha ro'yxatning tabi. Ikkovini bir joyda ushlab turish uchun sahifa
 * boshlang'ich tabni PROP orqali oladi: `/admin/students/arxiv` marshruti
 * shu bilan ochiladi, menyu esa o'z yozuvini yo'qotmaydi.
 */
export function StudentsPage({ initialTab = 'active' }: { initialTab?: Tab } = {}) {
  const { user } = useAuth()
  const navigate = useNavigate()

  const [tab, setTab] = useState<Tab>(initialTab)
  const [filter, setFilter] = useState<StudentListFilter>({})
  const [sortBy, setSortBy] = useState<StudentSortKey | undefined>(undefined)
  const [sortOrder, setSortOrder] = useState<'asc' | 'desc'>('asc')
  const [pageNo, setPageNo] = useState(1)
  const [refresh, setRefresh] = useState(0)

  const [page, setPage] = useState<StudentListPage>(EMPTY_PAGE)
  const [loading, setLoading] = useState(true)
  const [archivedTotal, setArchivedTotal] = useState(0)

  // Ma'lumotnomalar (filtr tanlovlari uchun).
  const [classNames, setClassNames] = useState<string[]>([])
  const [grades, setGrades] = useState<number[]>([])
  const [statuses, setStatuses] = useState<StudentStatusTag[]>([])
  const [archiveReasons, setArchiveReasons] = useState<ArchiveReason[]>([])
  const [certTypes, setCertTypes] = useState<CertificateType[]>([])
  const [certIssuers, setCertIssuers] = useState<IssuingTeacher[]>([])

  // Tanlov — qator obyekti bilan birga (sahifa almashsa ham yo'qolmaydi).
  const [selected, setSelected] = useState<Map<string, StudentListRow>>(new Map())

  // Modallar
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Student | null>(null)
  const [viewing, setViewing] = useState<Student | null>(null)
  /** S-6 — kim yuboradi: tanlangan qatorlar YOKI joriy filtrga mos HAMMASI. `null` = oyna yopiq. */
  const [smsMode, setSmsMode] = useState<'selected' | 'filter' | null>(null)
  /** K-5 — tanlangan qatorlarga bitta shartnoma raqami/sanasi biriktirish. */
  const [attachContractOpen, setAttachContractOpen] = useState(false)
  const [importOpen, setImportOpen] = useState(false)
  const [historyOf, setHistoryOf] = useState<Student | null>(null)
  const [commentsOf, setCommentsOf] = useState<StudentListRow | null>(null)
  const [archiveTargets, setArchiveTargets] = useState<Student[]>([])
  const [banner, setBanner] = useState<string | null>(null)

  /** Moliya roli — S-4 yakunlari (jami qarz / jami avans) faqat shu rolga ko'rinadi. */
  const canSeeTotals = !!user && BILLING_ROLES.includes(user.role)

  const effective: StudentListFilter = useMemo(
    () => ({
      ...filter,
      // "active" — serverning sukut holati, shuning uchun so'rovga qo'shilmaydi.
      state: tab === 'archived' ? 'archived' : undefined,
      sortBy,
      sortOrder: sortBy ? sortOrder : undefined,
      page: pageNo,
      pageSize: PAGE_SIZE,
    }),
    [filter, tab, sortBy, sortOrder, pageNo],
  )

  useEffect(() => {
    getClasses().then((cs) => {
      setClassNames(cs.map((c) => c.name))
      setGrades([...new Set(cs.map((c) => c.grade))].sort((a, b) => a - b))
    })
    getStudentStatuses().then(setStatuses)
    getArchiveReasons(true).then(setArchiveReasons)
    getCertificateTypes().then(setCertTypes)
    getIssuingTeachers().then(setCertIssuers)
  }, [])

  // Arxiv soni tab yorlig'ida turadi (bugungi ekrandagidek). Filtrga BOG'LIQ
  // EMAS — shuning uchun faqat ma'lumot o'zgarganda qayta so'raladi, har
  // harf bosilganda emas.
  useEffect(() => {
    searchStudents({ state: 'archived', pageSize: 1 }).then((p) => setArchivedTotal(p.total))
  }, [refresh])

  useEffect(() => {
    let alive = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi so'rovdan oldin "yuklanmoqda" holatini qo'yamiz (maqsadli)
    setLoading(true)
    // Matn maydonlari har bosishda so'rov yubormasin — qisqa kechikish.
    const timer = setTimeout(() => {
      searchStudents(effective)
        .then((result) => {
          if (alive) setPage(result)
        })
        .finally(() => {
          if (alive) setLoading(false)
        })
    }, 250)
    return () => {
      alive = false
      clearTimeout(timer)
    }
  }, [effective, refresh])

  // Filtr o'zgarsa birinchi sahifaga qaytamiz — aks holda "3-sahifa" bo'sh chiqardi.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda sahifani boshiga qaytaramiz (maqsadli)
    setPageNo(1)
  }, [filter, tab, sortBy, sortOrder])

  const rows = page.items
  const reload = () => setRefresh((v) => v + 1)

  /** Nechta filtr qo'yilgan — "Filtrlar" tugmasidagi son. */
  const activeCount = useMemo(
    () =>
      Object.entries(filter).filter(([, value]) => {
        if (value === undefined || value === null || value === '') return false
        if (Array.isArray(value)) return value.length > 0
        return true
      }).length,
    [filter],
  )

  const patchFilter = (patch: Partial<StudentListFilter>) =>
    setFilter((prev) => {
      const next = { ...prev, ...patch }
      // Bo'sh qiymatlar saqlanmaydi — "nechta filtr qo'yilgan" soni to'g'ri chiqsin.
      for (const key of Object.keys(next) as Array<keyof StudentListFilter>) {
        const value = next[key]
        if (value === undefined || value === '' || (Array.isArray(value) && value.length === 0))
          delete next[key]
      }
      return next
    })

  const selectedRows = [...selected.values()]
  const selectedStudents = selectedRows.map(toStudent)

  const allSelected = rows.length > 0 && rows.every((r) => selected.has(r.id))
  const someSelected = rows.some((r) => selected.has(r.id)) && !allSelected
  const headerCbRef = useRef<HTMLInputElement>(null)
  useEffect(() => {
    if (headerCbRef.current) headerCbRef.current.indeterminate = someSelected
  })

  const toggleOne = (row: StudentListRow) =>
    setSelected((prev) => {
      const next = new Map(prev)
      if (next.has(row.id)) next.delete(row.id)
      else next.set(row.id, row)
      return next
    })

  const toggleAll = () =>
    setSelected((prev) => {
      const next = new Map(prev)
      if (allSelected) rows.forEach((r) => next.delete(r.id))
      else rows.forEach((r) => next.set(r.id, r))
      return next
    })

  const clearSelection = () => setSelected(new Map())

  const dropFromSelection = (ids: string[]) =>
    setSelected((prev) => {
      const next = new Map(prev)
      ids.forEach((id) => next.delete(id))
      return next
    })

  const sortOn = (key: StudentSortKey) => {
    if (sortBy === key) {
      setSortOrder((o) => (o === 'asc' ? 'desc' : 'asc'))
      return
    }
    setSortBy(key)
    setSortOrder('asc')
  }

  const handleCsv = () => {
    exportToCsv(
      'oquvchilar.csv',
      ['F.I.SH', 'Sinf', 'Jinsi', "Tug'ilgan kun", 'Manzil', 'Ota-ona', 'Telefon', 'Holat', 'Balans'],
      selectedRows.map((s) => [
        s.fullName,
        s.className,
        genderLabels[s.gender],
        formatDate(s.birthDate),
        s.address,
        s.parentFullName,
        s.parentPhone,
        s.statusName ?? '',
        formatMoney(s.balance),
      ]),
    )
  }

  const handleFormSubmit = (values: StudentPayload) => {
    if (editing) {
      updateStudent(editing.id, values).then(reload)
    } else {
      createStudent(values).then((created) => {
        setViewing(created)
        reload()
      })
    }
    setFormOpen(false)
    setEditing(null)
  }

  const handleDelete = (row: StudentListRow) => {
    if (!confirm(`"${row.fullName}" o'quvchini BUTUNLAY o'chirishni tasdiqlaysizmi? Bu amal qaytarib bo'lmaydi.`))
      return
    deleteStudent(row.id)
      .then(() => {
        dropFromSelection([row.id])
        reload()
      })
      .catch((err) => alert(errorText(err, "O'chirib bo'lmadi")))
  }

  /** S-7 — tanlanganlarni butunlay o'chirish (arxiv tab'i). Hammasi yoki hech nima. */
  const handleDeleteMany = async () => {
    const ids = selectedRows.map((r) => r.id)
    if (ids.length === 0) return
    if (!confirm(`${ids.length} ta o'quvchi BUTUNLAY o'chiriladi. Bu amal qaytarib bo'lmaydi. Davom etamizmi?`))
      return
    try {
      const result = await deleteStudentsMany(ids)
      dropFromSelection(ids)
      setBanner(`${result.deleted} ta o'quvchi o'chirildi`)
      reload()
    } catch (err) {
      const data = (err as { response?: { data?: { blocked?: Array<{ fullName: string }>; message?: string } } })
        ?.response?.data
      const names = (data?.blocked ?? []).map((b) => b.fullName).join(', ')
      alert(
        (data?.message ?? "O'chirib bo'lmadi") + (names ? `\n\nTo'sganlar: ${names}` : ''),
      )
    }
  }

  const handleArchived = (ids: string[]) => {
    dropFromSelection(ids)
    setArchiveTargets([])
    reload()
  }

  const handleRestore = (row: StudentListRow) => {
    if (!confirm(`"${row.fullName}" o'quvchini arxivdan qaytarish? Login bloklangicha qoladi — keyin parol generatsiya qiling.`))
      return
    restoreStudent(row.id).then(() => {
      dropFromSelection([row.id])
      reload()
    })
  }

  /** S-5 — ro'yxatdan turib holat almashtirish (EduSchool'dagi inline tanlov). */
  const changeStatus = async (row: StudentListRow, statusId: string) => {
    const previous = page
    // Optimistik: nishon darrov almashadi, xato bo'lsa orqaga qaytariladi.
    const chosen = statuses.find((s) => s.id === statusId) ?? null
    setPage((p) => ({
      ...p,
      items: p.items.map((r) =>
        r.id === row.id
          ? { ...r, statusId: chosen?.id ?? null, statusName: chosen?.name ?? null, statusColor: chosen?.color ?? null }
          : r,
      ),
    }))
    try {
      await setStudentStatus(row.id, statusId || null)
    } catch (err) {
      setPage(previous)
      alert(errorText(err, "Holatni o'zgartirib bo'lmadi"))
    }
  }

  const totalPages = Math.max(1, Math.ceil(page.total / (page.pageSize || PAGE_SIZE)))

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">O'quvchilar</h1>
          <p className="text-sm text-slate-400">
            {tab === 'active'
              ? `Topildi: ${page.total} ta · Arxivda: ${archivedTotal} ta`
              : `Arxivda: ${page.total} ta o'quvchi`}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <div className="flex gap-1 rounded-lg bg-slate-100 p-1">
            <button
              type="button"
              onClick={() => {
                setTab('active')
                clearSelection()
              }}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                tab === 'active' ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
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
                tab === 'archived' ? 'bg-white text-amber-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              <Archive className="mr-1 inline h-4 w-4" />
              Arxiv ({archivedTotal})
            </button>
          </div>

          {user?.role === 'superadmin' && (
            <Button variant="secondary" onClick={() => downloadStudentCredentials()}>
              <Download className="h-4 w-4" /> Login/parollar
            </Button>
          )}

          <Button variant="secondary" onClick={() => exportStudents({ ...effective, page: undefined, pageSize: undefined })}>
            <FileSpreadsheet className="h-4 w-4" /> Eksport
          </Button>

          {/* S-6 — joriy filtrga mos BARCHA o'quvchiga, tanlovdan mustaqil. */}
          <Button variant="secondary" onClick={() => setSmsMode('filter')} disabled={page.total === 0}>
            <Send className="h-4 w-4" /> Filtrlanganlarga xabar
          </Button>

          {tab === 'active' && (
            <>
              <Button variant="secondary" onClick={() => setImportOpen(true)}>
                <Upload className="h-4 w-4" /> Excel yuklash
              </Button>
              <Button
                onClick={() => {
                  setEditing(null)
                  setFormOpen(true)
                }}
              >
                <Plus className="h-4 w-4" /> Yangi qo'shish
              </Button>
            </>
          )}
        </div>
      </div>

      {banner && (
        <div className="flex items-center gap-2 rounded-xl bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
          {banner}
          <button type="button" onClick={() => setBanner(null)} className="ml-auto text-emerald-600">
            <X className="h-4 w-4" />
          </button>
        </div>
      )}

      <Card className="p-0">
        <StudentListFilters
          filter={filter}
          onChange={patchFilter}
          onReset={() => setFilter({})}
          archived={tab === 'archived'}
          classNames={classNames}
          grades={grades}
          statuses={statuses}
          archiveReasons={archiveReasons}
          certTypes={certTypes}
          certIssuers={certIssuers}
          activeCount={activeCount}
        />

        {/* Tanlanganlar uchun amal paneli */}
        {selected.size > 0 && (
          <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 bg-brand-50/60 px-4 py-3">
            <span className="text-sm font-medium text-brand-700">{selected.size} ta tanlandi</span>
            <Button variant="secondary" onClick={() => setSmsMode('selected')}>
              <Send className="h-4 w-4" /> Xabar yuborish
            </Button>
            <Button variant="secondary" onClick={handleCsv}>
              <Download className="h-4 w-4" /> Yuklab olish (CSV)
            </Button>
            {tab === 'active' && (
              <>
                <Button variant="secondary" onClick={() => setAttachContractOpen(true)}>
                  <FileSignature className="h-4 w-4" /> Shartnoma biriktirish
                </Button>
                <Button variant="secondary" onClick={() => setArchiveTargets(selectedStudents)}>
                  <Archive className="h-4 w-4" /> Arxivlash
                </Button>
              </>
            )}
            {tab === 'archived' && (
              <Button variant="danger" onClick={handleDeleteMany}>
                <Trash2 className="h-4 w-4" /> Butunlay o'chirish
              </Button>
            )}
            <button
              onClick={clearSelection}
              className="ml-auto inline-flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700"
            >
              <X className="h-4 w-4" /> Bekor qilish
            </button>
          </div>
        )}

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
                  <SortHeader label="F.I.SH" sortKey="fullName" active={sortBy} order={sortOrder} onSort={sortOn} />
                  <SortHeader label="Sinf" sortKey="className" active={sortBy} order={sortOrder} onSort={sortOn} />
                  <th className="px-4 py-3">Jinsi</th>
                  <SortHeader label="Tug'ilgan kun" sortKey="birthDate" active={sortBy} order={sortOrder} onSort={sortOn} />
                  <th className="px-4 py-3">Telefon</th>
                  <th className="px-4 py-3">Ota-ona</th>
                  <th className="px-4 py-3">Ota-ona tel.</th>
                  <SortHeader label="Holat" sortKey="status" active={sortBy} order={sortOrder} onSort={sortOn} />
                  <SortHeader label="Balans" sortKey="balance" active={sortBy} order={sortOrder} onSort={sortOn} />
                  {tab === 'archived' && (
                    <SortHeader label="Arxiv sanasi" sortKey="archivedAt" active={sortBy} order={sortOrder} onSort={sortOn} />
                  )}
                  {tab === 'archived' && <th className="px-4 py-3">Sabab</th>}
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((s, i) => (
                  <tr
                    key={s.id}
                    onClick={() => navigate(`/admin/students/${s.id}`)}
                    title="Shaxsiy daftarni ochish"
                    className="cursor-pointer hover:bg-slate-50/60"
                  >
                    <td className="px-4 py-3" onClick={(e) => e.stopPropagation()}>
                      <input
                        type="checkbox"
                        checked={selected.has(s.id)}
                        onChange={() => toggleOne(s)}
                        className="h-4 w-4 accent-brand-600"
                      />
                    </td>
                    <td className="px-2 py-3 text-slate-400">{(page.page - 1) * page.pageSize + i + 1}</td>
                    <td className="px-4 py-3 font-medium text-slate-800">{s.fullName}</td>
                    <td className="px-4 py-3">
                      <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                        {s.className}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-slate-600">{genderLabels[s.gender]}</td>
                    <td className="px-4 py-3 text-slate-600">
                      {formatDate(s.birthDate)}
                      {s.age !== null && <span className="ml-1 text-xs text-slate-400">({s.age})</span>}
                    </td>
                    <td className="px-4 py-3 text-slate-600">{s.phone ?? '—'}</td>
                    <td className="px-4 py-3 text-slate-600">{s.parentFullName}</td>
                    <td className="px-4 py-3 text-slate-600">{s.parentPhone}</td>
                    <td className="px-4 py-3" onClick={(e) => e.stopPropagation()}>
                      {statuses.length === 0 ? (
                        <span className="text-slate-300">—</span>
                      ) : (
                        <div className="relative inline-flex items-center">
                          {s.statusName ? (
                            <StatusChip name={s.statusName} color={s.statusColor} />
                          ) : (
                            <span className="rounded-md border border-dashed border-slate-200 px-2 py-0.5 text-xs text-slate-400">
                              Holat yo'q
                            </span>
                          )}
                          {/* Ko'rinmas select — nishonning o'zi bosiladi (EduSchool'dagidek). */}
                          <select
                            value={s.statusId ?? ''}
                            onChange={(e) => changeStatus(s, e.target.value)}
                            title="Holatni o'zgartirish"
                            className="absolute inset-0 cursor-pointer opacity-0"
                          >
                            <option value="">Holat yo'q</option>
                            {statuses.map((st) => (
                              <option key={st.id} value={st.id}>
                                {st.name}
                              </option>
                            ))}
                          </select>
                        </div>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'font-medium',
                          s.balance < 0 ? 'text-red-600' : s.balance > 0 ? 'text-emerald-600' : 'text-slate-500',
                        )}
                      >
                        {s.balance > 0 ? `+${formatMoney(s.balance)}` : formatMoney(s.balance)}
                      </span>
                    </td>
                    {tab === 'archived' && (
                      <td className="px-4 py-3 text-slate-600">
                        {s.archivedAt ? formatDate(s.archivedAt) : '—'}
                      </td>
                    )}
                    {tab === 'archived' && (
                      <td
                        className="max-w-[18rem] truncate px-4 py-3 text-slate-600"
                        title={s.archiveReason ?? ''}
                      >
                        {s.archiveReason || '—'}
                      </td>
                    )}
                    <td className="px-4 py-3" onClick={(e) => e.stopPropagation()}>
                      <div className="flex items-center justify-end gap-0.5">
                        <IconBtn icon={MessageSquare} title="Izohlar" onClick={() => setCommentsOf(s)} />
                        {tab === 'active' ? (
                          <>
                            <IconBtn icon={History} title="To'lov tarixi" onClick={() => setHistoryOf(toStudent(s))} />
                            <IconBtn
                              icon={Pencil}
                              title="Tahrirlash"
                              onClick={() => {
                                setEditing(toStudent(s))
                                setFormOpen(true)
                              }}
                            />
                            <IconBtn
                              icon={Archive}
                              title="Arxivga ko'chirish"
                              onClick={() => setArchiveTargets([toStudent(s)])}
                            />
                          </>
                        ) : (
                          <>
                            <IconBtn icon={RotateCcw} title="Arxivdan qaytarish" onClick={() => handleRestore(s)} />
                            <IconBtn icon={Trash2} title="Butunlay o'chirish" danger onClick={() => handleDelete(s)} />
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
                {rows.length === 0 && (
                  <tr>
                    <td colSpan={tab === 'archived' ? 14 : 12} className="px-4 py-12 text-center text-slate-400">
                      {tab === 'archived' ? "Arxivda o'quvchi yo'q" : 'Hech narsa topilmadi'}
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}

        {/* Yakun va sahifalar */}
        {!loading && page.total > 0 && (
          <div className="flex flex-wrap items-center gap-4 border-t border-slate-100 px-4 py-3 text-sm">
            {canSeeTotals && (
              <>
                <span className="text-slate-500">
                  Jami qarz: <b className="text-red-600">{formatMoney(page.totalDebt)}</b>
                </span>
                <span className="text-slate-500">
                  Jami avans: <b className="text-emerald-600">{formatMoney(page.totalCredit)}</b>
                </span>
              </>
            )}
            {totalPages > 1 && (
              <div className="ml-auto flex items-center gap-2">
                <Button
                  variant="secondary"
                  disabled={page.page <= 1}
                  onClick={() => setPageNo((p) => Math.max(1, p - 1))}
                >
                  Oldingi
                </Button>
                <span className="text-slate-500">
                  {page.page} / {totalPages}
                </span>
                <Button
                  variant="secondary"
                  disabled={page.page >= totalPages}
                  onClick={() => setPageNo((p) => Math.min(totalPages, p + 1))}
                >
                  Keyingi
                </Button>
              </div>
            )}
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
      <SmsModal
        open={smsMode !== null}
        onClose={() => setSmsMode(null)}
        recipients={smsMode === 'selected' ? selectedStudents : []}
        filterScope={
          smsMode === 'filter'
            ? { filter: { ...effective, page: undefined, pageSize: undefined }, total: page.total }
            : undefined
        }
      />
      <AttachContractModal
        open={attachContractOpen}
        onClose={() => setAttachContractOpen(false)}
        students={selectedRows}
        onDone={() => {
          setAttachContractOpen(false)
          clearSelection()
          reload()
        }}
      />
      <PaymentHistoryModal student={historyOf} onClose={() => setHistoryOf(null)} />
      <ArchiveStudentsModal
        students={archiveTargets}
        onClose={() => setArchiveTargets([])}
        onArchived={handleArchived}
      />
      <StudentImportModal
        open={importOpen}
        onClose={() => setImportOpen(false)}
        onImported={(created, updated) => {
          setBanner(`Excel'dan yuklandi: ${created} ta yangi, ${updated} ta yangilandi`)
          reload()
        }}
      />
      <StudentCommentsModal
        studentId={commentsOf?.id ?? null}
        studentName={commentsOf?.fullName ?? ''}
        onClose={() => setCommentsOf(null)}
      />
    </div>
  )
}

interface SortHeaderProps {
  label: string
  sortKey: StudentSortKey
  active: StudentSortKey | undefined
  order: 'asc' | 'desc'
  onSort: (key: StudentSortKey) => void
}

function SortHeader({ label, sortKey, active, order, onSort }: SortHeaderProps) {
  const on = active === sortKey
  return (
    <th className="px-4 py-3">
      <button
        type="button"
        onClick={() => onSort(sortKey)}
        className={cn(
          'inline-flex items-center gap-1 uppercase tracking-wide transition-colors',
          on ? 'text-brand-600' : 'hover:text-slate-600',
        )}
      >
        {label}
        {on && (order === 'asc' ? <ChevronUp className="h-3 w-3" /> : <ChevronDown className="h-3 w-3" />)}
      </button>
    </th>
  )
}

interface IconBtnProps {
  icon: typeof Pencil
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
