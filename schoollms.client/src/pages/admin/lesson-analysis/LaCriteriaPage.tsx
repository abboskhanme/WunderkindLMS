import { useCallback, useState } from 'react'
import type { FormEvent } from 'react'
import { Copy, FilePlus2, Pencil, Plus, Settings2 } from 'lucide-react'
import {
  createCriterion,
  createSection,
  createTemplate,
  getCriteria,
  getTemplates,
  laErrorMessage,
  updateCriterion,
  updateSection,
  updateTemplate,
} from '@/api/services/lessonAnalysis'
import type { LaCriterion, LaSection, LaTemplate } from '@/types/lessonAnalysis'
import { useAuth } from '@/context/auth-context'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Toast } from '@/components/ui/Toast'
import { canManageLaCriteria } from '@/components/lesson-analysis/format'
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
 * Mezonlar — tahlil varaqlari, ularning bo'limlari va mezonlari. Admin/superadmin (yoki roliga
 * «Mezonlar» belgisi berilgan xodim — `canManageLaCriteria`) varaq qo'shadi (bo'sh yoki nusxadan),
 * bo'lim qo'shadi va nomlaydi, mezon qo'shadi, matnini, tartibini va bo'limini o'zgartiradi,
 * faolligini yoqib-o'chiradi (mijoz, 2026-10-07). O'chirish YO'Q (SPEC §3): nofaol mezon eski
 * tahlillarda ko'rinib turaveradi, eski tahlil esa o'sha paytdagi mezon matnini saqlaydi.
 */
export function LaCriteriaPage() {
  const { user } = useAuth()
  const admin = canManageLaCriteria(user)
  const templates = useLoad('la-templates', () => getTemplates())
  const [chosen, setChosen] = useState<string | null>(null)
  const sheet: LaTemplate | null =
    templates.data?.find((t) => t.id === chosen) ?? templates.data?.find((t) => t.isDefault) ?? templates.data?.[0] ?? null
  const criteria = useLoad(sheet ? `criteria-all-${sheet.id}` : null, () => getCriteria(true, sheet?.id))
  const [sheetModal, setSheetModal] = useState<'new' | 'edit' | null>(null)
  const [sectionModal, setSectionModal] = useState<LaSection | 'new' | null>(null)
  const [editing, setEditing] = useState<Editing | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const clearError = useCallback(() => setError(null), [])
  const clearNotice = useCallback(() => setNotice(null), [])

  if (templates.error) return <Notice>{templates.error}</Notice>
  if (criteria.error) return <Notice>{criteria.error}</Notice>
  if (!templates.data || !sheet || !criteria.data) return <Loader label="Yuklanmoqda..." />
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

      {/* Tahlil varaqlari (mijoz, 2026-10-07): har biri o'z bo'lim va mezonlari bilan. */}
      <Card className="flex flex-wrap items-center gap-2">
        <span className="text-xs font-medium uppercase tracking-wide text-slate-400">Varaq</span>
        <select
          value={sheet.id}
          onChange={(e) => setChosen(e.target.value)}
          className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
        >
          {templates.data.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name}
              {t.isDefault ? ' (standart)' : ''}
              {!t.isActive ? ' (nofaol)' : ''}
            </option>
          ))}
        </select>
        <span className="text-xs text-slate-400">{sheet.observationCount} ta tahlilda ishlatilgan</span>
        {admin && (
          <div className="ml-auto flex flex-wrap gap-2">
            <Button variant="secondary" onClick={() => setSheetModal('edit')}>
              <Settings2 className="h-4 w-4" /> Varaq sozlamalari
            </Button>
            <Button variant="secondary" onClick={() => setSheetModal('new')}>
              <FilePlus2 className="h-4 w-4" /> Yangi varaq
            </Button>
          </div>
        )}
      </Card>

      {!admin && (
        <Notice kind="info">
          Mezonlarni administrator yoki roliga «Dars tahlili: mezonlar» ruxsati berilgan xodim qo‘shadi va o‘zgartiradi.
        </Notice>
      )}

      {sections.map((section) => (
        <Card key={section.id} className="overflow-hidden p-0">
          <header
            className="flex items-center justify-between gap-3 px-5 py-3 text-white"
            style={{ backgroundColor: section.color }}
          >
            <span className="text-[13px] font-semibold uppercase tracking-wide">{section.title}</span>
            {admin && (
              <div className="flex shrink-0 items-center gap-1.5">
                <button
                  type="button"
                  onClick={() => setSectionModal(section)}
                  title="Bo‘limni tahrirlash"
                  aria-label="Bo‘limni tahrirlash"
                  className="grid h-8 w-8 place-items-center rounded-lg bg-white/20 text-white transition-colors hover:bg-white/30"
                >
                  <Pencil className="h-3.5 w-3.5" />
                </button>
                <button
                  type="button"
                  onClick={() => setEditing({ mode: 'create', sectionId: section.id })}
                  className="inline-flex shrink-0 items-center gap-1.5 rounded-lg bg-white/20 px-2.5 py-1.5 text-xs font-medium text-white transition-colors hover:bg-white/30"
                >
                  <Plus className="h-3.5 w-3.5" />
                  Mezon qo‘shish
                </button>
              </div>
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

      {admin && (
        <Button variant="secondary" onClick={() => setSectionModal('new')}>
          <Plus className="h-4 w-4" /> Bo‘lim qo‘shish
        </Button>
      )}

      {sheetModal && (
        <SheetModal
          mode={sheetModal}
          sheet={sheet}
          sheets={templates.data}
          onClose={() => setSheetModal(null)}
          onSaved={(saved) => {
            setSheetModal(null)
            templates.reload()
            setChosen(saved.id)
            setNotice(sheetModal === 'new' ? 'Varaq qo‘shildi' : 'Varaq saqlandi')
          }}
        />
      )}

      {sectionModal && (
        <SectionModal
          section={sectionModal === 'new' ? null : sectionModal}
          templateId={sheet.id}
          onClose={() => setSectionModal(null)}
          onSaved={() => {
            setSectionModal(null)
            criteria.reload()
            templates.reload()
            setNotice('Bo‘lim saqlandi')
          }}
        />
      )}

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
            ...(sectionId !== editing.sectionId ? { sectionId } : {}),
          })
        : await createCriterion({ sectionId, text: trimmed, orderIndex: order })
      onSaved(sectionId, saved)
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
            Eski tahlillar o‘zgarmaydi — ular javob berilgan paytdagi matnni saqlaydi. Bo‘limni almashtirsangiz, mezon
            shu varaq ichida ko‘chadi. Mezonni o‘chirib bo‘lmaydi — keraksizini nofaol qiling.
          </p>
        )}
      </form>
    </Modal>
  )
}

