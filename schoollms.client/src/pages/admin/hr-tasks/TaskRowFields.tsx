import { Trash2 } from 'lucide-react'
import type { HrRecurrence, HrTaskKind } from '@/types/hrTasks'
import { HR_RECURRENCES, HR_TASK_KINDS, HR_WEEKDAYS } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { Time24Input } from '@/components/ui/Input'
import { DateTimeField, Field, WeekdayToggles, inputCls } from '@/components/hr-tasks/ui'
import { HR_RECURRENCE_LABEL, hrTaskKindLabel, hrWeekdayLabel } from '@/components/hr-tasks/lib'
import { VoiceRecorder } from './VoiceRecorder'
import { hrHasShortMonthDay, type HrTaskRow, type HrTaskRowErrors } from './taskBatchForm'

/**
 * ONE row of the "Topshiriq berish" page (the old `TaskRowFields`). With "Takroriy" on, a
 * recurrence rule replaces the due date; voice notes are for one-off tasks only.
 */
export function TaskRowFields({
  index,
  row,
  errors,
  recurring,
  canRemove,
  disabled = false,
  onChange,
  onRemove,
}: {
  index: number
  row: HrTaskRow
  errors: HrTaskRowErrors
  recurring: boolean
  canRemove: boolean
  disabled?: boolean
  onChange: (patch: Partial<HrTaskRow>) => void
  onRemove: () => void
}) {
  const numeric = row.kind === 'numeric'

  return (
    <li className="rounded-2xl border border-slate-200 bg-white p-3 sm:p-4">
      <div className="mb-3 flex items-center justify-between gap-2">
        <p className="text-sm font-semibold text-slate-800">{index + 1}-topshiriq</p>
        {canRemove && (
          <Button type="button" variant="ghost" className="px-3 py-1.5" disabled={disabled} onClick={onRemove}>
            <Trash2 className="h-4 w-4" />
            O‘chirish
          </Button>
        )}
      </div>

      <div className="space-y-3">
        <Field label="Sarlavha" required error={errors.title}>
          <input
            className={inputCls}
            placeholder="Masalan: Sentabr hisobotini tayyorlash"
            value={row.title}
            disabled={disabled}
            onChange={(e) => onChange({ title: e.target.value })}
          />
        </Field>

        <Field label="Tavsif" error={errors.description}>
          <textarea
            className={`${inputCls} resize-none`}
            rows={2}
            placeholder="Nima qilinishi kerakligi qisqacha"
            value={row.description}
            disabled={disabled}
            onChange={(e) => onChange({ description: e.target.value })}
          />
        </Field>

        <div className="grid gap-3 sm:grid-cols-2">
          {recurring ? (
            <Field label="Takroriylik">
              <select
                className={inputCls}
                value={row.recurrence}
                disabled={disabled}
                onChange={(e) => onChange({ recurrence: e.target.value as HrRecurrence })}
              >
                {HR_RECURRENCES.map((value) => (
                  <option key={value} value={value}>
                    {HR_RECURRENCE_LABEL[value]}
                  </option>
                ))}
              </select>
            </Field>
          ) : (
            <DateTimeField
              label="Muddat"
              required
              hint="Toshkent vaqti"
              date={row.dueDate}
              time={row.dueTime}
              disabled={disabled}
              error={errors.due}
              onDate={(dueDate) => onChange({ dueDate })}
              onTime={(dueTime) => onChange({ dueTime })}
            />
          )}

          <Field label="Turi">
            <select
              className={inputCls}
              value={row.kind}
              disabled={disabled}
              onChange={(e) => onChange({ kind: e.target.value as HrTaskKind })}
            >
              {HR_TASK_KINDS.map((value) => (
                <option key={value} value={value}>
                  {hrTaskKindLabel(value)}
                </option>
              ))}
            </select>
          </Field>
        </div>

        {recurring && (
          <div className="space-y-3">
            {row.recurrence === 'daily' && (
              <WeekdayToggles
                label="Hafta kunlari"
                value={row.weekdays}
                disabled={disabled}
                error={errors.weekdays}
                hint="Tanlangan kunlarda nusxa yaratiladi"
                onChange={(weekdays) => onChange({ weekdays })}
              />
            )}
            {row.recurrence === 'weekly' && (
              <Field label="Hafta kuni" error={errors.weekday}>
                <select
                  className={inputCls}
                  value={row.weekday}
                  disabled={disabled}
                  onChange={(e) => onChange({ weekday: e.target.value })}
                >
                  {HR_WEEKDAYS.map((day) => (
                    <option key={day} value={String(day)}>
                      {hrWeekdayLabel(day)}
                    </option>
                  ))}
                </select>
              </Field>
            )}
            {row.recurrence === 'monthly' && (
              <div className="grid gap-3 sm:grid-cols-2">
                <Field label="Oyning sanasi" required error={errors.dayOfMonth1}>
                  <input
                    className={inputCls}
                    inputMode="numeric"
                    placeholder="14"
                    value={row.dayOfMonth1}
                    disabled={disabled}
                    onChange={(e) => onChange({ dayOfMonth1: e.target.value })}
                  />
                </Field>
                <Field label="Ikkinchi sana" error={errors.dayOfMonth2}>
                  <input
                    className={inputCls}
                    inputMode="numeric"
                    placeholder="28 (ixtiyoriy)"
                    value={row.dayOfMonth2}
                    disabled={disabled}
                    onChange={(e) => onChange({ dayOfMonth2: e.target.value })}
                  />
                </Field>
              </div>
            )}
            <Field label="Muddat soati" hint="Bo‘sh qoldirilsa — kun oxiri" error={errors.templateDueTime}>
              <Time24Input
                value={row.templateDueTime}
                disabled={disabled}
                onChange={(templateDueTime) => onChange({ templateDueTime })}
              />
            </Field>
            {hrHasShortMonthDay(row) && (
              <p className="text-xs text-slate-400">29–31-sanalar qisqa oylarda oyning oxirgi kuniga suriladi.</p>
            )}
          </div>
        )}

        {numeric && (
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Maqsad" required error={errors.targetAmount}>
              <input
                className={inputCls}
                inputMode="decimal"
                placeholder="200"
                value={row.targetAmount}
                disabled={disabled}
                onChange={(e) => onChange({ targetAmount: e.target.value })}
              />
            </Field>
            <Field label="O‘lchov birligi" required={recurring} error={errors.unit}>
              <input
                className={inputCls}
                placeholder="lead, dona, so‘m"
                value={row.unit}
                disabled={disabled}
                onChange={(e) => onChange({ unit: e.target.value })}
              />
            </Field>
          </div>
        )}

        {recurring ? (
          <Field label="Ovozli izoh">
            <p className="text-sm text-slate-400">Ovozli izoh hozircha faqat bir martalik topshiriqqa biriktiriladi.</p>
          </Field>
        ) : (
          <Field label="Ovozli izoh" hint="Yozib olinganda darhol yuklanadi va topshiriqqa biriktiriladi">
            <VoiceRecorder value={row.attachment} disabled={disabled} onChange={(attachment) => onChange({ attachment })} />
          </Field>
        )}
      </div>
    </li>
  )
}
