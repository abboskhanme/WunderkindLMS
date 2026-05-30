import { useEffect, useState } from 'react'
import { Trash2, Users, User, CalendarClock, AlertTriangle } from 'lucide-react'
import type { ScheduleLesson, Subject, Teacher } from '@/types'
import { Button } from '@/components/ui/Button'
import { Select } from '@/components/ui/Input'
import { weekDays } from '@/config/constants'
import { cn } from '@/lib/utils'

export interface SlotTarget {
  day: number
  period: number
  /** Ushbu (day, period) dagi MAVJUD darslar: 0 ta (bo'sh), 1 ta (SubGroup=0 — butun sinf) yoki 2 ta (SubGroup=1 va 2). */
  lessons: ScheduleLesson[]
}

/** O'qituvchi → band soatlar. ScheduleBoard tomonidan uzatiladi. */
export type OccupiedSlots = Record<
  string,
  { day: number; period: number; className: string; templateName: string }[]
>

interface Props {
  slot: SlotTarget | null
  subjects: Subject[]
  teachers: Teacher[]
  /** Boshqa template'lardagi o'qituvchi band soatlari — ziddiyat aniqlash uchun. */
  occupiedSlots: OccupiedSlots
  /** Yangi to'liq holatni saqlash: 1 ta = butun sinf, 2 ta (G1+G2) = bo'lingan. */
  onSave: (day: number, period: number, lessons: ScheduleLesson[]) => void
  onClear: (day: number, period: number) => void
}

interface Row {
  subjectId: string
  teacherId: string
}

const emptyRow: Row = { subjectId: '', teacherId: '' }

/**
 * Jadval yonidagi inline tahrir paneli.
 * Fan + o'qituvchi tanlanadi. Tanlangan o'qituvchi shu soatda boshqa sinfda
 * band bo'lsa — ogohlantirish ko'rsatiladi va saqlash bloklanadi.
 */
export function LessonEditorPanel({ slot, subjects, teachers, occupiedSlots, onSave, onClear }: Props) {
  const [mode, setMode] = useState<'whole' | 'split'>('whole')
  const [whole, setWhole] = useState<Row>(emptyRow)
  const [g1, setG1] = useState<Row>(emptyRow)
  const [g2, setG2] = useState<Row>(emptyRow)

  useEffect(() => {
    if (!slot) return
    const lessons = slot.lessons
    const wholeLesson = lessons.find((l) => (l.subGroup ?? 0) === 0)
    const g1Lesson = lessons.find((l) => l.subGroup === 1)
    const g2Lesson = lessons.find((l) => l.subGroup === 2)
    if (g1Lesson || g2Lesson) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- katak o'zgarganda formani sinxronlash (maqsadli)
      setMode('split')
      setWhole(emptyRow)
      setG1(g1Lesson ? { subjectId: g1Lesson.subjectId, teacherId: g1Lesson.teacherId } : emptyRow)
      setG2(g2Lesson ? { subjectId: g2Lesson.subjectId, teacherId: g2Lesson.teacherId } : emptyRow)
    } else if (wholeLesson) {
      setMode('whole')
      setWhole({ subjectId: wholeLesson.subjectId, teacherId: wholeLesson.teacherId })
      setG1(emptyRow)
      setG2(emptyRow)
    } else {
      setMode('whole')
      setWhole(emptyRow)
      setG1(emptyRow)
      setG2(emptyRow)
    }
  }, [slot])

  if (!slot) {
    return (
      <div className="rounded-2xl border border-dashed border-slate-200 bg-white p-6 text-center text-sm text-slate-400">
        <CalendarClock className="mx-auto mb-2 h-6 w-6 text-slate-300" />
        Soatni tanlash uchun jadvaldagi katakni bosing.
      </div>
    )
  }

  const teachersFor = (subjectId: string) =>
    subjectId ? teachers.filter((t) => t.subjectIds.includes(subjectId)) : []

  /** Berilgan o'qituvchi joriy (day, period) da boshqa template'da band ekanligini aniqlaydi. */
  const conflictOf = (teacherId: string) => {
    if (!teacherId) return null
    return (
      occupiedSlots[teacherId]?.find((s) => s.day === slot.day && s.period === slot.period) ?? null
    )
  }

  const updateRow = (row: Row, setter: (r: Row) => void, key: 'subjectId' | 'teacherId', val: string) => {
    if (key === 'subjectId') {
      const next: Row = { subjectId: val, teacherId: '' }
      if (val && row.teacherId && teachersFor(val).some((t) => t.id === row.teacherId))
        next.teacherId = row.teacherId
      setter(next)
    } else {
      setter({ ...row, [key]: val })
    }
  }

  const wholeConflict = conflictOf(whole.teacherId)
  const g1Conflict = conflictOf(g1.teacherId)
  const g2Conflict = conflictOf(g2.teacherId)
  const hasAnyConflict =
    mode === 'whole' ? !!wholeConflict : !!g1Conflict || !!g2Conflict

  const handleSave = () => {
    if (hasAnyConflict) return
    if (mode === 'whole') {
      if (!whole.subjectId) return
      onSave(slot.day, slot.period, [
        { day: slot.day, period: slot.period, subjectId: whole.subjectId, teacherId: whole.teacherId, subGroup: 0 },
      ])
    } else {
      const out: ScheduleLesson[] = []
      if (g1.subjectId)
        out.push({ day: slot.day, period: slot.period, subjectId: g1.subjectId, teacherId: g1.teacherId, subGroup: 1 })
      if (g2.subjectId)
        out.push({ day: slot.day, period: slot.period, subjectId: g2.subjectId, teacherId: g2.teacherId, subGroup: 2 })
      if (out.length === 0) return
      onSave(slot.day, slot.period, out)
    }
  }

  const hasLesson = slot.lessons.length > 0
  const canSave =
    !hasAnyConflict &&
    (mode === 'whole' ? !!whole.subjectId : !!g1.subjectId || !!g2.subjectId)

  return (
    <div className="rounded-2xl border border-slate-200/80 bg-white p-5 shadow-sm">
      {/* Qaysi soat yaratilayotgani */}
      <div className="mb-4 rounded-xl bg-brand-50 px-4 py-3">
        <p className="text-[11px] font-medium uppercase tracking-wide text-brand-500">
          {hasLesson ? 'Tahrirlanmoqda' : 'Yaratilmoqda'}
        </p>
        <p className="text-base font-semibold text-brand-700">
          {weekDays[slot.day]} · {slot.period}-dars
        </p>
      </div>

      <div className="space-y-4">
        {/* Rejim tanlovi */}
        <div className="flex gap-1 rounded-lg bg-slate-100 p-1">
          <button
            type="button"
            onClick={() => setMode('whole')}
            className={cn(
              'flex-1 rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
              mode === 'whole' ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500',
            )}
          >
            <Users className="mr-1 inline h-4 w-4" /> Butun sinf
          </button>
          <button
            type="button"
            onClick={() => setMode('split')}
            className={cn(
              'flex-1 rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
              mode === 'split' ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500',
            )}
          >
            <User className="mr-1 inline h-4 w-4" /> Guruhga
          </button>
        </div>

        {mode === 'whole' ? (
          <RowFields
            subjects={subjects}
            row={whole}
            onChange={(k, v) => updateRow(whole, setWhole, k, v)}
            teachersFor={teachersFor}
            conflict={wholeConflict}
          />
        ) : (
          <div className="space-y-3">
            <div className="rounded-lg border border-sky-200 bg-sky-50/40 p-3">
              <h4 className="mb-2 inline-flex items-center gap-1 rounded bg-sky-200 px-2 py-0.5 text-xs font-semibold text-sky-800">
                1-guruh
              </h4>
              <RowFields
                subjects={subjects}
                row={g1}
                onChange={(k, v) => updateRow(g1, setG1, k, v)}
                teachersFor={teachersFor}
                conflict={g1Conflict}
              />
            </div>
            <div className="rounded-lg border border-violet-200 bg-violet-50/40 p-3">
              <h4 className="mb-2 inline-flex items-center gap-1 rounded bg-violet-200 px-2 py-0.5 text-xs font-semibold text-violet-800">
                2-guruh
              </h4>
              <RowFields
                subjects={subjects}
                row={g2}
                onChange={(k, v) => updateRow(g2, setG2, k, v)}
                teachersFor={teachersFor}
                conflict={g2Conflict}
              />
            </div>
          </div>
        )}

        <div className="flex flex-col gap-2">
          <Button onClick={handleSave} disabled={!canSave} className="w-full">
            {hasLesson ? 'Saqlash' : 'Yaratish'}
          </Button>
          {hasLesson && (
            <Button variant="danger" onClick={() => onClear(slot.day, slot.period)} className="w-full">
              <Trash2 className="h-4 w-4" /> Tozalash
            </Button>
          )}
        </div>

        <p className="text-xs text-slate-400">
          Saqlagandan so'ng boshqa katakni bosib keyingi soatni yarataverasiz — panel ochiqligicha
          qoladi. Bo'lingan dars faqat tegishli guruh o'quvchilariga ko'rinadi.
        </p>
      </div>
    </div>
  )
}

