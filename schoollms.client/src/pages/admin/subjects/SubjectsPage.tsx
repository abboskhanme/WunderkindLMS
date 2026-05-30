import { useEffect, useState } from 'react'
import { Plus, Pencil, Trash2, BookOpen } from 'lucide-react'
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

  const handleSubmit = (values: SubjectPayload) => {
    if (editing) {
      updateSubject(editing.id, values).then((u) =>
        setSubjects((prev) => prev.map((s) => (s.id === u.id ? u : s))),
      )
    } else {
      createSubject(values).then((c) => setSubjects((prev) => [...prev, c]))
    }
    setFormOpen(false)
    setEditing(null)
  }

  const handleDelete = (s: Subject) => {
    if (!confirm(`"${s.name}" fanini o'chirasizmi?`)) return
    deleteSubject(s.id).then(() => setSubjects((prev) => prev.filter((x) => x.id !== s.id)))
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
                <p className="font-medium text-slate-800">{s.name}</p>
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
