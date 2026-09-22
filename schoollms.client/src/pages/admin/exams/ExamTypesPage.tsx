/**
 * IMTIHON TURI — the exam-type catalogue
 * (`docs/modules/admission-and-testing.md` §3.2 screen 7, §5.4, §6.3; unit C3).
 *
 * A flat list: nomi, izoh, faolmi. EduSchool's own screen was not in the
 * bundle (§0), so the columns are §5.4's — the shape of every other catalogue
 * we have (`EvaluationType`, `AssignmentType`).
 *
 * DELETE vs DEACTIVATE. A type that an exam uses cannot be deleted (409, the
 * FK is `set null` but the server refuses to orphan a label silently). The
 * row therefore always offers "faolsizlantirish" next to "o'chirish", and the
 * 409 sentence tells the user to use it.
 *
 * PERMISSION. The route is `exams`; reads are not gated (§4.4). Without the
 * `exams` key no write control is rendered at all — a button that always 403s
 * is worse than no button (§3.7).
 */
import { useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { Loader2, Pencil, Plus, Power, RefreshCw, RotateCcw, Search, Tags, Trash2 } from 'lucide-react'
import {
  createExamType,
  deleteExamType,
  examsErrorMessage,
  listExamTypes,
  updateExamType,
  type ExamType,
} from '@/api/services/exams'
import { useAsync } from '@/hooks/useAsync'
import { useAuth } from '@/context/auth-context'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input, Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Toast } from '@/components/ui/Toast'
import { cn } from '@/lib/utils'
import { DASH, control, hasPerm } from './examLabels'

interface Notice {
  message: string
  tone: 'success' | 'error'
}

/** The catalogue is small and unpaged (§6.3 returns a bare array). */
async function loadTypes(): Promise<ExamType[]> {
  try {
    return await listExamTypes()
  } catch (err) {
    throw new Error(examsErrorMessage(err, 'types.load'), { cause: err })
  }
}

