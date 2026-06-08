import { useEffect, useState } from 'react'
import { X } from 'lucide-react'
import type { LmsModule } from '@/types'
import type { SaveModulePayload } from '@/api/services/lms'
import { createLmsModule, updateLmsModule } from '@/api/services/lms'

interface Props {
  open: boolean
  /** null — yangi modul yaratish */
  module: LmsModule | null
  /** Yangi modul qaysi fanga tegishli */
  subjectId: string
  onClose: () => void
  onSaved: () => void
}

export function LmsModuleModal({ open, module, subjectId, onClose, onSaved }: Props) {
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (open) {
      setTitle(module?.title ?? '')
      setDescription(module?.description ?? '')
    }
  }, [open, module])

  if (!open) return null

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!title.trim()) return
    const payload: SaveModulePayload = {
      title: title.trim(),
      description: description.trim(),
    }
    setSaving(true)
    try {
      if (module) {
        await updateLmsModule(module.id, payload)
      } else {
        await createLmsModule(subjectId, payload)
      }
      onSaved()
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4">
      <div className="w-full max-w-lg rounded-2xl bg-white shadow-xl">
        <div className="flex items-center justify-between border-b border-slate-100 px-6 py-4">
          <h2 className="font-semibold text-slate-800">
            {module ? 'Modulni tahrirlash' : "Yangi modul qo'shish"}
          </h2>
          <button type="button" onClick={onClose} className="text-slate-400 hover:text-slate-600">
            <X className="h-5 w-5" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4 p-6">
          {/* Nomi */}
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Modul nomi *</label>
            <input
              required
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Masalan: 1-chorak, Algebra asoslari..."
              className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400"
            />
          </div>

          {/* Ta'rif */}
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Ta'rif</label>
            <textarea
              rows={2}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Qisqacha ta'rif..."
              className="w-full resize-none rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400"
            />
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50"
            >
              Bekor qilish
            </button>
            <button
              type="submit"
              disabled={saving || !title.trim()}
              className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-700 disabled:opacity-50"
            >
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
