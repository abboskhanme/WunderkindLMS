import { useEffect, useState } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import type { DayMenu } from '@/types'
import {
  copyMenuWeek,
  getMenuRange,
  MAX_COPY_WEEKS,
  type CopyWeekResult,
} from '@/api/services/canteen'
import { formatDate, cn } from '@/lib/utils'
import { addDaysISO } from '@/lib/weeks'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Select } from '@/components/ui/Input'

/* ==========================================================================
   Copy a week's menu (client, 2026-10-16: "Oshxona menyusini haftalik
   ko'chirish imkonini qo'shish"). docs/modules/canteen.md.

   The source is the week on screen; the target defaults to the next week.
   Every weekday goes onto the same weekday (Mon → Mon … Sun → Sun), and only
   the days the source fills are written. A filled target day stops the copy
   (the server's 409 names the days) unless "Ustiga yozish" is on.
   ========================================================================== */

interface Props {
  open: boolean
  /** Monday of the week being copied — the week the page shows. */
  sourceWeek: string
  onClose: () => void
  onCopied: (result: CopyWeekResult) => void
}

/** Monday first — the order the copy works in. */
const shortWeekdays = ['Du', 'Se', 'Cho', 'Pa', 'Ju', 'Sha', 'Yak']

const shortDate = (iso: string) => formatDate(iso).slice(0, 5)
const weekRange = (monday: string) => `${shortDate(monday)} – ${shortDate(addDaysISO(monday, 6))}`
const dishCount = (m: DayMenu) =>
  m.meals.breakfast.length + m.meals.lunch.length + m.meals.dinner.length

export function CopyWeekModal({ open, sourceWeek, onClose, onCopied }: Props) {
  const [target, setTarget] = useState(() => addDaysISO(sourceWeek, 7))
  const [weeks, setWeeks] = useState(1)
  const [overwrite, setOverwrite] = useState(false)
  const [preview, setPreview] = useState<DayMenu[] | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reset the form each time the modal opens (intended)
    setTarget(addDaysISO(sourceWeek, 7))
    setWeeks(1)
    setOverwrite(false)
    setError(null)
    setPreview(null)
    getMenuRange(sourceWeek, addDaysISO(sourceWeek, 6))
      .then((ms) => active && setPreview(ms))
      .catch(() => active && setPreview([]))
    return () => {
      active = false
    }
  }, [open, sourceWeek])

  const lastWeek = addDaysISO(target, 7 * (weeks - 1))
  const total = preview?.reduce((n, m) => n + dishCount(m), 0) ?? 0
  const filledDays = preview?.filter((m) => dishCount(m) > 0).length ?? 0
  // ISO dates compare as strings: the source week must not be one of the target weeks.
  const ontoItself = target <= sourceWeek && sourceWeek <= lastWeek
  const canSubmit = !saving && preview !== null && total > 0 && !ontoItself

  const change = (apply: () => void) => {
    apply()
    setError(null)
  }

  const submit = async () => {
    setSaving(true)
    setError(null)
    try {
      onCopied(await copyMenuWeek({ fromWeekStart: sourceWeek, toWeekStart: target, overwrite, weeks }))
    } catch (e) {
      setError(
        (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
          "Ko'chirib bo'lmadi. Qaytadan urinib ko'ring.",
      )
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="sm"
      title="Haftani ko'chirish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button onClick={submit} disabled={!canSubmit}>
            {saving ? "Ko'chirilmoqda…" : "Ko'chirish"}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div>
          <span className="mb-1 block text-sm font-medium text-slate-600">
            Qaysi haftadan: <span className="text-slate-800">{weekRange(sourceWeek)}</span>
          </span>
          {preview === null ? (
            <p className="py-3 text-center text-sm text-slate-400">Yuklanmoqda...</p>
          ) : (
            <>
              <div className="grid grid-cols-7 gap-1.5">
                {preview.map((m, i) => {
                  const n = dishCount(m)
                  return (
                    <div
                      key={m.date}
                      className={cn(
                        'rounded-xl border p-2 text-center',
                        n > 0 ? 'border-brand-100 bg-brand-50/40' : 'border-slate-200 bg-white',
                      )}
                    >
                      <p className="text-xs font-medium text-slate-500">{shortWeekdays[i]}</p>
                      <p className="text-sm font-semibold text-slate-800">{n > 0 ? n : '—'}</p>
                    </div>
                  )
                })}
              </div>
              <p className="mt-1 text-xs text-slate-400">
                {total > 0
                  ? `${filledDays} kun, jami ${total} taom ko'chiriladi`
                  : "Bu haftada menyu yo'q — ko'chiradigan narsa yo'q"}
              </p>
            </>
          )}
        </div>

        <div>
          <span className="mb-1 block text-sm font-medium text-slate-600">Qaysi haftaga</span>
          <div className="flex items-center gap-2">
            <button
              type="button"
              title="Oldingi hafta"
              aria-label="Oldingi hafta"
              onClick={() => change(() => setTarget(addDaysISO(target, -7)))}
              className="rounded-lg border border-slate-200 p-2 text-slate-500 hover:bg-slate-50"
            >
              <ChevronLeft className="h-4 w-4" />
            </button>
            <span className="min-w-[140px] text-center text-sm font-medium text-slate-700">
              {weekRange(target)}
            </span>
            <button
              type="button"
              title="Keyingi hafta"
              aria-label="Keyingi hafta"
              onClick={() => change(() => setTarget(addDaysISO(target, 7)))}
              className="rounded-lg border border-slate-200 p-2 text-slate-500 hover:bg-slate-50"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
            {target === addDaysISO(sourceWeek, 7) && (
              <span className="text-xs text-slate-400">kelasi hafta</span>
            )}
          </div>
        </div>

        <div>
          <Select
            label="Necha haftaga"
            value={weeks}
            onChange={(e) => change(() => setWeeks(Number(e.target.value)))}
          >
            {Array.from({ length: MAX_COPY_WEEKS }, (_, i) => i + 1).map((n) => (
              <option key={n} value={n}>
                {n} hafta
              </option>
            ))}
          </Select>
          {weeks > 1 && (
            <p className="mt-1 text-xs text-slate-400">
              {shortDate(target)} dan {shortDate(addDaysISO(lastWeek, 6))} gacha — har haftaga bir xil
              menyu
            </p>
          )}
        </div>

        <div className="flex items-start justify-between gap-3 rounded-xl border border-slate-200 px-3 py-2.5">
          <div className="min-w-0">
            <p className="text-sm font-medium text-slate-700">Ustiga yozish</p>
            <p className="mt-0.5 text-xs text-slate-400">
              Menyusi bor kunlar shu haftaniki bilan almashtiriladi. O'chiq bo'lsa, bunday kunlar
              bo'lganda hech narsa ko'chirilmaydi.
            </p>
          </div>
          <button
            type="button"
            role="switch"
            aria-checked={overwrite}
            aria-label="Ustiga yozish"
            onClick={() => change(() => setOverwrite(!overwrite))}
            className={cn(
              'relative mt-0.5 inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors',
              overwrite ? 'bg-brand-500' : 'bg-slate-200',
            )}
          >
            <span
              className={cn(
                'inline-block h-5 w-5 rounded-full bg-white shadow-sm transition-transform',
                overwrite ? 'translate-x-[22px]' : 'translate-x-0.5',
              )}
            />
          </button>
        </div>

        {ontoItself && (
          <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-700">
            Hafta o'zining ustiga ko'chirilmaydi — boshqa haftani tanlang.
          </p>
        )}
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      </div>
    </Modal>
  )
}