interface ConflictInfo {
  className: string
  templateName: string
}

function RowFields({
  subjects,
  row,
  onChange,
  teachersFor,
  conflict,
}: {
  subjects: Subject[]
  row: Row
  onChange: (key: 'subjectId' | 'teacherId', value: string) => void
  teachersFor: (subjectId: string) => Teacher[]
  conflict: ConflictInfo | null
}) {
  const avail = teachersFor(row.subjectId)
  return (
    <div className="space-y-3">
      <Select label="Fan" value={row.subjectId} onChange={(e) => onChange('subjectId', e.target.value)}>
        <option value="">Tanlanmagan</option>
        {subjects.map((s) => (
          <option key={s.id} value={s.id}>
            {s.name}
          </option>
        ))}
      </Select>

      <Select
        label="O'qituvchi"
        value={row.teacherId}
        disabled={!row.subjectId}
        onChange={(e) => onChange('teacherId', e.target.value)}
      >
        <option value="">Tanlanmagan</option>
        {avail.map((t) => (
          <option key={t.id} value={t.id}>
            {t.fullName}
          </option>
        ))}
      </Select>

      {row.subjectId && avail.length === 0 && (
        <p className="text-xs text-amber-600">Bu fanga biriktirilgan o'qituvchi yo'q.</p>
      )}

      {/* Ziddiyat ogohlantirilmasi */}
      {conflict && (
        <div className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2.5">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-red-500" />
          <div className="text-xs text-red-700">
            <p className="font-semibold">Ziddiyat topildi!</p>
            <p className="mt-0.5">
              Bu o'qituvchi{' '}
              <span className="font-semibold">{conflict.className}-sinf</span>
              {' '}da shu soatda allaqachon dars bor{' '}
              <span className="text-red-500">({conflict.templateName})</span>.
            </p>
            <p className="mt-1 font-medium">Boshqa o'qituvchi tanlang.</p>
          </div>
        </div>
      )}
    </div>
  )
}
