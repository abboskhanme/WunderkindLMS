import { useEffect, useState } from 'react'
import type { AbsenceReason, JournalEntry } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { cn } from '@/lib/utils'

interface Props {
  open: boolean
  studentName: string
  dateLabel: string
  entry: JournalEntry | null
  reasons: AbsenceReason[]
  onClose: () => void
  /** Baho, davomat, uyga vazifa (0/1/2), xulq (0/1/2) va o'zlashtirish %ni birga saqlaydi. */
  onSave: (
    grade: number | null,
    reasonId: string | null,
    homework: number,
    behavior: number,
    mastery: number | null,
  ) => void
  onClear: () => void
}

const grades = [1, 2, 3, 4, 5]

export function JournalCellModal({
  open,
  studentName,
  dateLabel,
  entry,
  reasons,
  onClose,
  onSave,
  onClear,
}: Props) {
  const [grade, setGrade] = useState<number | null>(null)
  const [reasonId, setReasonId] = useState<string | null>(null)
  const [homework, setHomework] = useState(0)
  const [behavior, setBehavior] = useState(0)
  const [mastery, setMastery] = useState<number | ''>('')

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda joriy katak qiymatini yuklash (maqsadli)
    setGrade(entry?.grade ?? null)
    setReasonId(entry?.reasonId ?? null)
    setHomework(entry?.homework ?? 0)
    setBehavior(entry?.behavior ?? 0)
    setMastery(entry?.mastery ?? '')
  }, [open, entry])

  const toggle = (cur: number, set: (v: number) => void, val: number) =>
    set(cur === val ? 0 : val)

  const lateReasons = reasons.filter((r) => r.isLate)
  const absentReasons = reasons.filter((r) => !r.isLate)
  const selectedLate = reasonId != null && lateReasons.some((r) => r.id === reasonId)

  const toggleReason = (id: string) => setReasonId((cur) => (cur === id ? null : id))

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="sm"
      title={studentName}
      footer={
        <>
          {entry && (
            <Button variant="danger" className="mr-auto" onClick={onClear}>
              Tozalash
            </Button>
          )}
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button onClick={() => onSave(grade, reasonId, homework, behavior, mastery === '' ? null : mastery)}>
            Saqlash
          </Button>
        </>
      }
    >
      {/* Tartibli, guruhlangan ko'rinish (mijoz, 2026-09-23: "sal tartibli bo'lsin"):
          har bo'lim — kulrang blok, tanlovlar teng kenglikdagi segmentlar. Mantiq o'zgarmagan. */}
      <p className="mb-4 inline-flex rounded-full bg-slate-100 px-2.5 py-1 text-xs font-medium text-slate-500">
        {dateLabel}
      </p>

      <div className="space-y-3">
        <Section title="Baho">
          <div className="grid grid-cols-5 gap-2">
            {grades.map((g) => (
              <button
                key={g}
                type="button"
                onClick={() => setGrade((cur) => (cur === g ? null : g))}
                className={cn(
                  'h-11 rounded-xl text-base font-semibold transition-colors',
                  grade === g ? gradeActive(g) : 'bg-white text-slate-700 ring-1 ring-slate-200 hover:bg-slate-100',
                )}
              >
                {g}
              </button>
            ))}
          </div>
        </Section>

        <Section
          title="Davomat"
          hint={
            selectedLate
              ? "Kech kelgan — darsda qatnashgan, baho ham qo'yish mumkin."
              : reasonId
                ? undefined
                : 'Hech biri tanlanmasa — keldi.'
          }
        >
          {reasons.length === 0 ? (
            <p className="text-xs text-slate-400">Sabablar yo'q — Sozlamalarda qo'shing</p>
          ) : (
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
              <Chip active={reasonId == null} tone="green" onClick={() => setReasonId(null)}>
                Keldi
              </Chip>
              {lateReasons.map((r) => (
                <Chip key={r.id} active={reasonId === r.id} tone="amber" onClick={() => toggleReason(r.id)}>
                  {r.name}
                </Chip>
              ))}
              {absentReasons.map((r) => (
                <Chip key={r.id} active={reasonId === r.id} tone="red" onClick={() => toggleReason(r.id)}>
                  {r.name}
                </Chip>
              ))}
            </div>
          )}
        </Section>

        <div className="grid grid-cols-2 gap-3">
          <Section title="Uyga vazifa">
            <div className="grid grid-cols-2 gap-2">
              <Chip active={homework === 1} tone="green" onClick={() => toggle(homework, setHomework, 1)}>
                Qildi
              </Chip>
              <Chip active={homework === 2} tone="red" onClick={() => toggle(homework, setHomework, 2)}>
                Qilmadi
              </Chip>
            </div>
          </Section>
          <Section title="Xulq">
            <div className="grid grid-cols-2 gap-2">
              <Chip active={behavior === 1} tone="green" onClick={() => toggle(behavior, setBehavior, 1)}>
                Yaxshi
              </Chip>
              <Chip active={behavior === 2} tone="red" onClick={() => toggle(behavior, setBehavior, 2)}>
                Yomon
              </Chip>
            </div>
          </Section>
        </div>

        <Section title="Darsni o'zlashtirish">
          <div className="flex items-center gap-2">
            <div className="relative w-28">
              <input
                type="number"
                min={0}
                max={100}
                inputMode="numeric"
                placeholder="0–100"
                value={mastery}
                onChange={(e) => {
                  const v = e.target.value
                  if (v === '') return setMastery('')
                  const n = Math.max(0, Math.min(100, Number(v)))
                  setMastery(Number.isNaN(n) ? '' : n)
                }}
                className="h-10 w-full rounded-xl bg-white pl-3 pr-7 text-sm outline-none ring-1 ring-slate-200 focus:ring-brand-400"
              />
              <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-sm text-slate-400">%</span>
            </div>
            {[50, 75, 100].map((v) => (
              <button
                key={v}
                type="button"
                onClick={() => setMastery(mastery === v ? '' : v)}
                className={cn(
                  'h-10 rounded-xl px-3 text-xs font-medium transition-colors',
                  mastery === v ? 'bg-brand-600 text-white' : 'bg-white text-slate-600 ring-1 ring-slate-200 hover:bg-slate-100',
                )}
              >
                {v}%
              </button>
            ))}
          </div>
        </Section>
      </div>
    </Modal>
  )
}

