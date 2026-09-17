import { useCallback, useEffect, useState } from 'react'
import { Download, FileSignature, Pencil, Plus, Search, Trash2, Wand2 } from 'lucide-react'
import type { SchoolClass } from '@/types'
import { getClasses } from '@/api/services/classes'
import type {
  SaveStudentContractInput,
  StudentContract,
  StudentContractSource,
  StudentContractStatus,
} from '@/api/services/studentContracts'
import {
  contractSourceLabels,
  contractStatusLabels,
  createStudentContract,
  deleteStudentContract,
  searchStudentContracts,
  updateStudentContract,
} from '@/api/services/studentContracts'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input, Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { GenerateContractModal } from './GenerateContractModal'
import { StudentContractFormModal } from './StudentContractFormModal'

/**
 * O'quvchi shartnomalari reyestri — docs/modules/students-parity.md §2.10 (K-1).
 * "Shartnomalar" ekranining uchinchi tab'i.
 *
 * Bu tab MAVJUD ikki tab'ga tegmaydi: u yerda andoza yuklanadi va Telegram
 * orqali yuboriladi, bu yerda esa tuzilgan shartnomalar ro'yxati turadi.
 */

const PAGE_SIZE = 50

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

const statusStyles: Record<StudentContractStatus, string> = {
  draft: 'bg-slate-100 text-slate-600',
  active: 'bg-emerald-50 text-emerald-700',
  expired: 'bg-amber-50 text-amber-700',
}

