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
      <p className="mb-4 text-sm text-slate-400">{dateLabel}</p>

      <div className="space-y-4">
        <div>
          <p className="mb-2 text-sm font-medium text-slate-600">Baho</p>
          <div className="flex gap-2">
            {grades.map((g) => (
              <button
                key={g}
                type="button"
                onClick={() => setGrade((cur) => (cur === g ? null : g))}
                className={cn(
                  'flex h-10 w-10 items-center justify-center rounded-lg border text-sm font-semibold transition-colors',
                  grade === g
                    ? 'border-brand-500 bg-brand-600 text-white'
                    : 'border-slate-200 text-slate-700 hover:bg-slate-50',
                )}
              >
                {g}
              </button>
            ))}
          </div>
        </div>

        {lateReasons.length > 0 && (
          <div>
            <p className="mb-2 text-sm font-medium text-slate-600">Kech keldi</p>
            <div className="flex flex-wrap gap-2">
              {lateReasons.map((r) => (
                <button
                  key={r.id}
                  type="button"
                  onClick={() => toggleReason(r.id)}
                  className={cn(
                    'rounded-lg border px-3 py-2 text-sm font-medium transition-colors',
                    reasonId === r.id
                      ? 'border-amber-400 bg-amber-50 text-amber-700'
                      : 'border-slate-200 text-slate-700 hover:bg-slate-50',
                  )}
                >
                  {r.name}
                </button>
              ))}
            </div>
            {selectedLate && (
              <p className="mt-1.5 text-xs text-amber-600">
                Kech kelgan — darsda qatnashgan, baho ham qo'yishingiz mumkin.
              </p>
            )}
          </div>
        )}

        <div>
          <p className="mb-2 text-sm font-medium text-slate-600">Davomat (kelmadi)</p>
          {absentReasons.length === 0 ? (
            <p className="text-xs text-slate-400">Sabablar yo'q — Sozlamalarda qo'shing</p>
          ) : (
            <div className="flex flex-wrap gap-2">
              {absentReasons.map((r) => (
                <button
                  key={r.id}
                  type="button"
                  onClick={() => toggleReason(r.id)}
                  className={cn(
                    'rounded-lg border px-3 py-2 text-sm font-medium transition-colors',
                    reasonId === r.id
                      ? 'border-red-400 bg-red-50 text-red-600'
                      : 'border-slate-200 text-slate-700 hover:bg-slate-50',
                  )}
                >
                  {r.name}
                </button>
              ))}
            </div>
          )}
        </div>

        {/* Uyga vazifa — har o'quvchiga (qildi/qilmadi) */}
        <div>
          <p className="mb-2 text-sm font-medium text-slate-600">Uyga vazifa</p>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => toggle(homework, setHomework, 1)}
              className={cn(
                'rounded-lg border px-3 py-2 text-sm font-medium transition-colors',
                homework === 1
                  ? 'border-emerald-400 bg-emerald-50 text-emerald-700'
                  : 'border-slate-200 text-slate-700 hover:bg-slate-50',
              )}
            >
              Qildi
            </button>
            <button
              type="button"
              onClick={() => toggle(homework, setHomework, 2)}
              className={cn(
                'rounded-lg border px-3 py-2 text-sm font-medium transition-colors',
                homework === 2
                  ? 'border-red-400 bg-red-50 text-red-600'
                  : 'border-slate-200 text-slate-700 hover:bg-slate-50',
              )}
            >
              Qilmadi
            </button>
          </div>
        </div>

        {/* Xulq — har o'quvchiga (yaxshi/yomon) */}
        <div>
          <p className="mb-2 text-sm font-medium text-slate-600">Xulq</p>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => toggle(behavior, setBehavior, 1)}
              className={cn(
                'rounded-lg border px-3 py-2 text-sm font-medium transition-colors',
                behavior === 1
                  ? 'border-emerald-400 bg-emerald-50 text-emerald-700'
                  : 'border-slate-200 text-slate-700 hover:bg-slate-50',
              )}
            >
              Yaxshi
            </button>
            <button
              type="button"
              onClick={() => toggle(behavior, setBehavior, 2)}
              className={cn(
                'rounded-lg border px-3 py-2 text-sm font-medium transition-colors',
                behavior === 2
                  ? 'border-red-400 bg-red-50 text-red-600'
                  : 'border-slate-200 text-slate-700 hover:bg-slate-50',
              )}
            >
              Yomon
            </button>
          </div>
        </div>

        {/* O'zlashtirish foizi — shu darsni necha % o'zlashtirdi */}
        <div>
          <p className="mb-2 text-sm font-medium text-slate-600">Darsni o'zlashtirish (%)</p>
          <div className="flex items-center gap-2">
            <input
              type="number"
              min={0}
              max={100}
              inputMode="numeric"
              placeholder="0-100"
              value={mastery}
              onChange={(e) => {
                const v = e.target.value
                if (v === '') return setMastery('')
                const n = Math.max(0, Math.min(100, Number(v)))
                setMastery(Number.isNaN(n) ? '' : n)
              }}
              className="w-28 rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400"
            />
            <span className="text-sm text-slate-400">%</span>
            {mastery !== '' && (
              <button
                type="button"
                onClick={() => setMastery('')}
                className="text-xs text-slate-400 hover:text-slate-600"
              >
                Tozalash
              </button>
            )}
          </div>
        </div>
      </div>
    </Modal>
  )
}