/* ---------- kichik qismlar ---------- */

function Section({ title, hint, children }: { title: string; hint?: string; children: React.ReactNode }) {
  return (
    <section className="rounded-2xl bg-slate-50 p-3">
      <p className="mb-2 text-[11px] font-semibold uppercase tracking-wide text-slate-400">{title}</p>
      {children}
      {hint && <p className="mt-2 text-xs text-slate-400">{hint}</p>}
    </section>
  )
}

const CHIP_TONES = {
  green: 'bg-emerald-500 text-white ring-emerald-500',
  amber: 'bg-amber-400 text-white ring-amber-400',
  red: 'bg-red-500 text-white ring-red-500',
} as const

function Chip({
  active,
  tone,
  onClick,
  children,
}: {
  active: boolean
  tone: keyof typeof CHIP_TONES
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'h-10 truncate rounded-xl px-2 text-sm font-medium ring-1 transition-colors',
        active ? CHIP_TONES[tone] : 'bg-white text-slate-700 ring-slate-200 hover:bg-slate-100',
      )}
    >
      {children}
    </button>
  )
}

/** Tanlangan baho rangi — jadvaldagi baho ranglari bilan bir xil ma'noda. */
function gradeActive(g: number): string {
  if (g >= 5) return 'bg-emerald-500 text-white'
  if (g === 4) return 'bg-brand-600 text-white'
  if (g === 3) return 'bg-amber-400 text-white'
  return 'bg-red-500 text-white'
}
