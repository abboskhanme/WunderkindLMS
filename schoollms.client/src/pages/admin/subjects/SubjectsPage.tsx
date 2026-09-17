import { useEffect, useState } from 'react'
import { Plus, Pencil, Trash2, BookOpen, Users } from 'lucide-react'
import type { Subject } from '@/types'
import type { SubjectPayload } from '@/api/services/subjects'
import {
  getSubjects,
  createSubject,
  updateSubject,
  deleteSubject,
} from '@/api/services/subjects'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { SubjectFormModal } from './SubjectFormModal'

export function SubjectsPage() {
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [loading, setLoading] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Subject | null>(null)

  useEffect(() => {
    getSubjects()
      .then(setSubjects)
      .finally(() => setLoading(false))
  }, [])

  // Server saqlashni yoki o'chirishni RAD ETISHI mumkin (G-9: faol guruhi bor
  // fandan belgini olib bo'lmaydi; F-1: ishlatilayotgan fan o'chirilmaydi).
  // Ilgari javob jimgina tashlab yuborilardi — tugma bosilardi, hech narsa
  // o'zgarmasdi va sababi hech qayerda ko'rinmasdi.
  const handleSubmit = (values: SubjectPayload) => {
    const failed = (e: unknown) =>
      alert(
        (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
          "Fanni saqlab bo'lmadi",
      )
    if (editing) {
      updateSubject(editing.id, values)
        .then((u) => setSubjects((prev) => prev.map((s) => (s.id === u.id ? u : s))))
        .catch(failed)
    } else {
      createSubject(values)
        .then((c) => setSubjects((prev) => [...prev, c]))
        .catch(failed)
    }
    setFormOpen(false)
    setEditing(null)
  }

  const handleDelete = (s: Subject) => {
    if (!confirm(`"${s.name}" fanini o'chirasizmi?`)) return
    deleteSubject(s.id)
      .then(() => setSubjects((prev) => prev.filter((x) => x.id !== s.id)))
      .catch((e) =>
        alert(
          (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
            "Fanni o'chirib bo'lmadi",
        ),
      )
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Fanlar</h1>
          <p className="text-sm text-slate-400">Jami {subjects.length} ta fan</p>
        </div>
        <Button
          onClick={() => {
            setEditing(null)
            setFormOpen(true)
          }}
        >
          <Plus className="h-4 w-4" /> Yangi fan
        </Button>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {subjects.map((s) => (
            <Card key={s.id} className="flex items-center justify-between gap-3">
              <div className="flex items-center gap-3">
                <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                  <BookOpen className="h-5 w-5" />
                </div>
                <div>
                  <p className="font-medium text-slate-800">{s.name}</p>
                  {s.isGroupable && (
                    <span className="mt-0.5 inline-flex items-center gap-1 rounded-md bg-brand-50 px-1.5 py-0.5 text-[11px] font-medium text-brand-600">
                      <Users className="h-3 w-3" /> Guruhlarga bo'linadi
                    </span>
                  )}
                </div>
              </div>
              <div className="flex items-center gap-0.5">
                <IconBtn
                  icon={Pencil}
                  title="Tahrirlash"
                  onClick={() => {
                    setEditing(s)
                    setFormOpen(true)
                  }}
                />
                <IconBtn icon={Trash2} title="O'chirish" danger onClick={() => handleDelete(s)} />
              </div>
            </Card>
          ))}
          {subjects.length === 0 && (
            <p className="col-span-full py-12 text-center text-slate-400">Fanlar yo'q</p>
          )}
        </div>
      )}

      <SubjectFormModal
        open={formOpen}
        onClose={() => {
          setFormOpen(false)
          setEditing(null)
        }}
        onSubmit={handleSubmit}
        initial={editing}
      />
    </div>
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
