import { useState } from 'react'
import { Lock } from 'lucide-react'
import { getSettings, hrErrorMessage, hrErrorStatus, updateSettings } from '@/api/services/hrTasks'
import type { HrSettings, HrSettingsPayload } from '@/types/hrTasks'
import {
  HR_DEFAULT_REMINDER_OFFSETS,
  HR_MAX_REMINDER_OFFSETS,
  HR_REMINDER_OFFSET_MAX,
  HR_REMINDER_OFFSET_MIN,
} from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Time24Input } from '@/components/ui/Input'
import { EmptyState, Field, Notice, PageHeader, Skeleton, Switch, ToggleChips, WeekdayToggles, inputCls } from '@/components/hr-tasks/ui'
import { useToast } from '@/components/hr-tasks/hooks'
import { useLoad } from '@/components/hr-tasks/useLoad'

const TIME_RE = /^([01]\d|2[0-3]):[0-5]\d$/

/** "30 daqiqa" · "1 soat" · "2 soat". */
function reminderOffsetLabel(minutes: number): string {
  if (minutes >= 60 && minutes % 60 === 0) return `${minutes / 60} soat`
  return `${minutes} daqiqa`
}

interface FormValues {
  digestTime: string
  digestWeekdays: number[]
  morningListEnabled: boolean
  morningTime: string
  reminderEnabled: boolean
  reminderOffsets: number[]
  remindEndOfDay: boolean
}

const toTime = (value: string | null | undefined): string => (value ? value.slice(0, 5) : '')

function toForm(s: HrSettings): FormValues {
  return {
    digestTime: toTime(s.digestTime),
    digestWeekdays: [...(s.digestWeekdays ?? [])],
    morningListEnabled: s.morningListEnabled,
    morningTime: toTime(s.morningTime),
    reminderEnabled: s.reminderEnabled ?? true,
    reminderOffsets: [...(s.reminderOffsets ?? HR_DEFAULT_REMINDER_OFFSETS)],
    remindEndOfDay: s.remindEndOfDay ?? false,
  }
}

function validate(v: FormValues): Partial<Record<keyof FormValues, string>> {
  const e: Partial<Record<keyof FormValues, string>> = {}
  if (!TIME_RE.test(v.digestTime)) e.digestTime = 'Vaqt HH:MM ko‘rinishida bo‘lsin'
  if (v.digestWeekdays.length === 0) e.digestWeekdays = 'Kamida bitta kunni tanlang'
  if (v.morningListEnabled && !TIME_RE.test(v.morningTime)) {
    e.morningTime = 'Ertalabki ro‘yxat yoqilgan — vaqtni HH:MM ko‘rinishida kiriting'
  }
  if (v.reminderOffsets.length > HR_MAX_REMINDER_OFFSETS) e.reminderOffsets = `Ko‘pi bilan ${HR_MAX_REMINDER_OFFSETS} ta eslatma`
  else if (v.reminderEnabled && v.reminderOffsets.length === 0) e.reminderOffsets = 'Kamida bitta eslatma vaqtini belgilang'
  else if (v.reminderOffsets.some((m) => !Number.isInteger(m) || m < HR_REMINDER_OFFSET_MIN || m > HR_REMINDER_OFFSET_MAX)) {
    e.reminderOffsets = `Eslatma ${HR_REMINDER_OFFSET_MIN}–${HR_REMINDER_OFFSET_MAX} daqiqa oralig‘ida bo‘lsin`
  }
  return e
}

/** `PUT` sends the WHOLE object: non-editable fields are echoed from the server copy. */
function toPayload(v: FormValues, current: HrSettings): HrSettingsPayload {
  return {
    timezone: current.timezone,
    digestTime: v.digestTime,
    digestWeekdays: [...new Set(v.digestWeekdays)].sort((a, b) => a - b),
    morningListEnabled: v.morningListEnabled,
    morningTime: v.morningTime || current.morningTime,
    generationTime: current.generationTime,
    closeOverdueTime: current.closeOverdueTime,
    reminderEnabled: v.reminderEnabled,
    // LARGEST FIRST — the domain expects this order.
    reminderOffsets: [...new Set(v.reminderOffsets)].sort((a, b) => b - a),
    remindEndOfDay: v.remindEndOfDay,
  }
}

/**
 * HR settings — evening digest, morning list, pre-deadline reminders (the old
 * `HrSettingsCard`). DIRECTORS only (a backend 403 shows a clear message). No bot settings
 * here: we have one bot (Sozlamalar → Integratsiyalar).
 */
export function HrSettingsPage() {
  // Toast lives on the page: the form remounts (via `key`) after saving; keep the message.
  const toast = useToast()
  const settings = useLoad('hr-settings', getSettings)
  const forbidden = hrErrorStatus(settings.rawError) === 403

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader title="Sozlamalar" description="Yig‘ma xabar, ertalabki ro‘yxat va eslatma sozlamalari" />
      {settings.loading ? (
        <Skeleton className="h-80 w-full rounded-2xl" />
      ) : forbidden ? (
        <Card>
          <EmptyState
            icon={<Lock className="h-5 w-5" />}
            title="HR sozlamalari faqat direktor uchun"
            hint="Bu sozlamalar butun tizimga ta’sir qiladi — ularni HR direktori yoki administrator o‘zgartiradi."
          />
        </Card>
      ) : settings.error || !settings.data ? (
        <Card>
          <EmptyState
            title="Sozlamalarni yuklab bo‘lmadi"
            hint={settings.error ?? undefined}
            action={
              <Button variant="secondary" onClick={settings.reload}>
                Qayta urinish
              </Button>
            }
          />
        </Card>
      ) : (
        <SettingsForm
          key={settings.data.updatedAt ?? 'initial'}
          current={settings.data}
          toast={toast}
          onSaved={(next) => settings.setData(() => next)}
        />
      )}
      {toast.node}
    </div>
  )
}

