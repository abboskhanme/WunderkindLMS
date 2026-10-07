import { useEffect, useState } from 'react'
import { ArrowDown, ArrowUp, Plus, Trash2 } from 'lucide-react'
import { getOnboardingTemplates, saveOnboardingTemplates } from '@/api/services/recruitment'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

interface Draft {
  key: string
  id: string | null
  title: string
  description: string
  dueDay: number
  isActive: boolean
}

let seq = 0

/**
 * The adaptation steps every new hire gets (11 to start with). A plan already laid out for someone
 * keeps its own copy — changes here apply to the next hire.
 */
export function OnboardingStepsModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [steps, setSteps] = useState<Draft[] | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    getOnboardingTemplates()
      .then((rows) =>
        setSteps(rows.map((r) => ({ key: `s${++seq}`, id: r.id, title: r.title, description: r.description ?? '', dueDay: r.dueDay, isActive: r.isActive }))),
      )
      .catch((e) => setError(hrErrorMessage(e, "Yuklab bo'lmadi")))
  }, [])

  const patch = (key: string, change: Partial<Draft>) => setSteps((s) => s?.map((x) => (x.key === key ? { ...x, ...change } : x)) ?? null)
  const move = (i: number, d: number) =>
    setSteps((s) => {
      if (!s) return s
      const next = [...s]
      const [item] = next.splice(i, 1)
      next.splice(i + d, 0, item)
      return next
    })

  const save = async () => {
    if (!steps) return
    setSaving(true)
    setError('')
    try {
      await saveOnboardingTemplates(
        steps.map((s) => ({ id: s.id, title: s.title.trim(), description: s.description.trim() || null, dueDay: s.dueDay, isActive: s.isActive })),
      )
      onSaved()
    } catch (e) {
      setError(hrErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title="Adaptatsiya bosqichlari (30 kun)"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving || !steps || steps.some((s) => !s.title.trim())}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      {!steps ? (
        error ? <p className="text-sm text-red-600">{error}</p> : <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-3">
          <p className="text-sm text-slate-500">
            Har bir yangi xodimga shu bosqichlar ishga kirgan kunidan hisoblab qo'yiladi. Allaqachon adaptatsiyadagilarning rejasi
            o'zgarmaydi.
          </p>
          {steps.map((s, i) => (
            <div key={s.key} className="flex items-start gap-2 rounded-xl border border-slate-200 p-3">
              <span className="mt-2 w-6 shrink-0 text-sm font-semibold text-slate-400">{i + 1}.</span>
              <div className="min-w-0 flex-1 space-y-2">
                <div className="flex gap-2">
                  <input
                    value={s.title}
                    maxLength={200}
                    onChange={(e) => patch(s.key, { title: e.target.value })}
                    placeholder="Bosqich nomi"
                    className="min-w-0 flex-1 rounded-lg border border-slate-200 px-3 py-1.5 text-sm outline-none focus:border-brand-400"
                  />
                  <label className="flex items-center gap-1 text-sm text-slate-500">
                    <input
                      type="number"
                      min={1}
                      max={30}
                      value={s.dueDay}
                      onChange={(e) => patch(s.key, { dueDay: Math.max(1, Math.min(30, Number(e.target.value) || 1)) })}
                      className="w-16 rounded-lg border border-slate-200 px-2 py-1.5 text-right text-sm outline-none focus:border-brand-400"
                    />
                    -kun
                  </label>
                </div>
                <input
                  value={s.description}
                  maxLength={1000}
                  onChange={(e) => patch(s.key, { description: e.target.value })}
                  placeholder="Izoh (ixtiyoriy)"
                  className="w-full rounded-lg border border-slate-200 px-3 py-1.5 text-xs text-slate-600 outline-none focus:border-brand-400"
                />
                <label className="flex items-center gap-1.5 text-xs text-slate-500">
                  <input type="checkbox" checked={s.isActive} onChange={(e) => patch(s.key, { isActive: e.target.checked })} className="h-3.5 w-3.5" />
                  Faol (yangi xodimlarga qo'yiladi)
                </label>
              </div>
              <div className="flex shrink-0 flex-col gap-1">
                <button type="button" disabled={i === 0} onClick={() => move(i, -1)} className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 disabled:opacity-30" aria-label="Yuqoriga">
                  <ArrowUp className="h-4 w-4" />
                </button>
                <button type="button" disabled={i === steps.length - 1} onClick={() => move(i, 1)} className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 disabled:opacity-30" aria-label="Pastga">
                  <ArrowDown className="h-4 w-4" />
                </button>
                <button type="button" onClick={() => setSteps((x) => x?.filter((y) => y.key !== s.key) ?? null)} className="rounded-lg p-1.5 text-red-500 hover:bg-red-50" aria-label="O'chirish">
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
            </div>
          ))}
          <Button
            variant="secondary"
            onClick={() => setSteps((x) => [...(x ?? []), { key: `s${++seq}`, id: null, title: '', description: '', dueDay: 30, isActive: true }])}
          >
            <Plus className="h-4 w-4" /> Bosqich qo'shish
          </Button>
          {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
        </div>
      )}
    </Modal>
  )
}
