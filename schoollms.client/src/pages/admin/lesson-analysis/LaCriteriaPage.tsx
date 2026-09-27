import { useCallback, useState } from 'react'
import type { FormEvent } from 'react'
import { Pencil, Plus } from 'lucide-react'
import { createCriterion, getCriteria, laErrorMessage, updateCriterion } from '@/api/services/lessonAnalysis'
import type { LaCriterion, LaSection } from '@/types/lessonAnalysis'
import { useAuth } from '@/context/auth-context'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Toast } from '@/components/ui/Toast'
import { isLaAdmin } from '@/components/lesson-analysis/format'
import { Notice } from '@/components/lesson-analysis/ui'
import { useLoad } from '@/components/lesson-analysis/useLoad'
import { cn } from '@/lib/utils'

type Editing = { mode: 'create'; sectionId: string } | { mode: 'edit'; criterion: LaCriterion; sectionId: string }

const byOrder = (a: LaCriterion, b: LaCriterion) => a.orderIndex - b.orderIndex

/** Mezon o'zgarishini bo'limlar ro'yxatiga qo'llaydi (qayta so'rovsiz). */
function applyCriterion(sections: LaSection[], sectionId: string, saved: LaCriterion): LaSection[] {
  return sections.map((section) => {
    const rest = section.criteria.filter((c) => c.id !== saved.id)
    if (section.id !== sectionId) return rest.length === section.criteria.length ? section : { ...section, criteria: rest }
    return { ...section, criteria: [...rest, saved].sort(byOrder) }
  })
}

/**
 * Mezonlar — bo'limlar va ularning mezonlari. Admin/superadmin mezon qo'shadi, matn va
 * tartibini o'zgartiradi, faolligini yoqib-o'chiradi. O'chirish YO'Q (SPEC §3): nofaol
 * mezon eski tahlillarda ko'rinib turaveradi.
 */