function SettingsForm({
  current,
  toast,
  onSaved,
}: {
  current: HrSettings
  toast: ReturnType<typeof useToast>
  onSaved: (next: HrSettings) => void
}) {
  const [values, setValues] = useState<FormValues>(() => toForm(current))
  const [submitted, setSubmitted] = useState(false)
  const [busy, setBusy] = useState(false)
  const errors = submitted ? validate(values) : {}
  const set = (patch: Partial<FormValues>) => setValues((v) => ({ ...v, ...patch }))

  // Buttons smallest first; a non-standard stored value (e.g. 45) is added too.
  const offsetChoices = [...new Set([...HR_DEFAULT_REMINDER_OFFSETS, ...values.reminderOffsets])].sort((a, b) => a - b)

  async function save(): Promise<void> {
    setSubmitted(true)
    if (Object.keys(validate(values)).length > 0) return
    setBusy(true)
    try {
      const next = await updateSettings(toPayload(values, current))
      toast.success('Sozlamalar saqlandi')
      onSaved(next)
    } catch (err) {
      toast.error(hrErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card className="p-4 sm:p-5">
      <div className="mb-4">
        <h2 className="font-semibold text-slate-800">HR sozlamalari</h2>
        <p className="text-sm text-slate-400">Kechki yig‘ma xabar, ertalabki ro‘yxat va eslatmalar</p>
      </div>
      <form
        className="space-y-5"
        noValidate
        onSubmit={(e) => {
          e.preventDefault()
          void save()
        }}
      >
        <section className="grid gap-4 sm:grid-cols-2">
          <Field label="Yig‘ma xabar vaqti" required hint="Kunlik yakun rahbarlarga shu vaqtda bitta xabar bo‘lib boradi" error={errors.digestTime}>
            <Time24Input value={values.digestTime} onChange={(digestTime) => set({ digestTime })} />
          </Field>
          <Field label="Vaqt mintaqasi" hint="Barcha vaqtlar shu mintaqada hisoblanadi">
            <input className={inputCls} value={current.timezone} readOnly disabled />
          </Field>
        </section>

        <WeekdayToggles
          label="Yig‘ma xabar kunlari"
          value={values.digestWeekdays}
          onChange={(digestWeekdays) => set({ digestWeekdays })}
          error={errors.digestWeekdays}
          hint="Belgilanmagan kunlarda xabar yuborilmaydi"
        />

        <section className="space-y-3 rounded-xl border border-slate-200 p-3 sm:p-4">
          <Switch
            checked={values.morningListEnabled}
            onChange={(morningListEnabled) => set({ morningListEnabled })}
            label="Ertalabki ro‘yxat yuborilsin"
            hint="Har xodimga o‘sha kungi topshiriqlari ro‘yxati boradi"
          />
          {values.morningListEnabled && (
            <Field
              label="Ertalabki xabar vaqti"
              hint="Sukut — 07:30: xodim ish boshlashidan oldin ro‘yxatni ko‘rsin"
              error={errors.morningTime}
            >
              <Time24Input value={values.morningTime} onChange={(morningTime) => set({ morningTime })} />
            </Field>
          )}
        </section>

        <section className="space-y-3 rounded-xl border border-slate-200 p-3 sm:p-4">
          <Switch
            checked={values.reminderEnabled}
            onChange={(reminderEnabled) => set({ reminderEnabled })}
            label="Muddatdan oldin eslatma"
            hint="Muddat yaqinlashganda xodimga bitta xabar boradi — ichida shu oynadagi topshiriqlar ro‘yxati"
          />
          {values.reminderEnabled && (
            <>
              <Field
                label="Qancha vaqt oldin"
                hint="Bir nechtasini belgilash mumkin; eslatma ±5 daqiqa aniqlikda ketadi"
                error={errors.reminderOffsets}
              >
                <ToggleChips
                  ariaLabel="Eslatma vaqtlari"
                  options={offsetChoices.map((m) => ({
                    value: m,
                    label: reminderOffsetLabel(m),
                    ariaLabel: `Muddatdan ${reminderOffsetLabel(m)} oldin`,
                  }))}
                  value={values.reminderOffsets}
                  onChange={(next) => set({ reminderOffsets: [...next].sort((a, b) => b - a) })}
                />
              </Field>
              <Switch
                checked={values.remindEndOfDay}
                onChange={(remindEndOfDay) => set({ remindEndOfDay })}
                label="Vaqti ko‘rsatilmagan (kun oxiri) topshiriqlarga ham eslatma"
              />
              <Notice kind="warning">
                Yoqilsa har kecha 21:59/22:59/23:29 da xabar ketadi — xodim bildirishnomalarni butunlay o‘chirib qo‘yishi
                mumkin.
              </Notice>
            </>
          )}
        </section>

        <div className="flex justify-end">
          <Button type="submit" className="w-full sm:w-auto" disabled={busy}>
            {busy ? 'Saqlanmoqda…' : 'Sozlamalarni saqlash'}
          </Button>
        </div>
      </form>
    </Card>
  )
}
