/**
 * Alohida qo'ng'iroq jadvali muharriri (mijoz, 2026-10-11: "istalgancha dars soatlarini tahrirlash
 * mumkin bo'lsin ... kichik sinflarda dars soatlari va vaqtlari boshqacha bo'lishi mumkin").
 *
 * Bitta oynada: nomi, darslar soni va vaqtlari, qaysi sinf/guruhlarga tegishli. Sinf boshqa jadvalda
 * bo'lsa, uni tanlash uni shu jadvalga ko'chiradi — chipda qaysi jadvaldan kelishi yozib turiladi.
 */
import { useMemo, useState } from 'react'
import { Copy, Plus, Trash2 } from 'lucide-react'
import type { BellOwnerOption, BellSchedule, LessonTime, SaveBellScheduleInput } from '@/types'
import { saveBellSchedule } from '@/api/services/settings'
import { billingErrorMessage } from '@/api/services/billingError'
import { Button } from '@/components/ui/Button'
import { Input, Time24Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'

const MAX_PERIODS = 20

function toMinutes(t: string): number | null {
  const m = /^(\d{2}):(\d{2})$/.exec(t)
  return m ? Number(m[1]) * 60 + Number(m[2]) : null
}

function fromMinutes(n: number): string {
  const v = Math.max(0, Math.min(23 * 60 + 59, n))
  return `${String(Math.floor(v / 60)).padStart(2, '0')}:${String(v % 60).padStart(2, '0')}`
}

/** Har dars uchun xato matni (yoki null) — server ham xuddi shu qoidalarni tekshiradi. */
function periodErrors(periods: LessonTime[]): (string | null)[] {
  let prevEnd: number | null = null
  return periods.map((p) => {
    const s = toMinutes(p.startTime)
    const e = toMinutes(p.endTime)
    if (s === null || e === null) return 'Vaqtni kiriting'
    if (e <= s) return 'Tugashi boshlanishidan keyin bo‘lsin'
    const err = prevEnd !== null && s < prevEnd ? 'Oldingi dars tugamasdan boshlanyapti' : null
    prevEnd = e
    return err
  })
}

export function BellScheduleModal({
  schedule,
  schedules,
  owners,
  defaultTimes,
  onClose,
  onSaved,
}: {
  /** null — yangi jadval. */
  schedule: BellSchedule | null
  schedules: BellSchedule[]
  owners: BellOwnerOption[]
  defaultTimes: LessonTime[]
  onClose: () => void
  onSaved: (saved: BellSchedule) => void
}) {
  const [name, setName] = useState(schedule?.name ?? '')
  const [periods, setPeriods] = useState<LessonTime[]>(
    schedule?.periods.length ? schedule.periods : defaultTimes.filter((t) => t.startTime && t.endTime),
  )
  const [selected, setSelected] = useState<Set<string>>(() => new Set(schedule?.ownerIds ?? []))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [touched, setTouched] = useState(false)

  const errors = useMemo(() => periodErrors(periods), [periods])
  const scheduleName = useMemo(() => new Map(schedules.map((s) => [s.id, s.name])), [schedules])

  const classes = owners.filter((o) => o.kind === 'class')
  const tracks = owners.filter((o) => o.kind === 'group' && o.isTrack)
  const groups = owners.filter((o) => o.kind === 'group' && !o.isTrack)
  const grades = [...new Set(classes.map((c) => c.grade))].sort((a, b) => a - b)

  const renumber = (list: LessonTime[]) => list.map((t, i) => ({ ...t, period: i + 1 }))

  const update = (i: number, field: 'startTime' | 'endTime', value: string) =>
    setPeriods((prev) => prev.map((t, idx) => (idx === i ? { ...t, [field]: value } : t)))

  /** Keyingi dars: oldingisidan keyin xuddi shu tanaffus va davomiylik bilan. */
  const addPeriod = () =>
    setPeriods((prev) => {
      if (prev.length >= MAX_PERIODS) return prev
      const last = prev[prev.length - 1]
      const beforeLast = prev[prev.length - 2]
      const lastStart = last ? toMinutes(last.startTime) : null
      const lastEnd = last ? toMinutes(last.endTime) : null
      const prevEnd = beforeLast ? toMinutes(beforeLast.endTime) : null
      let next: LessonTime = { period: prev.length + 1, startTime: '', endTime: '' }
      if (lastStart !== null && lastEnd !== null && lastEnd > lastStart) {
        const gap = prevEnd !== null && lastStart >= prevEnd ? lastStart - prevEnd : 10
        const start = lastEnd + gap
        next = { ...next, startTime: fromMinutes(start), endTime: fromMinutes(start + (lastEnd - lastStart)) }
      } else if (!last) {
        next = { ...next, startTime: '08:30', endTime: '09:15' }
      }
      return [...prev, next]
    })

  const removePeriod = (i: number) => setPeriods((prev) => renumber(prev.filter((_, idx) => idx !== i)))

  const toggle = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  /** Butun daraja: hammasi tanlangan bo'lsa — olib tashlaydi, aks holda hammasini qo'shadi. */
  const toggleGrade = (grade: number) =>
    setSelected((prev) => {
      const ids = classes.filter((c) => c.grade === grade).map((c) => c.id)
      const all = ids.every((id) => prev.has(id))
      const next = new Set(prev)
      ids.forEach((id) => (all ? next.delete(id) : next.add(id)))
      return next
    })

  const valid = name.trim().length > 0 && periods.length > 0 && errors.every((e) => e === null)

  const save = async () => {
    setTouched(true)
    if (!valid) return
    setSaving(true)
    setError(null)
    const input: SaveBellScheduleInput = { name: name.trim(), periods: renumber(periods), ownerIds: [...selected] }
    try {
      onSaved(await saveBellSchedule(schedule?.id ?? null, input))
    } catch (e) {
      setError(billingErrorMessage(e, "Jadvalni saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const chip = (o: BellOwnerOption) => {
    const on = selected.has(o.id)
    const elsewhere = o.scheduleId && o.scheduleId !== schedule?.id ? scheduleName.get(o.scheduleId) : null
    return (
      <button
        key={o.id}
        type="button"
        onClick={() => toggle(o.id)}
        title={elsewhere ? `Hozir "${elsewhere}" jadvalida — tanlansa shu jadvalga o'tadi` : undefined}
        className={cn(
          'rounded-lg border px-2.5 py-1 text-xs font-medium transition-colors',
          on
            ? 'border-brand-300 bg-brand-50 text-brand-800'
            : 'border-slate-200 bg-white text-slate-600 hover:border-slate-300 hover:bg-slate-50',
        )}
      >
        {o.name}
        {elsewhere && <span className={cn('ml-1 font-normal', on ? 'text-amber-700' : 'text-slate-400')}>· {elsewhere}</span>}
      </button>
    )
  }

  return (
    <Modal
      open
      onClose={() => !saving && onClose()}
      title={schedule ? 'Jadvalni tahrirlash' : 'Yangi qo‘ng‘iroq jadvali'}
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        <div>
          <Input
            label="Nomi"
            required
            value={name}
            maxLength={120}
            placeholder="Masalan: Boshlang'ich sinflar"
            onChange={(e) => setName(e.target.value)}
          />
          {touched && !name.trim() && <p className="mt-1 text-xs text-red-600">Jadval nomini kiriting</p>}
        </div>

        <section>
          <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-sm font-semibold text-slate-700">Dars vaqtlari</h3>
            {defaultTimes.length > 0 && (
              <button
                type="button"
                onClick={() => setPeriods(renumber(defaultTimes.filter((t) => t.startTime && t.endTime)))}
                className="inline-flex items-center gap-1 text-xs font-medium text-slate-500 hover:text-brand-700"
              >
                <Copy className="h-3.5 w-3.5" /> Asosiy jadvaldan nusxa
              </button>
            )}
          </div>
          <div className="space-y-1.5">
            {periods.map((t, i) => {
              const s = toMinutes(t.startTime)
              const e = toMinutes(t.endTime)
              const err = touched || (t.startTime && t.endTime) ? errors[i] : null
              return (
                <div key={i}>
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="w-16 text-sm font-medium text-slate-600">{i + 1}-dars</span>
                    <Time24Input value={t.startTime} onChange={(v) => update(i, 'startTime', v)} />
                    <span className="text-slate-400">—</span>
                    <Time24Input value={t.endTime} onChange={(v) => update(i, 'endTime', v)} />
                    <span className="w-14 text-xs text-slate-400">{s !== null && e !== null && e > s ? `${e - s} daq` : ''}</span>
                    <button
                      type="button"
                      onClick={() => removePeriod(i)}
                      title="O'chirish"
                      className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </div>
                  {err && <p className="ml-[4.5rem] text-xs text-red-600">{err}</p>}
                </div>
              )
            })}
            {periods.length === 0 && <p className="text-sm text-slate-400">Hali dars qo&apos;shilmagan.</p>}
            {periods.length < MAX_PERIODS && (
              <button
                type="button"
                onClick={addPeriod}
                className="inline-flex items-center gap-1 pt-1 text-sm font-medium text-brand-600 hover:text-brand-700"
              >
                <Plus className="h-4 w-4" /> Dars qo&apos;shish
              </button>
            )}
          </div>
        </section>

        <section>
          <div className="mb-2 flex flex-wrap items-baseline justify-between gap-2">
            <h3 className="text-sm font-semibold text-slate-700">Qaysi sinf va guruhlar uchun</h3>
            <span className="text-xs text-slate-400">{selected.size} ta tanlandi</span>
          </div>
          {owners.length === 0 ? (
            <p className="text-sm text-slate-400">Sinflar topilmadi.</p>
          ) : (
            <div className="space-y-3">
              {grades.length > 1 && (
                <div className="flex flex-wrap items-center gap-1.5">
                  <span className="mr-1 text-xs text-slate-400">Butun daraja:</span>
                  {grades.map((g) => {
                    const ids = classes.filter((c) => c.grade === g).map((c) => c.id)
                    const all = ids.every((id) => selected.has(id))
                    return (
                      <button
                        key={g}
                        type="button"
                        onClick={() => toggleGrade(g)}
                        className={cn(
                          'rounded-md px-2 py-0.5 text-xs font-medium transition-colors',
                          all ? 'bg-brand-100 text-brand-800' : 'bg-slate-100 text-slate-500 hover:bg-slate-200',
                        )}
                      >
                        {g > 0 ? `${g}-sinflar` : 'Darajasiz'}
                      </button>
                    )
                  })}
                </div>
              )}
              {classes.length > 0 && <div className="flex flex-wrap gap-1.5">{classes.map(chip)}</div>}
              {tracks.length > 0 && (
                <div>
                  <p className="mb-1.5 text-xs font-medium text-slate-400">Yo&apos;nalish guruhlari</p>
                  <div className="flex flex-wrap gap-1.5">{tracks.map(chip)}</div>
                </div>
              )}
              {groups.length > 0 && (
                <div>
                  <p className="mb-1.5 text-xs font-medium text-slate-400">O&apos;quv guruhlari</p>
                  <div className="flex flex-wrap gap-1.5">{groups.map(chip)}</div>
                </div>
              )}
            </div>
          )}
        </section>

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      </div>
    </Modal>
  )
}