export function StudentContractsTab() {
  const [rows, setRows] = useState<StudentContract[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)
  const [classes, setClasses] = useState<SchoolClass[]>([])

  const [search, setSearch] = useState('')
  const [className, setClassName] = useState('')
  const [status, setStatus] = useState<'' | StudentContractStatus>('')
  const [source, setSource] = useState<'' | StudentContractSource>('')
  const [hasFile, setHasFile] = useState<'' | 'yes' | 'no'>('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<StudentContract | null>(null)
  const [generateFor, setGenerateFor] = useState<{ id: string; fullName: string } | null>(null)

  useEffect(() => {
    getClasses().then(setClasses).catch(() => setClasses([]))
  }, [])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await searchStudentContracts({
        search: search.trim() || undefined,
        className: className || undefined,
        status: status || undefined,
        source: source || undefined,
        hasFile: hasFile === '' ? undefined : hasFile === 'yes',
        from: from || undefined,
        to: to || undefined,
        page,
        pageSize: PAGE_SIZE,
      })
      setRows(data.items)
      setTotal(data.total)
    } finally {
      setLoading(false)
    }
  }, [search, className, status, source, hasFile, from, to, page])

  // Debounce — setState taymer ichida, ya'ni effekt tanasida emas.
  useEffect(() => {
    const timer = setTimeout(load, 250)
    return () => clearTimeout(timer)
  }, [load])

  // Filtr o'zgarganda birinchi sahifaga qaytamiz — aks holda bo'sh sahifada
  // qolib ketish mumkin edi.
  const resetPage = () => setPage(1)

  const submitForm = async (input: SaveStudentContractInput) => {
    if (editing) await updateStudentContract(editing.id, input)
    else await createStudentContract(input)
    setFormOpen(false)
    await load()
  }

  const remove = async (row: StudentContract) => {
    if (!confirm(`${row.studentName} — ${row.number ?? 'raqamsiz'} shartnomasini o'chirasizmi?`))
      return
    try {
      await deleteStudentContract(row.id)
      setRows((prev) => prev.filter((r) => r.id !== row.id))
      setTotal((t) => t - 1)
    } catch (err) {
      alert(errorText(err, "O'chirib bo'lmadi"))
    }
  }

  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  return (
    <div className="space-y-4">
      {/* Filtrlar */}
      <Card className="grid gap-3 md:grid-cols-4">
        <div className="relative">
          <Search className="absolute left-3 top-9 h-4 w-4 text-slate-400" />
          <Input
            label="Qidirish"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value)
              resetPage()
            }}
            placeholder="O'quvchi yoki raqam"
            className="pl-9"
          />
        </div>

        <Select
          label="Sinf"
          value={className}
          onChange={(e) => {
            setClassName(e.target.value)
            resetPage()
          }}
        >
          <option value="">Barchasi</option>
          {classes.map((c) => (
            <option key={c.id} value={c.name}>
              {c.name}
            </option>
          ))}
        </Select>

        <Select
          label="Holati"
          value={status}
          onChange={(e) => {
            setStatus(e.target.value as '' | StudentContractStatus)
            resetPage()
          }}
        >
          <option value="">Barchasi</option>
          <option value="active">{contractStatusLabels.active}</option>
          <option value="expired">{contractStatusLabels.expired}</option>
          <option value="draft">{contractStatusLabels.draft}</option>
        </Select>

        <Select
          label="Fayl"
          value={hasFile}
          onChange={(e) => {
            setHasFile(e.target.value as '' | 'yes' | 'no')
            resetPage()
          }}
        >
          <option value="">Farqi yo'q</option>
          <option value="yes">Fayli bor</option>
          <option value="no">Faylsiz</option>
        </Select>

        <Select
          label="Manba"
          value={source}
          onChange={(e) => {
            setSource(e.target.value as '' | StudentContractSource)
            resetPage()
          }}
        >
          <option value="">Barchasi</option>
          <option value="generated">{contractSourceLabels.generated}</option>
          <option value="uploaded">{contractSourceLabels.uploaded}</option>
        </Select>

        <Input
          label="Imzo sanasi (dan)"
          type="date"
          value={from}
          onChange={(e) => {
            setFrom(e.target.value)
            resetPage()
          }}
        />
        <Input
          label="Imzo sanasi (gacha)"
          type="date"
          value={to}
          onChange={(e) => {
            setTo(e.target.value)
            resetPage()
          }}
        />
      </Card>

      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 p-4">
          <p className="text-sm text-slate-500">{total} ta shartnoma</p>
          <Button
            onClick={() => {
              setEditing(null)
              setFormOpen(true)
            }}
          >
            <Plus className="h-4 w-4" /> Yangi yozuv
          </Button>
        </div>

        {loading && rows.length === 0 ? (
          <div className="p-6">
            <Loader label="Yuklanmoqda..." />
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Raqam</th>
                  <th className="px-4 py-3">O'quvchi</th>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3">Imzo sanasi</th>
                  <th className="px-4 py-3">Tugash sanasi</th>
                  <th className="px-4 py-3">Holati</th>
                  <th className="px-4 py-3">Manba</th>
                  <th className="w-28 px-4 py-3" />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => (
                  <tr key={row.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 font-medium text-slate-800">
                      {row.number ? `№ ${row.number}` : '—'}
                    </td>
                    <td className="px-4 py-3 text-slate-700">{row.studentName}</td>
                    <td className="px-4 py-3 text-slate-500">{row.className || '—'}</td>
                    <td className="px-4 py-3 text-slate-500">{row.signedOn ?? '—'}</td>
                    <td className="px-4 py-3 text-slate-500">{row.endsOn ?? '—'}</td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'rounded px-2 py-0.5 text-xs font-medium',
                          statusStyles[row.status],
                        )}
                      >
                        {contractStatusLabels[row.status]}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-xs text-slate-500">
                      {contractSourceLabels[row.source]}
                      {row.templateName ? ` · ${row.templateName}` : ''}
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex justify-end gap-1">
                        {row.fileUrl && (
                          <a
                            href={row.fileUrl}
                            target="_blank"
                            rel="noreferrer"
                            title="Yuklab olish"
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-brand-600"
                          >
                            <Download className="h-4 w-4" />
                          </a>
                        )}
                        <button
                          type="button"
                          title="Andozadan yangi shartnoma"
                          onClick={() =>
                            setGenerateFor({ id: row.studentId, fullName: row.studentName })
                          }
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                        >
                          <Wand2 className="h-4 w-4" />
                        </button>
                        <button
                          type="button"
                          title="Tahrirlash"
                          onClick={() => {
                            setEditing(row)
                            setFormOpen(true)
                          }}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                        >
                          <Pencil className="h-4 w-4" />
                        </button>
                        <button
                          type="button"
                          title="O'chirish"
                          onClick={() => remove(row)}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                        >
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
                {rows.length === 0 && (
                  <tr>
                    <td colSpan={8} className="px-4 py-12 text-center text-slate-400">
                      <FileSignature className="mx-auto mb-2 h-6 w-6 text-slate-300" />
                      Shartnoma topilmadi
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}

        {pages > 1 && (
          <div className="flex items-center justify-between border-t border-slate-100 px-4 py-3">
            <Button
              variant="secondary"
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page <= 1}
            >
              Oldingi
            </Button>
            <span className="text-sm text-slate-500">
              {page} / {pages}
            </span>
            <Button
              variant="secondary"
              onClick={() => setPage((p) => Math.min(pages, p + 1))}
              disabled={page >= pages}
            >
              Keyingi
            </Button>
          </div>
        )}
      </Card>

      <StudentContractFormModal
        open={formOpen}
        contract={editing}
        onClose={() => setFormOpen(false)}
        onSubmit={submitForm}
      />
      <GenerateContractModal
        open={generateFor !== null}
        studentId={generateFor?.id ?? null}
        studentName={generateFor?.fullName ?? ''}
        onClose={() => setGenerateFor(null)}
        onCreated={() => {
          setGenerateFor(null)
          load()
        }}
      />
    </div>
  )
}
