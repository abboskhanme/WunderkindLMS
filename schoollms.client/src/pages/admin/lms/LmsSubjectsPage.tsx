import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { ArrowLeft, BookOpen, ChevronRight, Layers, Lock, Pencil, Trash2, Plus } from 'lucide-react'
import type { LmsSubject, LmsUnlockMode, SchoolClass } from '@/types'
import {
  getLmsSubjects, createLmsSubject, updateLmsSubject, deleteLmsSubject,
} from '@/api/services/lms'
import { getClasses } from '@/api/services/classes'
import { Loader } from '@/components/ui/Loader'
import { Card } from '@/components/ui/Card'
import { cn } from '@/lib/utils'
import { LmsSubjectModal } from './LmsSubjectModal'

const unlockLabel: Record<LmsUnlockMode, string> = {
  all: 'Hammasi ochiq',
  sequential: 'Ketma-ket',
  batch: 'Guruhli',
}

const unlockColor: Record<LmsUnlockMode, string> = {
  all: 'bg-emerald-100 text-emerald-700',
  sequential: 'bg-amber-100 text-amber-700',
  batch: 'bg-brand-100 text-brand-700',
}

export function LmsSubjectsPage() {
  const { classId } = useParams<{ classId: string }>()
  const navigate = useNavigate()

  const [schoolClass, setSchoolClass] = useState<SchoolClass | null>(null)
  const [subjects, setSubjects] = useState<LmsSubject[]>([])
  const [loading, setLoading] = useState(true)
  const [modal, setModal] = useState<{ open: boolean; editing?: LmsSubject }>({ open: false })
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!classId) return
    Promise.all([getClasses(), getLmsSubjects(classId)])
      .then(([cls, subs]) => {
        setSchoolClass(cls.find((c) => c.id === classId) ?? null)
        setSubjects(subs)
      })
      .finally(() => setLoading(false))
  }, [classId])

  const handleSave = async (payload: Parameters<typeof createLmsSubject>[0]) => {
    if (!classId) return
    setSaving(true)
    try {
      if (modal.editing) {
        await updateLmsSubject(modal.editing.id, {
          title: payload.title,
          description: payload.description,
          unlockMode: payload.unlockMode,
          batchSize: payload.batchSize,
        })
        setSubjects((prev) =>
          prev.map((s) =>
            s.id === modal.editing!.id ? { ...s, ...payload } : s,
          ),
        )
      } else {
        const created = await createLmsSubject({ ...payload, classId })
        setSubjects((prev) => [...prev, created])
      }
      setModal({ open: false })
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async (id: string) => {
    if (!window.confirm("Fan va uning barcha mavzulari o'chiriladi. Davom etasizmi?")) return
    await deleteLmsSubject(id)
    setSubjects((prev) => prev.filter((s) => s.id !== id))
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={() => navigate('/admin/lms')}
            className="flex items-center gap-1 rounded-lg px-2 py-1.5 text-sm text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-700"
          >
            <ArrowLeft className="h-4 w-4" />
            Sinflar
          </button>
          <div>
            <h1 className="text-xl font-semibold text-slate-800">
              {schoolClass ? `${schoolClass.name}-sinf` : '...'} — Fanlar
            </h1>
            <p className="text-sm text-slate-400">
              Sinfga fan qo'shing, mavzularni boshqaring
            </p>
          </div>
        </div>
        <button
          type="button"
          onClick={() => setModal({ open: true })}
          className="inline-flex items-center gap-2 rounded-xl bg-brand-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-700"
        >
          <Plus className="h-4 w-4" />
          Yangi fan
        </button>
      </div>

      {/* Fanlar */}
      {subjects.length === 0 ? (
        <Card className="py-16 text-center">
          <BookOpen className="mx-auto mb-3 h-10 w-10 text-slate-300" />
          <p className="text-slate-400">Bu sinfda hali fan qo'shilmagan</p>
          <button
            type="button"
            onClick={() => setModal({ open: true })}
            className="mt-4 text-sm font-medium text-brand-600 hover:text-brand-700"
          >
            Birinchi fanni qo'shing →
          </button>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {subjects.map((s) => (
            <div
              key={s.id}
              className="flex flex-col rounded-2xl border border-slate-200/80 bg-white shadow-sm transition-shadow hover:shadow-md"
            >
              {/* Asosiy qism — bosish → mavzular */}
              <button
                type="button"
                onClick={() => navigate(`/admin/lms/${classId}/${s.id}`)}
                className="flex flex-1 flex-col gap-2 rounded-t-2xl p-4 text-left"
              >
                <div className="flex items-start justify-between gap-2">
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                    <BookOpen className="h-5 w-5" />
                  </div>
                  <span
                    className={cn(
                      'rounded-full px-2.5 py-0.5 text-xs font-semibold',
                      unlockColor[s.unlockMode],
                    )}
                  >
                    {unlockLabel[s.unlockMode]}
                    {s.unlockMode === 'batch' && ` (${s.batchSize})`}
                  </span>
                </div>

                <div>
                  <p className="font-semibold text-slate-800">{s.title}</p>
                  {s.description && (
                    <p className="mt-0.5 line-clamp-2 text-xs text-slate-400">{s.description}</p>
                  )}
                </div>

                <div className="mt-auto flex items-center gap-1 text-xs text-slate-500">
                  <Layers className="h-3.5 w-3.5" />
                  {s.topicsCount} ta mavzu
                </div>
              </button>

              {/* Tugmalar */}
              <div className="flex items-center justify-between border-t border-slate-100 px-4 py-2">
                <div className="flex gap-1">
                  <button
                    type="button"
                    onClick={() => setModal({ open: true, editing: s })}
                    className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                    title="Tahrirlash"
                  >
                    <Pencil className="h-3.5 w-3.5" />
                  </button>
                  <button
                    type="button"
                    onClick={() => handleDelete(s.id)}
                    className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-500"
                    title="O'chirish"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                </div>
                <button
                  type="button"
                  onClick={() => navigate(`/admin/lms/${classId}/${s.id}`)}
                  className="flex items-center gap-1 text-xs font-medium text-brand-600 hover:text-brand-700"
                >
                  Mavzular <ChevronRight className="h-3.5 w-3.5" />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      <LmsSubjectModal
        open={modal.open}
        editing={modal.editing}
        saving={saving}
        onClose={() => setModal({ open: false })}
        onSave={handleSave}
      />
    </div>
  )
}