export function LaCriteriaPage() {
  const { user } = useAuth()
  const admin = isLaAdmin(user)
  const criteria = useLoad('criteria-all', () => getCriteria(true))
  const [editing, setEditing] = useState<Editing | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const clearError = useCallback(() => setError(null), [])
  const clearNotice = useCallback(() => setNotice(null), [])

  if (criteria.error) return <Notice>{criteria.error}</Notice>
  if (!criteria.data) return <Loader label="Yuklanmoqda..." />
  const sections = criteria.data
  const total = sections.reduce((sum, s) => sum + s.criteria.length, 0)
  const active = sections.reduce((sum, s) => sum + s.criteria.filter((c) => c.isActive).length, 0)
  // qog'oz shakldagidek raqamlash — faqat faol mezonlar, bo'limlar bo'ylab uzluksiz
  const numbers = new Map(
    sections
      .flatMap((s) => s.criteria)
      .filter((c) => c.isActive)
      .map((c, index) => [c.id, index + 1] as const),
  )

  async function toggle(sectionId: string, criterion: LaCriterion) {
    setBusyId(criterion.id)
    setError(null)
    try {
      const saved = await updateCriterion(criterion.id, { isActive: !criterion.isActive })
      criteria.setData((prev) => applyCriterion(prev, sectionId, saved))
      setNotice(saved.isActive ? 'Mezon faollashtirildi' : 'Mezon nofaol qilindi')
    } catch (err) {
      setError(laErrorMessage(err, 'Saqlab bo‘lmadi'))
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="font-semibold text-slate-800">Mezonlar</h2>
          <p className="text-sm text-slate-400">
            {sections.length} ta bo‘lim · {active} ta faol mezon{total > active ? ` · ${total - active} ta nofaol` : ''}
          </p>
        </div>
      </div>

      {!admin && (
        <Notice kind="info">Mezonlarni faqat administrator qo‘shishi va o‘zgartirishi mumkin.</Notice>
      )}

      {sections.map((section) => (
        <Card key={section.id} className="overflow-hidden p-0">
          <header
            className="flex items-center justify-between gap-3 px-5 py-3 text-white"
            style={{ backgroundColor: section.color }}
          >
            <span className="text-[13px] font-semibold uppercase tracking-wide">{section.title}</span>
            {admin && (
              <button
                type="button"
                onClick={() => setEditing({ mode: 'create', sectionId: section.id })}
                className="inline-flex shrink-0 items-center gap-1.5 rounded-lg bg-white/20 px-2.5 py-1.5 text-xs font-medium text-white transition-colors hover:bg-white/30"
              >
                <Plus className="h-3.5 w-3.5" />
                Mezon qo‘shish
              </button>
            )}
          </header>
          {section.criteria.length === 0 ? (
            <p className="px-5 py-6 text-center text-sm text-slate-400">Bu bo‘limda mezon yo‘q</p>
          ) : (
            <ul className="divide-y divide-slate-100">
              {section.criteria.map((criterion) => {
                return (
                  <li key={criterion.id} className="flex items-start gap-3 px-4 py-3 sm:px-5">
                    <span className="mt-0.5 w-6 shrink-0 text-xs font-medium tabular-nums text-slate-400">
                      {numbers.get(criterion.id) ?? '–'}
                    </span>
                    <div className="min-w-0 flex-1">
                      <p className={cn('text-sm leading-snug', criterion.isActive ? 'text-slate-800' : 'text-slate-400 line-through')}>
                        {criterion.text}
                      </p>
                      <p className="mt-0.5 text-[11px] text-slate-400">
                        Tartib: {criterion.orderIndex}
                        {!criterion.isActive && ' · nofaol'}
                      </p>
                    </div>
                    {admin && (
                      <div className="flex shrink-0 items-center gap-1">
                        <button
                          type="button"
                          role="switch"
                          aria-checked={criterion.isActive}
                          aria-label={criterion.isActive ? 'Nofaol qilish' : 'Faollashtirish'}
                          title={criterion.isActive ? 'Nofaol qilish' : 'Faollashtirish'}
                          disabled={busyId === criterion.id}
                          onClick={() => toggle(section.id, criterion)}
                          className={cn(
                            'relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors disabled:opacity-50',
                            criterion.isActive ? 'bg-emerald-500' : 'bg-slate-300',
                          )}
                        >
                          <span
                            className={cn(
                              'inline-block h-5 w-5 rounded-full bg-white shadow transition-transform',
                              criterion.isActive ? 'translate-x-5' : 'translate-x-0.5',
                            )}
                          />
                        </button>
                        <button
                          type="button"
                          onClick={() => setEditing({ mode: 'edit', criterion, sectionId: section.id })}
                          title="Tahrirlash"
                          aria-label="Tahrirlash"
                          className="grid h-9 w-9 place-items-center rounded-lg text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                        >
                          <Pencil className="h-4 w-4" />
                        </button>
                      </div>
                    )}
                  </li>
                )
              })}
            </ul>
          )}
        </Card>
      ))}

      {editing && (
        <CriterionModal
          key={editing.mode === 'edit' ? editing.criterion.id : `new-${editing.sectionId}`}
          editing={editing}
          sections={sections}
          onClose={() => setEditing(null)}
          onSaved={(sectionId, saved) => {
            criteria.setData((prev) => applyCriterion(prev, sectionId, saved))
            setEditing(null)
            setNotice(editing.mode === 'create' ? 'Mezon qo‘shildi' : 'Mezon saqlandi')
          }}
        />
      )}

      <Toast message={error} onClose={clearError} tone="error" duration={6000} />
      <Toast message={notice} onClose={clearNotice} />
    </div>
  )
}

function CriterionModal({
  editing,
  sections,
  onClose,
  onSaved,
}: {
  editing: Editing
  sections: LaSection[]
  onClose: () => void
  onSaved: (sectionId: string, saved: LaCriterion) => void
}) {
  const isEdit = editing.mode === 'edit'
  const [sectionId, setSectionId] = useState(editing.sectionId)
  const [text, setText] = useState(isEdit ? editing.criterion.text : '')
  const [orderIndex, setOrderIndex] = useState(isEdit ? String(editing.criterion.orderIndex) : '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(e: FormEvent) {
    e.preventDefault()
    const trimmed = text.trim()
    if (!trimmed) {
      setError('Mezon matnini kiriting')
      return
    }
    const order = orderIndex.trim() === '' ? null : Number(orderIndex)
    if (order !== null && (!Number.isInteger(order) || order < 0)) {
      setError('Tartib raqami musbat butun son bo‘lishi kerak')
      return
    }
    setSaving(true)
    setError(null)
    try {
      const saved = isEdit
        ? await updateCriterion(editing.criterion.id, {
            text: trimmed,
            ...(order !== null ? { orderIndex: order } : {}),
          })
        : await createCriterion({ sectionId, text: trimmed, orderIndex: order })
      onSaved(isEdit ? editing.sectionId : sectionId, saved)
    } catch (err) {
      setError(laErrorMessage(err, 'Saqlab bo‘lmadi'))
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={isEdit ? 'Mezonni tahrirlash' : 'Yangi mezon'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="la-criterion-form" disabled={saving}>
            {saving ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="la-criterion-form" onSubmit={submit} className="space-y-4">
        {error && <Notice>{error}</Notice>}
        <Select
          label="Bo‘lim"
          value={sectionId}
          onChange={(e) => setSectionId(e.target.value)}
          disabled={isEdit}
        >
          {sections.map((s) => (
            <option key={s.id} value={s.id}>
              {s.title}
            </option>
          ))}
        </Select>
        <Textarea
          label="Mezon matni"
          required
          rows={4}
          maxLength={1000}
          value={text}
          onChange={(e) => setText(e.target.value)}
          autoFocus
        />
        <Input
          label="Tartib raqami"
          type="number"
          min={0}
          inputMode="numeric"
          value={orderIndex}
          onChange={(e) => setOrderIndex(e.target.value)}
          placeholder={isEdit ? undefined : 'Bo‘sh qolsa — bo‘lim oxiriga'}
        />
        {isEdit && (
          <p className="text-xs text-slate-400">
            Matn o‘zgarishi eski tahlillarda ham shu mezon uchun ko‘rinadi. Mezonni o‘chirib bo‘lmaydi — keraksizini
            nofaol qiling.
          </p>
        )}
      </form>
    </Modal>
  )
}