export function ExamTypesPage() {
  const { user } = useAuth()
  const canWrite = hasPerm(user?.permissions, 'exams')

  const types = useAsync(loadTypes, [])
  const [search, setSearch] = useState('')
  const [showInactive, setShowInactive] = useState(false)

  const [editing, setEditing] = useState<ExamType | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [notice, setNotice] = useState<Notice | null>(null)

  const all = useMemo(() => types.data ?? [], [types.data])
  const inactiveCount = all.filter((t) => !t.isActive).length

  const rows = useMemo(() => {
    const needle = search.trim().toLowerCase()
    return all
      .filter((t) => showInactive || t.isActive)
      .filter(
        (t) =>
          !needle ||
          t.name.toLowerCase().includes(needle) ||
          t.description.toLowerCase().includes(needle),
      )
      .sort((a, b) => a.name.localeCompare(b.name, 'uz'))
  }, [all, search, showInactive])

  const filtersActive = Boolean(search.trim())

  const openCreate = () => {
    setEditing(null)
    setFormOpen(true)
  }

  const openEdit = (row: ExamType) => {
    setEditing(row)
    setFormOpen(true)
  }

  const toggleActive = async (row: ExamType) => {
    setBusyId(row.id)
    try {
      await updateExamType(row.id, {
        name: row.name,
        description: row.description,
        isActive: !row.isActive,
      })
      setNotice({
        message: row.isActive
          ? `"${row.name}" faolsizlantirildi — yangi imtihonda tanlanmaydi`
          : `"${row.name}" qayta faollashtirildi`,
        tone: 'success',
      })
      types.refetch()
    } catch (err) {
      setNotice({ message: examsErrorMessage(err, 'types.save'), tone: 'error' })
    } finally {
      setBusyId(null)
    }
  }

  const remove = async (row: ExamType) => {
    if (!confirm(`"${row.name}" imtihon turini o'chirasizmi?`)) return
    setBusyId(row.id)
    try {
      await deleteExamType(row.id)
      setNotice({ message: `"${row.name}" o'chirildi`, tone: 'success' })
      types.refetch()
    } catch (err) {
      setNotice({ message: examsErrorMessage(err, 'types.delete'), tone: 'error' })
    } finally {
      setBusyId(null)
    }
  }

  const onSaved = (saved: ExamType, created: boolean) => {
    setFormOpen(false)
    setEditing(null)
    setNotice({
      message: created ? `"${saved.name}" qo'shildi` : `"${saved.name}" saqlandi`,
      tone: 'success',
    })
    types.refetch()
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Imtihon turlari</h1>
          <p className="text-sm text-slate-400">
            Imtihonlarni guruhlash uchun: masalan, "Choraklik blok test", "Oylik nazorat"
            {all.length > 0 && ` · ${all.length} ta tur`}
          </p>
        </div>
        {canWrite && (
          <Button onClick={openCreate}>
            <Plus className="h-4 w-4" /> Yangi tur
          </Button>
        )}
      </div>

      {!canWrite && (
        <p className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-500">
          Sizda "Imtihonlar" ruxsati yo'q — ro'yxat faqat ko'rish uchun ochiq.
        </p>
      )}

      {types.loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : types.error ? (
        <Card className="flex flex-col items-center justify-center gap-3 py-16 text-center">
          <p className="text-sm font-medium text-slate-600">Imtihon turlarini ochib bo'lmadi</p>
          <p className="max-w-md text-sm text-slate-400">{types.error}</p>
          <Button variant="secondary" onClick={types.refetch}>
            <RefreshCw className="h-4 w-4" /> Qayta urinish
          </Button>
        </Card>
      ) : all.length === 0 ? (
        <Card className="flex flex-col items-center justify-center gap-3 py-16 text-center">
          <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100">
            <Tags className="h-7 w-7 text-slate-400" />
          </div>
          <p className="text-sm font-medium text-slate-600">Hali birorta imtihon turi yo'q</p>
          <p className="max-w-md text-sm text-slate-400">
            Tur majburiy emas, lekin natijalarni saralash va solishtirishni osonlashtiradi.
          </p>
          {canWrite && (
            <Button onClick={openCreate}>
              <Plus className="h-4 w-4" /> Birinchi turni qo'shish
            </Button>
          )}
        </Card>
      ) : (
        <Card className="p-0">
          <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
            <div className="relative min-w-[200px] flex-1">
              <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
              <input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Nomi yoki izohi..."
                aria-label="Qidiruv"
                className={cn(control, 'w-full pl-9')}
              />
            </div>
            <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-600">
              <input
                type="checkbox"
                checked={showInactive}
                onChange={(e) => setShowInactive(e.target.checked)}
                className="h-4 w-4 rounded border-slate-300 accent-brand-600"
              />
              Faol emaslarini ko'rsatish
              {inactiveCount > 0 && <span className="text-slate-400">({inactiveCount})</span>}
            </label>
          </div>

          {rows.length === 0 ? (
            <div className="flex flex-col items-center gap-2 px-4 py-14 text-center">
              <Tags className="h-8 w-8 text-slate-300" />
              <p className="font-medium text-slate-600">
                {filtersActive ? "Qidiruv bo'yicha tur topilmadi" : "Faol imtihon turi yo'q"}
              </p>
              {filtersActive ? (
                <button
                  type="button"
                  onClick={() => setSearch('')}
                  className="mt-1 inline-flex items-center gap-1.5 text-sm font-medium text-brand-600 transition-colors hover:text-brand-700"
                >
                  <RotateCcw className="h-3.5 w-3.5" /> Qidiruvni tozalash
                </button>
              ) : (
                <button
                  type="button"
                  onClick={() => setShowInactive(true)}
                  className="mt-1 text-sm font-medium text-brand-600 transition-colors hover:text-brand-700"
                >
                  Faol emaslarini ko'rsatish
                </button>
              )}
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="w-10 px-4 py-3">#</th>
                    <th className="px-4 py-3">Nomi</th>
                    <th className="px-4 py-3">Izoh</th>
                    <th className="px-4 py-3">Holati</th>
                    {canWrite && <th className="px-4 py-3 text-right">Amallar</th>}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {rows.map((row, i) => (
                    <tr key={row.id} className="hover:bg-slate-50/60">
                      <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                      <td className="px-4 py-3">
                        <span
                          className="block max-w-[16rem] truncate font-medium text-slate-800"
                          title={row.name}
                        >
                          {row.name}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-slate-500">
                        {row.description.trim() ? (
                          <span className="block max-w-[28rem] truncate" title={row.description}>
                            {row.description}
                          </span>
                        ) : (
                          <span className="text-slate-300">{DASH}</span>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        <span
                          className={cn(
                            'rounded-full px-2 py-0.5 text-xs font-medium',
                            row.isActive
                              ? 'bg-emerald-50 text-emerald-700'
                              : 'bg-slate-100 text-slate-500',
                          )}
                        >
                          {row.isActive ? 'Faol' : 'Faol emas'}
                        </span>
                      </td>
                      {canWrite && (
                        <td className="px-4 py-3">
                          <div className="flex items-center justify-end gap-0.5">
                            <button
                              type="button"
                              title="Tahrirlash"
                              aria-label="Tahrirlash"
                              onClick={() => openEdit(row)}
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                            >
                              <Pencil className="h-4 w-4" />
                            </button>
                            <button
                              type="button"
                              disabled={busyId === row.id}
                              title={row.isActive ? 'Faolsizlantirish' : 'Qayta faollashtirish'}
                              aria-label={row.isActive ? 'Faolsizlantirish' : 'Qayta faollashtirish'}
                              onClick={() => void toggleActive(row)}
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:text-slate-200"
                            >
                              {row.isActive ? (
                                <Power className="h-4 w-4" />
                              ) : (
                                <RotateCcw className="h-4 w-4" />
                              )}
                            </button>
                            <button
                              type="button"
                              disabled={busyId === row.id}
                              title="O'chirish"
                              aria-label="O'chirish"
                              onClick={() => void remove(row)}
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600 disabled:cursor-not-allowed disabled:text-slate-200 disabled:hover:bg-transparent"
                            >
                              <Trash2 className="h-4 w-4" />
                            </button>
                          </div>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      )}

      {formOpen && canWrite && (
        <ExamTypeFormModal
          editing={editing}
          onClose={() => {
            setFormOpen(false)
            setEditing(null)
          }}
          onSaved={onSaved}
        />
      )}

      <Toast
        message={notice?.message ?? null}
        tone={notice?.tone ?? 'success'}
        onClose={() => setNotice(null)}
      />
    </div>
  )
}

interface FormProps {
  /** `null` — a new type. */
  editing: ExamType | null
  onClose: () => void
  onSaved: (saved: ExamType, created: boolean) => void
}

/** Mounted per open (conditional render), so the initial values live in `useState`. */
function ExamTypeFormModal({ editing, onClose, onSaved }: FormProps) {
  const [name, setName] = useState(editing?.name ?? '')
  const [description, setDescription] = useState(editing?.description ?? '')
  const [isActive, setIsActive] = useState(editing?.isActive ?? true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const canSave = Boolean(name.trim()) && !saving

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!canSave) return
    setSaving(true)
    setError(null)
    try {
      const saved = editing
        ? await updateExamType(editing.id, {
            name: name.trim(),
            description: description.trim(),
            isActive,
          })
        : await createExamType({ name: name.trim(), description: description.trim() })
      onSaved(saved, !editing)
    } catch (err) {
      setError(examsErrorMessage(err, 'types.save'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={editing ? 'Imtihon turini tahrirlash' : 'Yangi imtihon turi'}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="exam-type-form" disabled={!canSave}>
            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
            Saqlash
          </Button>
        </>
      }
    >
      <form id="exam-type-form" onSubmit={(e) => void submit(e)} className="space-y-4">
        <Input
          label="Nomi"
          required
          autoFocus
          maxLength={120}
          placeholder="masalan: Choraklik blok test"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
        <Textarea
          label="Izoh"
          rows={3}
          maxLength={500}
          placeholder="Ixtiyoriy"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
        />
        {editing && (
          <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-600">
            <input
              type="checkbox"
              checked={isActive}
              onChange={(e) => setIsActive(e.target.checked)}
              className="h-4 w-4 rounded border-slate-300 accent-brand-600"
            />
            Faol — yangi imtihon yaratishda tanlash mumkin
          </label>
        )}
        {error && (
          <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {error}
          </p>
        )}
      </form>
    </Modal>
  )
}