/** Yangi varaq (bo'sh yoki nusxadan) yoki joriy varaqning nomi, izohi, faolligi, standartligi. */
function SheetModal({
  mode,
  sheet,
  sheets,
  onClose,
  onSaved,
}: {
  mode: 'new' | 'edit'
  sheet: LaTemplate
  sheets: LaTemplate[]
  onClose: () => void
  onSaved: (saved: LaTemplate) => void
}) {
  const isNew = mode === 'new'
  const [name, setName] = useState(isNew ? '' : sheet.name)
  const [description, setDescription] = useState(isNew ? '' : sheet.description ?? '')
  const [copyFrom, setCopyFrom] = useState(isNew ? sheet.id : '')
  const [isActive, setIsActive] = useState(sheet.isActive)
  const [isDefault, setIsDefault] = useState(sheet.isDefault)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (name.trim().length < 2) {
      setError('Varaq nomini kiriting')
      return
    }
    setSaving(true)
    setError(null)
    try {
      const saved = isNew
        ? await createTemplate({ name: name.trim(), description: description.trim() || null, copyFromId: copyFrom || null })
        : await updateTemplate(sheet.id, {
            name: name.trim(),
            description: description.trim(),
            ...(isActive !== sheet.isActive ? { isActive } : {}),
            ...(isDefault && !sheet.isDefault ? { isDefault: true } : {}),
          })
      onSaved(saved)
    } catch (err) {
      setError(laErrorMessage(err, 'Saqlab bo‘lmadi'))
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={isNew ? 'Yangi tahlil varag‘i' : 'Varaq sozlamalari'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="la-sheet-form" disabled={saving}>
            {saving ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="la-sheet-form" onSubmit={submit} className="space-y-4">
        {error && <Notice>{error}</Notice>}
        <Input
          label="Varaq nomi"
          required
          maxLength={120}
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="masalan: Boshlang‘ich sinflar, Ingliz tili darsi"
          autoFocus
        />
        <Textarea
          label="Izoh (ixtiyoriy)"
          rows={2}
          maxLength={500}
          value={description}
          onChange={(e) => setDescription(e.target.value)}
        />
        {isNew ? (
          <Select label="Boshlang‘ich mazmun" value={copyFrom} onChange={(e) => setCopyFrom(e.target.value)}>
            <option value="">Bo‘sh varaq</option>
            {sheets.map((t) => (
              <option key={t.id} value={t.id}>
                Nusxa: {t.name}
              </option>
            ))}
          </Select>
        ) : (
          <div className="space-y-2">
            <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-700">
              <input
                type="checkbox"
                checked={isDefault}
                disabled={sheet.isDefault}
                onChange={(e) => setIsDefault(e.target.checked)}
                className="h-4 w-4 rounded border-slate-300 accent-brand-600"
              />
              Standart varaq (yangi tahlilda oldindan tanlanadi)
            </label>
            <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-700">
              <input
                type="checkbox"
                checked={isActive}
                disabled={sheet.isDefault}
                onChange={(e) => setIsActive(e.target.checked)}
                className="h-4 w-4 rounded border-slate-300 accent-brand-600"
              />
              Faol (yangi tahlilda tanlash mumkin)
            </label>
            {sheet.isDefault && (
              <p className="text-xs text-slate-400">Standart varaqni o‘chirib bo‘lmaydi — avval boshqasini standart qiling.</p>
            )}
          </div>
        )}
        {isNew && (
          <p className="flex items-start gap-1.5 text-xs text-slate-400">
            <Copy className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            Nusxada bo‘limlar va faol mezonlar ko‘chadi; keyin ularni shu varaq ichida tahrirlaysiz.
          </p>
        )}
      </form>
    </Modal>
  )
}

/** Bo'lim qo'shish yoki nomi, rangi, tartibini o'zgartirish. */
function SectionModal({
  section,
  templateId,
  onClose,
  onSaved,
}: {
  section: LaSection | null
  templateId: string
  onClose: () => void
  onSaved: () => void
}) {
  const [title, setTitle] = useState(section?.title ?? '')
  const [color, setColor] = useState(section?.color ?? '#4b5563')
  const [order, setOrder] = useState(section ? String(section.orderIndex) : '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (title.trim().length < 2) {
      setError('Bo‘lim nomini kiriting')
      return
    }
    setSaving(true)
    setError(null)
    try {
      if (section) {
        const orderIndex = order.trim() === '' ? undefined : Number(order)
        await updateSection(section.id, { title: title.trim(), color, ...(orderIndex !== undefined ? { orderIndex } : {}) })
      } else {
        await createSection({ templateId, title: title.trim(), color })
      }
      onSaved()
    } catch (err) {
      setError(laErrorMessage(err, 'Saqlab bo‘lmadi'))
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={section ? 'Bo‘limni tahrirlash' : 'Yangi bo‘lim'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="la-section-form" disabled={saving}>
            {saving ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="la-section-form" onSubmit={submit} className="space-y-4">
        {error && <Notice>{error}</Notice>}
        <Input label="Bo‘lim nomi" required value={title} onChange={(e) => setTitle(e.target.value)} autoFocus />
        <div className="flex items-end gap-3">
          <label className="block">
            <span className="mb-1.5 block text-sm font-medium text-slate-700">Rang</span>
            <input
              type="color"
              value={color}
              onChange={(e) => setColor(e.target.value)}
              className="h-10 w-16 cursor-pointer rounded-lg border border-slate-200 bg-white"
            />
          </label>
          {section && (
            <Input
              label="Tartib raqami"
              type="number"
              min={0}
              value={order}
              onChange={(e) => setOrder(e.target.value)}
            />
          )}
        </div>
      </form>
    </Modal>
  )
}
