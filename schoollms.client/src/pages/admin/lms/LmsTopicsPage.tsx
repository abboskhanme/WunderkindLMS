import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import {
  ArrowLeft, ChevronUp, ChevronDown, Video, FileText,
  Paperclip, Pencil, Trash2, Plus,
} from 'lucide-react'
import type { LmsSubject, LmsTopic } from '@/types'
import {
  getLmsSubjects, getLmsTopics, createLmsTopic, updateLmsTopic,
  deleteLmsTopic, reorderLmsTopics, updateLmsSubject,
} from '@/api/services/lms'
import { Loader } from '@/components/ui/Loader'
import { Card } from '@/components/ui/Card'
import { LmsSubjectModal } from './LmsSubjectModal'
import { LmsTopicModal } from './LmsTopicModal'

const unlockLabels: Record<string, string> = {
  all: 'Hammasi ochiq',
  sequential: 'Ketma-ket ochiladi',
  batch: 'Guruhli ochiladi',
}

export function LmsTopicsPage() {
  const { classId, subjectId } = useParams<{ classId: string; subjectId: string }>()
  const navigate = useNavigate()

  const [subject, setSubject] = useState<LmsSubject | null>(null)
  const [topics, setTopics] = useState<LmsTopic[]>([])
  const [loading, setLoading] = useState(true)

  const [subjectModal, setSubjectModal] = useState(false)
  const [topicModal, setTopicModal] = useState<{ open: boolean; editing?: LmsTopic }>({ open: false })
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!classId || !subjectId) return
    Promise.all([
      getLmsSubjects(classId),
      getLmsTopics(subjectId),
    ])
      .then(([subs, tops]) => {
        setSubject(subs.find((s) => s.id === subjectId) ?? null)
        setTopics(tops)
      })
      .finally(() => setLoading(false))
  }, [classId, subjectId])

  /* ─── Subject settings ─── */
  const handleSubjectSave = async (payload: Parameters<typeof updateLmsSubject>[1]) => {
    if (!subjectId || !subject) return
    setSaving(true)
    try {
      await updateLmsSubject(subjectId, payload)
      setSubject({ ...subject, ...payload })
      setSubjectModal(false)
    } finally {
      setSaving(false)
    }
  }

  /* ─── Topic CRUD ─── */
  const handleTopicSave = async (payload: Parameters<typeof createLmsTopic>[1]) => {
    if (!subjectId) return
    setSaving(true)
    try {
      if (topicModal.editing) {
        await updateLmsTopic(topicModal.editing.id, payload)
        setTopics((prev) =>
          prev.map((t) =>
            t.id === topicModal.editing!.id
              ? { ...t, ...payload, materials: payload.materials as LmsTopic['materials'] }
              : t,
          ),
        )
      } else {
        const created = await createLmsTopic(subjectId, payload)
        setTopics((prev) => [...prev, created])
      }
      setTopicModal({ open: false })
    } finally {
      setSaving(false)
    }
  }

  const handleTopicDelete = async (id: string) => {
    if (!window.confirm("Mavzu va uning materiallari o'chiriladi. Davom etasizmi?")) return
    await deleteLmsTopic(id)
    setTopics((prev) => prev.filter((t) => t.id !== id))
  }

  /* ─── Reorder ─── */
  const move = async (index: number, dir: -1 | 1) => {
    if (!subjectId) return
    const next = [...topics]
    const swap = index + dir
    if (swap < 0 || swap >= next.length) return
    ;[next[index], next[swap]] = [next[swap], next[index]]
    setTopics(next)
    await reorderLmsTopics(subjectId, next.map((t) => t.id))
  }

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (!subject) return <p className="py-12 text-center text-slate-400">Fan topilmadi</p>

  return (
    <div className="space-y-6">
      {/* Breadcrumb + amallar */}
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          {/* Breadcrumb */}
          <div className="mb-1 flex items-center gap-1 text-sm text-slate-400">
            <button
              type="button"
              onClick={() => navigate('/admin/lms')}
              className="hover:text-brand-600"
            >
              Ta'lim
            </button>
            <span>/</span>
            <button
              type="button"
              onClick={() => navigate(`/admin/lms/${classId}`)}
              className="hover:text-brand-600"
            >
              {subject.className}-sinf
            </button>
            <span>/</span>
            <span className="text-slate-700">{subject.title}</span>
          </div>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => navigate(`/admin/lms/${classId}`)}
              className="flex items-center gap-1 rounded-lg px-2 py-1 text-sm text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-700"
            >
              <ArrowLeft className="h-4 w-4" />
            </button>
            <h1 className="text-xl font-semibold text-slate-800">{subject.title}</h1>
          </div>

          {subject.description && (
            <p className="mt-0.5 pl-8 text-sm text-slate-400">{subject.description}</p>
          )}
        </div>

        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => setSubjectModal(true)}
            className="rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition-colors hover:bg-slate-50"
          >
            Sozlamalar
          </button>
          <button
            type="button"
            onClick={() => setTopicModal({ open: true })}
            className="inline-flex items-center gap-2 rounded-xl bg-brand-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-700"
          >
            <Plus className="h-4 w-4" />
            Yangi mavzu
          </button>
        </div>
      </div>

      {/* Fan sozlamalari banner */}
      <div className="flex flex-wrap items-center gap-4 rounded-xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600">
        <span>
          <span className="font-medium">Ochilish:</span>{' '}
          <span className="rounded-full bg-white px-2.5 py-0.5 text-xs font-semibold text-slate-700 shadow-sm">
            {unlockLabels[subject.unlockMode] ?? subject.unlockMode}
            {subject.unlockMode === 'batch' && ` (${subject.batchSize} ta)`}
          </span>
        </span>
        <span className="text-slate-400">|</span>
        <span className="text-slate-400">{topics.length} ta mavzu</span>
      </div>

      {/* Mavzular ro'yxati */}
      {topics.length === 0 ? (
        <Card className="py-16 text-center">
          <p className="text-slate-400">Hali mavzu qo'shilmagan</p>
          <button
            type="button"
            onClick={() => setTopicModal({ open: true })}
            className="mt-3 text-sm font-medium text-brand-600 hover:text-brand-700"
          >
            Birinchi mavzuni qo'shing →
          </button>
        </Card>
      ) : (
        <div className="space-y-2">
          {topics.map((t, i) => (
            <div
              key={t.id}
              className="flex items-center gap-3 rounded-xl border border-slate-200 bg-white px-4 py-3 shadow-sm"
            >
              {/* Tartib raqami */}
              <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-slate-100 text-sm font-bold text-slate-600">
                {t.order}
              </div>

              {/* Asosiy info */}
              <div className="min-w-0 flex-1">
                <p className="truncate font-medium text-slate-800">{t.title}</p>
                {t.description && (
                  <p className="truncate text-xs text-slate-400">{t.description}</p>
                )}
                <div className="mt-1 flex flex-wrap items-center gap-2.5">
                  {t.videoUrl && (
                    <span className="flex items-center gap-1 text-xs text-brand-600">
                      <Video className="h-3.5 w-3.5" />
                      Video
                    </span>
                  )}
                  {t.textContent && (
                    <span className="flex items-center gap-1 text-xs text-slate-500">
                      <FileText className="h-3.5 w-3.5" />
                      Matn
                    </span>
                  )}
                  {t.materials.length > 0 && (
                    <span className="flex items-center gap-1 text-xs text-slate-500">
                      <Paperclip className="h-3.5 w-3.5" />
                      {t.materials.length} ta material
                    </span>
                  )}
                  {t.completedCount > 0 && (
                    <span className="text-xs text-emerald-600">
                      ✓ {t.completedCount} o'quvchi
                    </span>
                  )}
                </div>
              </div>

              {/* Amallar */}
              <div className="flex shrink-0 items-center gap-1">
                <button
                  type="button"
                  onClick={() => move(i, -1)}
                  disabled={i === 0}
                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600 disabled:opacity-25"
                  title="Yuqoriga"
                >
                  <ChevronUp className="h-4 w-4" />
                </button>
                <button
                  type="button"
                  onClick={() => move(i, 1)}
                  disabled={i === topics.length - 1}
                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600 disabled:opacity-25"
                  title="Pastga"
                >
                  <ChevronDown className="h-4 w-4" />
                </button>
                <button
                  type="button"
                  onClick={() => setTopicModal({ open: true, editing: t })}
                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-brand-50 hover:text-brand-600"
                  title="Tahrirlash"
                >
                  <Pencil className="h-4 w-4" />
                </button>
                <button
                  type="button"
                  onClick={() => handleTopicDelete(t.id)}
                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-500"
                  title="O'chirish"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Modals */}
      <LmsSubjectModal
        open={subjectModal}
        editing={subject}
        saving={saving}
        onClose={() => setSubjectModal(false)}
        onSave={handleSubjectSave}
      />
      <LmsTopicModal
        open={topicModal.open}
        editing={topicModal.editing}
        saving={saving}
        onClose={() => setTopicModal({ open: false })}
        onSave={handleTopicSave}
      />
    </div>
  )
}
