import { useEffect, useState } from 'react'
import { X } from 'lucide-react'
import type { LmsSubject, LmsUnlockMode } from '@/types'
import type { SaveSubjectPayload } from '@/api/services/lms'

interface Props {
  open: boolean
  editing?: LmsSubject
  saving: boolean
  onClose: () => void
  /** classId LmsSubjectsPage dan beriladi — modal o'zi bilmaydi */
  onSave: (payload: SaveSubjectPayload) => void
}

const MODES: { value: LmsUnlockMode; label: string; desc: string }[] = [
  { value: 'all', label: 'Hammasi ochiq', desc: 'Barcha mavzular bir vaqtda ko\'rinadi' },
  { value: 'sequential', label: 'Ketma-ket', desc: 'Oldingi mavzu tugallananda keyingisi ochiladi' },
  { value: 'batch', label: 'Guruhli', desc: 'Bir vaqtda N ta mavzu ochiq bo\'ladi' },
]

export function LmsSubjectModal({ open, editing, saving, onClose, onSave }: Props) {
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [unlockMode, setUnlockMode] = useState<LmsUnlockMode>('all')
  const [batchSize, setBatchSize] = useState(3)

  useEffect(() => {
    if (open) {
      setTitle(editing?.title ?? '')
      setDescription(editing?.description ?? '')
      setUnlockMode(editing?.unlockMode ?? 'all')
      setBatchSize(editing?.batchSize ?? 3)
    }
  }, [open, editing])

  if (!open) return null

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!title.trim()) return
    onSave({
      classId: editing?.classId ?? '',
      title: title.trim(),
      description: description.trim(),
      unlockMode,
      batchSize,
    })
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4">
      <div className="w-full max-w-lg rounded-2xl bg-white shadow-xl">
        <div className="flex items-center justify-between border-b border-slate-100 px-6 py-4">
          <h2 className="font-semibold text-slate-800">
            {editing ? 'Fanni tahrirlash' : "Yangi fan qo'shish"}
          </h2>
          <button type="button" onClick={onClose} className="text-slate-400 hover:text-slate-600">
            <X className="h-5 w-5" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4 p-6">
          {/* Nomi */}
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Fan nomi *</label>
            <input
              required
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Masalan: Matematika, Fizika..."
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

          {/* Ochilish tartibi */}
          <div>
            <label className="mb-1.5 block text-sm font-medium text-slate-700">
              Mavzular ochilish tartibi
            </label>
            <div className="space-y-2">
              {MODES.map((m) => (
                <label
                  key={m.value}
                  className={`flex cursor-pointer items-start gap-3 rounded-lg border p-3 transition-colors ${
                    unlockMode === m.value
                      ? 'border-brand-300 bg-brand-50'
                      : 'border-slate-200 hover:border-slate-300'
                  }`}
                >
                  <input
                    type="radio"
                    name="unlockMode"
                    value={m.value}
                    checked={unlockMode === m.value}
                    onChange={() => setUnlockMode(m.value)}
                    className="mt-0.5 accent-brand-600"
                  />
                  <div>
                    <p className="text-sm font-medium text-slate-800">{m.label}</p>
                    <p className="text-xs text-slate-500">{m.desc}</p>
                  </div>
                </label>
              ))}
            </div>
          </div>

          {/* Guruh hajmi */}
          {unlockMode === 'batch' && (
            <div className="flex items-center gap-3 rounded-lg bg-brand-50 px-3 py-2">
              <label className="text-sm font-medium text-slate-700">
                Bir vaqtda ochiq mavzular soni:
              </label>
              <input
                type="number"
                min={1}
                max={50}
                value={batchSize}
                onChange={(e) => setBatchSize(Math.max(1, Number(e.target.value)))}
                className="w-20 rounded-lg border border-brand-200 bg-white px-3 py-1.5 text-sm outline-none focus:border-brand-400"
              />
            </div>
          )}

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
