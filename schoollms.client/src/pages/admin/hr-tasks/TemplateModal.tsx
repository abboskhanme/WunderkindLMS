import { useMemo, useState } from 'react'
import { Plus } from 'lucide-react'
import { createTemplate, hrErrorMessage, updateTemplate } from '@/api/services/hrTasks'
import type { HrEmployee, HrPosition, HrRecurrence, HrTargetType, HrTaskKind, HrTaskTemplate } from '@/types/hrTasks'
import { HR_RECURRENCES, HR_WEEKDAYS } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { Time24Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { Field, Notice, WeekdayToggles, inputCls } from '@/components/hr-tasks/ui'
import { HR_RECURRENCE_LABEL, HR_TASK_KIND_LABEL, hrWeekdayLabel } from '@/components/hr-tasks/lib'
import { NewPositionInline } from './NewPositionInline'
import {
  emptyTemplateForm,
  hasShortMonthDay,
  templateErrors,
  toTemplateFormValues,
  toTemplatePayload,
  type TemplateFormValues,
} from './templateForm'

const box = 'space-y-3 rounded-xl border border-slate-200 p-3 sm:p-4'

/**
 * Add / edit a recurring task (template) — the old `TemplateDialog`. The form adapts to the
 * recurrence; `check` has no KPI fields. Mounted with a `key`.
 */
export function TemplateModal({
  template,
  employees,
  positions,
  canManagePositions,
  onClose,
  onSaved,
}: {
  /** `null` — a new template. */
  template: HrTaskTemplate | null
  employees: HrEmployee[]
  positions: HrPosition[]
  canManagePositions: boolean
  onClose: () => void
  onSaved: (message: string) => void
}) {
  const [values, setValues] = useState<TemplateFormValues>(() =>
    template ? toTemplateFormValues(template) : emptyTemplateForm(),
  )
  const [submitted, setSubmitted] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [addingPosition, setAddingPosition] = useState(false)
  const [extraPositions, setExtraPositions] = useState<HrPosition[]>([])

  const errors = submitted ? templateErrors(values) : {}
  const set = (patch: Partial<TemplateFormValues>) => setValues((v) => ({ ...v, ...patch }))

  const positionOptions = useMemo(() => {
    const merged = [...positions]
    for (const extra of extraPositions) if (!merged.some((p) => p.id === extra.id)) merged.push(extra)
    return merged
  }, [positions, extraPositions])

  async function save(): Promise<void> {
    setSubmitted(true)
    if (Object.keys(templateErrors(values)).length > 0) return
    setBusy(true)
    setError(null)
    try {
      const payload = toTemplatePayload(values)
      if (template) {
        await updateTemplate(template.id, payload)
        onSaved('Shablon yangilandi')
      } else {
        await createTemplate(payload)
        onSaved('Shablon qo‘shildi')
      }
    } catch (err) {
      setError(hrErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title={template ? 'Takroriy topshiriqni tahrirlash' : 'Yangi takroriy topshiriq'}
      footer={
        <div className="flex w-full flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={() => void save()} disabled={busy}>
            {busy ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </div>
      }
    >
      <form
        className="space-y-5"
        noValidate
        onSubmit={(e) => {
          e.preventDefault()
          void save()
        }}
      >
        <p className="text-sm text-slate-400">Shablondan har davr uchun topshiriq nusxasi avtomatik yaratiladi</p>

        <section className="space-y-3">
          <Field label="Sarlavha" required error={errors.title}>
            <input className={inputCls} placeholder="Instagram post" value={values.title} onChange={(e) => set({ title: e.target.value })} />
          </Field>
          <Field label="Tavsif" error={errors.description}>
            <textarea
              className={`${inputCls} resize-none`}
              rows={2}
              placeholder="Xodim topshiriqni ochganda ko‘radigan izoh"
              value={values.description}
              onChange={(e) => set({ description: e.target.value })}
            />
          </Field>
        </section>

        <section className="grid gap-3 rounded-xl border border-slate-200 p-3 sm:grid-cols-2 sm:p-4">
          <Field label="Kimga" required>
            <select
              className={inputCls}
              value={values.targetType}
              onChange={(e) => set({ targetType: e.target.value as HrTargetType })}
            >
              <option value="employee">Bitta xodimga</option>
              <option value="position">Butun lavozimga</option>
            </select>
          </Field>
          {values.targetType === 'employee' ? (
            <Field label="Xodim" required error={errors.targetEmployeeId}>
              <select className={inputCls} value={values.targetEmployeeId} onChange={(e) => set({ targetEmployeeId: e.target.value })}>
                <option value="">Tanlanmagan</option>
                {employees.map((employee) => (
                  <option key={employee.id} value={employee.id}>
                    {employee.fullName}
                  </option>
                ))}
              </select>
            </Field>
          ) : (
            <div className="space-y-1.5">
              <Field
                label="Lavozim"
                required
                hint="Shu lavozimdagi har bir faol xodimga nusxa yaratiladi"
                error={errors.targetPositionId}
              >
                <select className={inputCls} value={values.targetPositionId} onChange={(e) => set({ targetPositionId: e.target.value })}>
                  <option value="">Tanlanmagan</option>
                  {positionOptions.map((position) => (
                    <option key={position.id} value={position.id}>
                      {position.name}
                    </option>
                  ))}
                </select>
              </Field>
              {canManagePositions && !addingPosition && (
                <button
                  type="button"
                  className="inline-flex items-center gap-1 text-sm font-medium text-brand-600 hover:underline"
                  onClick={() => setAddingPosition(true)}
                >
                  <Plus className="h-3.5 w-3.5" />
                  Yangi lavozim
                </button>
              )}
            </div>
          )}
          {values.targetType === 'position' && addingPosition && (
            <NewPositionInline
              className="sm:col-span-2"
              autoFocus
              onCancel={() => setAddingPosition(false)}
              onCreated={(position) => {
                setExtraPositions((list) => [...list, position])
                set({ targetPositionId: position.id })
                setAddingPosition(false)
              }}
            />
          )}
        </section>

        <section className={box}>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Takroriylik" required>
              <select
                className={inputCls}
                value={values.recurrence}
                onChange={(e) => set({ recurrence: e.target.value as HrRecurrence })}
              >
                {HR_RECURRENCES.map((value) => (
                  <option key={value} value={value}>
                    {HR_RECURRENCE_LABEL[value]}
                  </option>
                ))}
              </select>
            </Field>
            <Field label="Muddat vaqti" hint="Bo‘sh qoldirilsa — kun oxiri (23:59)" error={errors.dueTime}>
              <Time24Input value={values.dueTime} onChange={(dueTime) => set({ dueTime })} />
            </Field>
          </div>

          {values.recurrence === 'daily' && (
            <WeekdayToggles
              label="Hafta kunlari"
              value={values.weekdays}
              onChange={(weekdays) => set({ weekdays })}
              error={errors.weekdays}
              hint="Belgilangan kunlarda topshiriq avtomatik yaratiladi"
            />
          )}

          {values.recurrence === 'weekly' && (
            <Field label="Hafta kuni" required error={errors.weekday}>
              <select className={inputCls} value={values.weekday} onChange={(e) => set({ weekday: e.target.value })}>
                {HR_WEEKDAYS.map((day) => (
                  <option key={day} value={String(day)}>
                    {hrWeekdayLabel(day).replace(/^./, (c) => c.toUpperCase())}
                  </option>
                ))}
              </select>
            </Field>
          )}

          {values.recurrence === 'monthly' && (
            <>
              <div className="grid gap-3 sm:grid-cols-2">
                <Field label="Oyning sanasi" required error={errors.dayOfMonth1}>
                  <input
                    className={inputCls}
                    inputMode="numeric"
                    placeholder="14"
                    value={values.dayOfMonth1}
                    onChange={(e) => set({ dayOfMonth1: e.target.value })}
                  />
                </Field>
                <Field label="Ikkinchi sana" hint="Ixtiyoriy — oyda ikki marta takrorlansa" error={errors.dayOfMonth2}>
                  <input
                    className={inputCls}
                    inputMode="numeric"
                    placeholder="28"
                    value={values.dayOfMonth2}
                    onChange={(e) => set({ dayOfMonth2: e.target.value })}
                  />
                </Field>
              </div>
              {hasShortMonthDay(values) && (
                <Notice kind="warning">
                  Qisqa oylarda (29/30/31-sana yo‘q bo‘lganda) topshiriq oyning oxirgi kunida yaratiladi — o‘tkazib
                  yuborilmaydi.
                </Notice>
              )}
            </>
          )}
        </section>

        <section className={box}>
          <Field label="Turi" required hint="«Belgilash» — bajardim/bajarmadim; «Raqamli» — maqsad sari yig‘iladigan son">
            <select
              className={inputCls}
              value={values.kind}
              onChange={(e) => {
                const kind = e.target.value as HrTaskKind
                // Switching to `check` drops stale KPI values.
                set(kind === 'check' ? { kind, targetAmount: '', unit: '' } : { kind })
              }}
            >
              <option value="check">{HR_TASK_KIND_LABEL.check}</option>
              <option value="numeric">{HR_TASK_KIND_LABEL.numeric}</option>
            </select>
          </Field>
          {values.kind === 'numeric' && (
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Maqsad" required error={errors.targetAmount}>
                <input
                  className={inputCls}
                  inputMode="decimal"
                  placeholder="200"
                  value={values.targetAmount}
                  onChange={(e) => set({ targetAmount: e.target.value })}
                />
              </Field>
              <Field label="O‘lchov birligi" required hint="Hisobotda «147/200 lead» ko‘rinishida chiqadi" error={errors.unit}>
                <input className={inputCls} placeholder="lead" value={values.unit} onChange={(e) => set({ unit: e.target.value })} />
              </Field>
            </div>
          )}
          <p className="rounded-lg border border-slate-200 bg-slate-50 p-3 text-xs text-slate-600">
            Isbot har doim talab qilinadi: xodim topshiriqni yopishda fayl biriktiradi yoki izoh yozadi. Fayl istalgan
            turdagi bo‘lishi mumkin; fayl bilan isbotlab bo‘lmaydigan ishlar uchun izohning o‘zi yetadi.
          </p>
        </section>

        <p className="rounded-lg border border-slate-200 bg-slate-50 p-3 text-xs text-slate-600">
          Muddat o‘tsa topshiriq <b>yopilmaydi</b>: u ochiq qoladi va xodimga har kuni ertalab «🔴 muddati o‘tgan»
          belgisi bilan qaytib keladi. Keyin bajarilsa «kechikib bajardi» deb belgilanadi va hisobotda shunday
          ko‘rinadi.
        </p>

        {error && (
          <p className="text-sm text-red-600" role="alert">
            {error}
          </p>
        )}
      </form>
    </Modal>
  )
}
