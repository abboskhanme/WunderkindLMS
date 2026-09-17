import { useCallback, useEffect, useState } from 'react'
import { Download, FileSignature, Pencil, Plus, Trash2, Wand2 } from 'lucide-react'
import type { SaveStudentContractInput, StudentContract } from '@/api/services/studentContracts'
import {
  contractSourceLabels,
  contractStatusLabels,
  createStudentContract,
  deleteStudentContract,
  getStudentContracts,
  updateStudentContract,
} from '@/api/services/studentContracts'
import { GenerateContractModal } from '@/pages/admin/contracts/GenerateContractModal'
import { StudentContractFormModal } from '@/pages/admin/contracts/StudentContractFormModal'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

interface Props {
  studentId: string
  studentName: string
}

/**
 * O'quvchi kartochkasining "Shartnomalar" tab'i —
 * docs/modules/students-parity.md §2.10 (K-1 kartochka qismi).
 *
 * Shu bolaning butun shartnoma tarixi: eng yangisi tepada, raqamsiz
 * (qoralama) yozuvlar oxirida. Shu yerdan yangi yozuv qo'shish, imzolangan
 * nusxani biriktirish va andozadan yangi shartnoma hosil qilish mumkin.
 */

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

const statusStyles: Record<string, string> = {
  draft: 'bg-slate-100 text-slate-600',
  active: 'bg-emerald-50 text-emerald-700',
  expired: 'bg-amber-50 text-amber-700',
}

export function ContractsTab({ studentId, studentName }: Props) {
  const [rows, setRows] = useState<StudentContract[]>([])
  const [loading, setLoading] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<StudentContract | null>(null)
  const [generateOpen, setGenerateOpen] = useState(false)

  const load = useCallback(() => {
    setLoading(true)
    getStudentContracts(studentId)
      .then(setRows)
      .finally(() => setLoading(false))
  }, [studentId])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- kartochka ochilganda tarix yuklanadi (maqsadli, loyihadagi mavjud naqsh)
    load()
  }, [load])

  const submitForm = async (input: SaveStudentContractInput) => {
    if (editing) await updateStudentContract(editing.id, input)
    else await createStudentContract({ ...input, studentId })
    setFormOpen(false)
    load()
  }

  const remove = async (row: StudentContract) => {
    if (!confirm(`${row.number ?? 'Raqamsiz'} shartnomani o'chirasizmi?`)) return
    try {
      await deleteStudentContract(row.id)
      setRows((prev) => prev.filter((r) => r.id !== row.id))
    } catch (err) {
      alert(errorText(err, "O'chirib bo'lmadi"))
    }
  }

  if (loading && rows.length === 0) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="flex items-center gap-2 text-sm font-semibold text-slate-700">
          <FileSignature className="h-4 w-4 text-brand-600" /> Shartnomalar
        </h3>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={() => setGenerateOpen(true)}>
            <Wand2 className="h-4 w-4" /> Andozadan
          </Button>
          <Button
            onClick={() => {
              setEditing(null)
              setFormOpen(true)
            }}
          >
            <Plus className="h-4 w-4" /> Yozuv qo'shish
          </Button>
        </div>
      </div>

      {rows.length === 0 ? (
        <Card>
          <p className="py-6 text-center text-sm text-slate-400">
            Bu o'quvchida shartnoma yozuvi yo'q.
          </p>
        </Card>
      ) : (
        <div className="space-y-2">
          {rows.map((row) => (
            <Card key={row.id} className="flex flex-wrap items-center gap-3">
              <div className="min-w-0 flex-1">
                <p className="flex flex-wrap items-center gap-2 text-sm font-medium text-slate-800">
                  {row.number ? `№ ${row.number}` : 'Raqamsiz'}
                  <span
                    className={cn(
                      'rounded px-2 py-0.5 text-xs font-medium',
                      statusStyles[row.status],
                    )}
                  >
                    {contractStatusLabels[row.status]}
                  </span>
                </p>
                <p className="mt-0.5 text-xs text-slate-400">
                  {row.signedOn ?? 'sanasiz'}
                  {row.endsOn ? ` — ${row.endsOn}` : ''} · {contractSourceLabels[row.source]}
                  {row.templateName ? ` · ${row.templateName}` : ''}
                  {row.createdByName ? ` · ${row.createdByName}` : ''}
                </p>
                {row.comment && <p className="mt-1 text-xs text-slate-500">{row.comment}</p>}
              </div>

              <div className="flex gap-1">
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
            </Card>
          ))}
        </div>
      )}

      <StudentContractFormModal
        open={formOpen}
        contract={editing}
        fixedStudentId={studentId}
        fixedStudentName={studentName}
        onClose={() => setFormOpen(false)}
        onSubmit={submitForm}
      />
      <GenerateContractModal
        open={generateOpen}
        studentId={studentId}
        studentName={studentName}
        onClose={() => setGenerateOpen(false)}
        onCreated={() => {
          setGenerateOpen(false)
          load()
        }}
      />
    </div>
  )
}
