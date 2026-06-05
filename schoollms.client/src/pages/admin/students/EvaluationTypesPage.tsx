import { useEffect, useState } from 'react'
import { Plus, Pencil, Trash2, ClipboardList } from 'lucide-react'
import type { EvaluationType } from '@/types'
import {
  getEvaluationTypes,
  createEvaluationType,
  updateEvaluationType,
  deleteEvaluationType,
} from '@/api/services/studentEvaluation'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input } from '@/components/ui/Input'

/** Feedback nomi — admin xohlagancha feedback nomi (yozma, og'zaki/suhbat...) qo'shadi/tahrirlaydi/o'chiradi. */
export function EvaluationTypesPage() {
  const [types, setTypes] = useState<EvaluationType[]>([])
  const [loading, setLoading] = useState(true)
  const [open, setOpen] = useState(false)
  const [editing, setEditing] = useState<EvaluationType | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    getEvaluationTypes()
      .then(setTypes)
      .finally(() => setLoading(false))
  }, [])

  const openCreate = () => {
    setEditing(null)
    setName('')
    setDescription('')
    setOpen(true)
  }
  const openEdit = (t: EvaluationType) => {
    setEditing(t)
    setName(t.name)
    setDescription(t.description)
    setOpen(true)
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return
    setSaving(true)
    try {
      if (editing) {
        const saved = await updateEvaluationType(editing.id, name.trim(), description.trim())
        setTypes((p) => p.map((x) => (x.id === saved.id ? saved : x)))
      } else {
        const saved = await createEvaluationType(name.trim(), description.trim())
        setTypes((p) => [...p, saved])
      }
      setOpen(false)
    } finally {
      setSaving(false)
    }
  }

  const remove = (t: EvaluationType) => {
    if (!confirm(`"${t.name}" feedback nomini o'chirasizmi?`)) return
    deleteEvaluationType(t.id).then(() => setTypes((p) => p.filter((x) => x.id !== t.id)))
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Feedback nomi</h1>
          <p className="text-sm text-slate-400">
            Feedback nomi — xohlagancha qo'shing (masalan: Og'zaki/Suhbat, Yozma, Nazorat ishi)
          </p>
        </div>
        <Button onClick={openCreate}>
          <Plus className="h-4 w-4" /> Yangi feedback nomi
        </Button>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : types.length === 0 ? (
        <Card className="flex flex-col items-center justify-center gap-3 py-16 text-center">
          <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100">
            <ClipboardList className="h-7 w-7 text-slate-400" />
          </div>
          <p className="text-sm font-medium text-slate-600">Hali feedback nomi qo'shilmagan</p>
          <p className="max-w-sm text-sm text-slate-400">
            "Yangi feedback nomi" tugmasi orqali xohlagancha nom qo'shing.
          </p>
        </Card>
      ) : (
        <Card className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-10 px-4 py-3">#</th>
                  <th className="px-4 py-3">Nomi</th>
                  <th className="px-4 py-3">Izoh</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {types.map((t, i) => (
                  <tr key={t.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                    <td className="px-4 py-3 font-medium text-slate-800">{t.name}</td>
                    <td className="px-4 py-3 text-slate-500">{t.description || '—'}</td>
                    <td className="px-4 py-3">
                      <div className="flex items-center justify-end gap-0.5">
                        <button
                          type="button"
                          title="Tahrirlash"
                          onClick={() => openEdit(t)}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                        >
                          <Pencil className="h-4 w-4" />
                        </button>
                        <button
                          type="button"
                          title="O'chirish"
                          onClick={() => remove(t)}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                        >
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title={editing ? 'Feedback nomini tahrirlash' : 'Yangi feedback nomi'}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setOpen(false)}>
              Bekor qilish
            </Button>
            <Button type="submit" form="eval-type-form" disabled={!name.trim() || saving}>
              Saqlash
            </Button>
          </>
        }
      >
        <form id="eval-type-form" onSubmit={submit} className="space-y-4">
          <Input
            label="Feedback nomi"
            required
            placeholder="masalan: Og'zaki/Suhbat, Yozma"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
          <Input
            label="Izoh (ixtiyoriy)"
            placeholder="qisqacha tavsif"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        </form>
      </Modal>
    </div>
  )
}
