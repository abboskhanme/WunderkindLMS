/**
 * Imtihon yaratish / tahrirlash (§3.2 screen 6, §6.3 `ExamUpsertDto`; unit C3).
 *
 * TWO KINDS, ONE FORM. `kind` and `delivery` are immutable after create
 * (§6.3), and each kind has exactly one delivery here:
 *   · Blok test → `manual` (§13 Q4: paper exam, scores typed or imported);
 *   · Qabul imtihoni → `online` (§2.1: the engine draws and grades).
 * The admission option is offered only to a user who also holds `admission`,
 * because its section picker reads the question banks — a read gated by that
 * key (§4.3). Without it the option would open onto a 403.
 *
 * CLASSES ARE PARTICIPANTS, NOT A COLUMN. `ExamUpsertDto` has no class list;
 * "sinflar" becomes `POST /exams/{id}/participants { classIds }` after the
 * exam is saved. It is idempotent (already-added pupils are `skipped`), so the
 * edit form simply offers "add more classes". If the exam saves and the
 * classes do not, the form stays open on the now-existing exam, so a retry
 * adds the classes instead of creating a second exam.
 *
 * FROZEN SECTIONS. Once an exam leaves `draft` its sections cannot change
 * (§5.5 — the server answers 409). The form then shows them read-only and
 * sends them back unchanged.
 */
import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { AlertTriangle, ArrowDown, ArrowUp, Loader2, Lock, Plus, RefreshCw, X } from 'lucide-react'
import {
  addParticipants,
  createExam,
  examsErrorMessage,
  getExam,
  listExamTypes,
  updateExam,
  type Exam,
  type ExamKind,
  type ExamSectionInput,
  type ExamType,
  type ExamUpsert,
} from '@/api/services/exams'
import { getSubjects } from '@/api/services/subjects'
import { getClasses } from '@/api/services/classes'
import type { SchoolClass, Subject } from '@/types'
import { Button } from '@/components/ui/Button'
import { DatePicker } from '@/components/ui/DatePicker'
import { Input, Select, Time24Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'
import { BankSectionsEditor } from './BankSectionsEditor'
import {
  GRADES,
  MAX_QUESTIONS_PER_EXAM,
  ONLINE_EXAMS_ENABLED,
  examStatusLabel,
  formatPoints,
  gradeLabel,
  kindLabel,
} from './examLabels'
import {
  MAX_SECTION_SCORE,
  move,
  newSectionDraft,
  parseDecimal,
  parsePositiveInt,
  type SectionDraft,
} from './examForm'

interface Props {
  /** `null` — a new exam. */
  examId: string | null
  /** Holds `admission` as well — may create an online admission exam. */
  canAdmission: boolean
  onClose: () => void
  /** Everything was written: close and refresh. */
  onSaved: (message: string) => void
  /** Something was written but the form stays open (exam saved, classes failed). */
  onChanged: () => void
}

interface Lookups {
  types: ExamType[]
  subjects: Subject[]
  classes: SchoolClass[]
}

type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; lookups: Lookups; exam: Exam | null }

export function ExamFormModal({ examId, canAdmission, onClose, onSaved, onChanged }: Props) {
  const [state, setState] = useState<LoadState>({ status: 'loading' })
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let cancelled = false
    Promise.all([
      listExamTypes(),
      getSubjects(),
      getClasses(),
      examId ? getExam(examId) : Promise.resolve(null),
    ])
      .then(([types, subjects, classes, exam]) => {
        if (!cancelled) setState({ status: 'ready', lookups: { types, subjects, classes }, exam })
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setState({
            status: 'error',
            message: examsErrorMessage(err, examId ? 'exam.load' : 'lookup.load'),
          })
        }
      })
    return () => {
      cancelled = true
    }
  }, [examId, attempt])

  const title = examId ? 'Imtihonni tahrirlash' : "Imtihon qo'shish"

  if (state.status !== 'ready') {
    return (
      <Modal
        open
        onClose={onClose}
        title={title}
        size="lg"
        footer={
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
        }
      >
        {state.status === 'loading' ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="flex flex-col items-center gap-3 py-10 text-center">
            <p className="text-sm font-medium text-slate-600">Formani ochib bo'lmadi</p>
            <p className="max-w-md text-sm text-slate-400">{state.message}</p>
            <Button
              variant="secondary"
              onClick={() => {
                setState({ status: 'loading' })
                setAttempt((n) => n + 1)
              }}
            >
              <RefreshCw className="h-4 w-4" /> Qayta urinish
            </Button>
          </div>
        )}
      </Modal>
    )
  }

  return (
    <ExamForm
      title={title}
      exam={state.exam}
      lookups={state.lookups}
      canAdmission={canAdmission}
      onClose={onClose}
      onSaved={onSaved}
      onChanged={onChanged}
    />
  )
}

// ---------------------------------------------------------------------------

interface FormProps {
  title: string
  exam: Exam | null
  lookups: Lookups
  canAdmission: boolean
  onClose: () => void
  onSaved: (message: string) => void
  onChanged: () => void
}

const field =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

/** `yyyy-MM-ddTHH:mm:ss` → its date and `HH:mm` halves. */
function splitWallClock(value: string | null): { date: string; time: string } {
  if (!value) return { date: '', time: '' }
  return { date: value.slice(0, 10), time: value.slice(11, 16) }
}

function draftsFrom(exam: Exam | null): SectionDraft[] {
  if (!exam) return [newSectionDraft()]
  return [...exam.sections]
    .sort((a, b) => a.order - b.order)
    .map((s) =>
      newSectionDraft({
        subjectId: s.subjectId,
        maxScore: s.maxScore === null ? '' : formatPoints(s.maxScore),
        bankId: s.bankId ?? '',
        questionCount: s.questionCount === null ? '' : String(s.questionCount),
      }),
    )
}

function ExamForm({ title, exam, lookups, canAdmission, onClose, onSaved, onChanged }: FormProps) {
  /** Set once the exam exists on the server — including right after a create. */
  const [saved, setSaved] = useState<Exam | null>(exam)
  const isNew = saved === null

  const [kind, setKind] = useState<ExamKind>(exam?.kind ?? 'block')
  const [name, setName] = useState(exam?.title ?? '')
  const [examTypeId, setExamTypeId] = useState(exam?.examTypeId ?? '')
  const [examDate, setExamDate] = useState(exam?.examDate ?? '')

  const [grade, setGrade] = useState(exam?.grade === null || exam?.grade === undefined ? '' : String(exam.grade))
  const opens0 = splitWallClock(exam?.opensAt ?? null)
  const closes0 = splitWallClock(exam?.closesAt ?? null)
  const [opensDate, setOpensDate] = useState(opens0.date)
  const [opensTime, setOpensTime] = useState(opens0.time || '08:00')
  const [closesDate, setClosesDate] = useState(closes0.date)
  const [closesTime, setClosesTime] = useState(closes0.time || '18:00')
  const [timeLimit, setTimeLimit] = useState(exam?.timeLimitMin ? String(exam.timeLimitMin) : '')
  const [timeLimitTouched, setTimeLimitTouched] = useState(Boolean(exam?.timeLimitMin))

  const [sections, setSections] = useState<SectionDraft[]>(() => draftsFrom(exam))
  const [classIds, setClassIds] = useState<Set<string>>(() => new Set())

  const [submitted, setSubmitted] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  /** §5.5: sections are frozen once the exam leaves `draft`. */
  const frozen = saved !== null && saved.status !== 'draft'
  const isBlock = kind === 'block'
  /**
   * Natija qo'lda kiritiladimi (qog'ozda o'tkaziladigan imtihon). Onlayn test
   * (ochiq havola, qurilma qulfi — admission-and-testing.md §7, B3) 2026-09-22 da
   * KEYINGA qoldirildi, shuning uchun hozircha qabul imtihoni ham qog'ozda o'tadi.
   * Onlayn qaytganda `ONLINE_EXAMS_ENABLED` ni yoqish kifoya.
   */
  const isManual = isBlock || !ONLINE_EXAMS_ENABLED

  const subjectsById = useMemo(
    () => new Map(lookups.subjects.map((s) => [s.id, s])),
    [lookups.subjects],
  )

  const typeOptions = lookups.types.filter((t) => t.isActive || t.id === examTypeId)

  const classOptions = useMemo(
    () =>
      lookups.classes
        .filter((c) => !c.isArchived)
        .sort((a, b) => a.grade - b.grade || a.name.localeCompare(b.name, 'uz')),
    [lookups.classes],
  )

  // ---- validation (input hygiene; the server remains the judge) ----

  const problem = useMemo((): string | null => {
    if (!name.trim()) return 'Imtihon nomini kiriting'
    if (isBlock && isNew && classIds.size === 0) return 'Kamida bitta sinfni tanlang'
    if (!isBlock && grade === '') return 'Qaysi sinfga qabul ekanini tanlang'
    if (isManual) {
      if (!examDate) return 'Imtihon sanasini tanlang'
    } else {
      if (!opensDate || !opensTime || !closesDate || !closesTime) {
        return "Imtihon ochiq bo'ladigan vaqt oralig'ini to'liq kiriting"
      }
      if (`${closesDate}T${closesTime}` <= `${opensDate}T${opensTime}`) {
        return "Yopilish vaqti ochilish vaqtidan keyin bo'lishi kerak"
      }
      const minutes = parsePositiveInt(timeLimit)
      if (minutes === null || Number.isNaN(minutes)) return 'Imtihon vaqtini daqiqada kiriting'
    }
    if (frozen) return null
    if (sections.length === 0) return "Kamida bitta fan qo'shing"
    const seen = new Set<string>()
    for (const [i, s] of sections.entries()) {
      const n = i + 1
      if (isManual) {
        if (!s.subjectId) return `${n}-qatorda fanni tanlang`
        if (seen.has(s.subjectId)) return `${n}-qatordagi fan takrorlangan`
        seen.add(s.subjectId)
        const max = parseDecimal(s.maxScore)
        if (max === null) return `${n}-qatorda maksimal ballni kiriting`
        if (Number.isNaN(max) || max <= 0 || max > MAX_SECTION_SCORE) {
          return `${n}-qatordagi maksimal ball 0 dan katta son bo'lsin (ko'pi bilan 2 xona kasr)`
        }
      } else {
        if (!s.bankId) return `${n}-qatorda test bazasini tanlang`
        const count = parsePositiveInt(s.questionCount)
        if (count === null || Number.isNaN(count)) return `${n}-qatorda savollar sonini kiriting`
      }
    }
    return null
  }, [
    name,
    isBlock,
    isManual,
    examDate,
    isNew,
    classIds,
    grade,
    opensDate,
    opensTime,
    closesDate,
    closesTime,
    timeLimit,
    frozen,
    sections,
  ])

  /** §13 Q7 — a warning, not a block: the server refuses at publish, not at save. */
  const totalQuestions = isManual
    ? 0
    : sections.reduce((sum, s) => {
        const n = parsePositiveInt(s.questionCount)
        return n && !Number.isNaN(n) ? sum + n : sum
      }, 0)

  // ---- payload ----

  const buildSections = (): ExamSectionInput[] => {
    if (frozen && saved) {
      return [...saved.sections]
        .sort((a, b) => a.order - b.order)
        .map((s) => ({
          subjectId: s.subjectId,
          bankId: s.bankId,
          questionCount: s.questionCount,
          maxScore: s.maxScore,
          order: s.order,
        }))
    }
    return sections.map((s, order) =>
      isManual
        ? { subjectId: s.subjectId, bankId: null, questionCount: null, maxScore: parseDecimal(s.maxScore), order }
        : {
            subjectId: s.subjectId,
            bankId: s.bankId,
            questionCount: parsePositiveInt(s.questionCount),
            maxScore: null,
            order,
          },
    )
  }

  const buildPayload = (): ExamUpsert => ({
    title: name.trim(),
    kind,
    delivery: isManual ? 'manual' : 'online',
    examTypeId: examTypeId || null,
    grade: isBlock ? null : Number(grade),
    examDate: isManual ? examDate : null,
    opensAt: isManual ? null : `${opensDate}T${opensTime}:00`,
    closesAt: isManual ? null : `${closesDate}T${closesTime}:00`,
    timeLimitMin: isManual ? null : parsePositiveInt(timeLimit),
    sections: buildSections(),
  })

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setSubmitted(true)
    if (problem || saving) return
    setSaving(true)
    setError(null)

    const creating = saved === null
    let exam: Exam
    try {
      exam = saved ? await updateExam(saved.id, buildPayload()) : await createExam(buildPayload())
    } catch (err) {
      setError(examsErrorMessage(err, 'exam.save'))
      setSaving(false)
      return
    }
    setSaved(exam)

    let message = creating ? `"${exam.title}" imtihoni yaratildi` : `"${exam.title}" saqlandi`
    if (isBlock && classIds.size > 0) {
      try {
        const result = await addParticipants(exam.id, { classIds: [...classIds] })
        message += ` · ${result.added} ta o'quvchi qo'shildi`
        if (result.skipped > 0) message += ` (${result.skipped} tasi avvaldan bor edi)`
      } catch (err) {
        // The exam exists now; keep the form open on it so a retry adds the
        // classes instead of creating a second exam.
        onChanged()
        setError(
          `Imtihon saqlandi, lekin sinflar qo'shilmadi: ${examsErrorMessage(err, 'participants.add')}. ` +
            "Qayta \"Saqlash\"ni bosing — imtihon qayta yaratilmaydi.",
        )
        setSaving(false)
        return
      }
    }
    setSaving(false)
    onSaved(message)
  }

  // ---- sections (manual) ----

  const patchSection = (index: number, patch: Partial<SectionDraft>) =>
    setSections((prev) => prev.map((s, i) => (i === index ? { ...s, ...patch } : s)))

  const subjectOptions = (index: number) => {
    const taken = new Set(sections.filter((_, i) => i !== index).map((s) => s.subjectId))
    const current = sections[index]?.subjectId
    return lookups.subjects
      .filter((s) => s.id === current || (s.isActive !== false && !taken.has(s.id)))
      .sort((a, b) => a.name.localeCompare(b.name, 'uz'))
  }

  const changeKind = (next: ExamKind) => {
    if (next === kind) return
    setKind(next)
    setSections([newSectionDraft()])
    setSubmitted(false)
  }

  const changeGrade = (value: string) => {
    setGrade(value)
    // Banks belong to one grade — the chosen ones no longer apply.
    setSections([newSectionDraft()])
    if (!timeLimitTouched) setTimeLimit('')
  }

  const toggleClass = (id: string) =>
    setClassIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  return (
    <Modal
      open
      onClose={onClose}
      title={title}
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button type="submit" form="exam-form" disabled={saving}>
            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
            Saqlash
          </Button>
        </>
      }
    >
      <form id="exam-form" onSubmit={(e) => void submit(e)} className="space-y-5">
        {/* Kind — chosen once. */}
        {isNew ? (
          canAdmission && (
            <div className="inline-flex rounded-lg bg-slate-100 p-1">
              {(['block', 'admission'] as const).map((k) => (
                <button
                  key={k}
                  type="button"
                  onClick={() => changeKind(k)}
                  className={cn(
                    'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                    k === kind ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                  )}
                >
                  {kindLabel(k)}
                </button>
              ))}
            </div>
          )
        ) : (
          <p className="flex flex-wrap items-center gap-2 text-sm text-slate-500">
            <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
              {kindLabel(kind)}
            </span>
            {saved && <span>Holati: {examStatusLabel(saved.status)}</span>}
            {saved?.participantCount !== undefined && (
              <span>· {saved.participantCount} ta ishtirokchi</span>
            )}
          </p>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="sm:col-span-2">
            <Input
              label="Nomi"
              required
              maxLength={200}
              placeholder={isBlock ? 'masalan: 1-chorak blok testi, 9-sinflar' : "masalan: 5-sinfga qabul, 2027"}
              value={name}
              onChange={(e) => setName(e.target.value)}
              className={cn(submitted && !name.trim() && 'border-red-300')}
            />
          </div>
          <Select label="Imtihon turi" value={examTypeId} onChange={(e) => setExamTypeId(e.target.value)}>
            <option value="">Tanlanmagan</option>
            {typeOptions.map((t) => (
              <option key={t.id} value={t.id}>
                {t.isActive ? t.name : `${t.name} (faol emas)`}
              </option>
            ))}
          </Select>

          {isManual && (
            <DatePicker
              label="Imtihon sanasi"
              required
              value={examDate}
              onChange={setExamDate}
              invalid={submitted && !examDate}
            />
          )}
          {!isBlock && (
            <Select
              label="Qaysi sinfga qabul"
              required
              value={grade}
              onChange={(e) => changeGrade(e.target.value)}
              disabled={!isNew}
              className={cn(submitted && grade === '' && 'border-red-300')}
            >
              <option value="">Tanlang</option>
              {GRADES.map((g) => (
                <option key={g} value={String(g)}>
                  {gradeLabel(g)}
                </option>
              ))}
            </Select>
          )}
        </div>

        {/* Online window (§5.5: required for `online`). */}
        {!isManual && (
          <div className="grid gap-4 sm:grid-cols-3">
            <div>
              <span className="mb-1 block text-sm font-medium text-slate-600">
                Ochiladi<span className="text-red-500"> *</span>
              </span>
              <div className="flex items-center gap-2">
                <DatePicker value={opensDate} onChange={setOpensDate} ariaLabel="Ochilish sanasi" className="w-36" invalid={submitted && !opensDate} />
                <Time24Input value={opensTime} onChange={setOpensTime} />
              </div>
            </div>
            <div>
              <span className="mb-1 block text-sm font-medium text-slate-600">
                Yopiladi<span className="text-red-500"> *</span>
              </span>
              <div className="flex items-center gap-2">
                <DatePicker value={closesDate} onChange={setClosesDate} min={opensDate || undefined} ariaLabel="Yopilish sanasi" className="w-36" invalid={submitted && !closesDate} />
                <Time24Input value={closesTime} onChange={setClosesTime} />
              </div>
            </div>
            <Input
              label="Vaqt (daqiqa)"
              required
              inputMode="numeric"
              value={timeLimit}
              onChange={(e) => {
                setTimeLimitTouched(true)
                setTimeLimit(e.target.value)
              }}
              placeholder="Bazalardan hisoblanadi"
            />
          </div>
        )}

        {/* Sections */}
        <div>
          <div className="mb-2 flex items-baseline justify-between gap-3">
            <span className="text-sm font-medium text-slate-600">
              Fanlar<span className="text-red-500"> *</span>
            </span>
            {!isManual && totalQuestions > 0 && (
              <span className="text-xs text-slate-400">Jami {totalQuestions} ta savol</span>
            )}
          </div>

          {frozen && saved ? (
            <div className="space-y-2">
              <p className="flex items-center gap-1.5 text-xs text-slate-500">
                <Lock className="h-3.5 w-3.5" />
                Imtihon e'lon qilingan — fanlar ro'yxati va ballari endi o'zgarmaydi.
              </p>
              <ul className="divide-y divide-slate-100 rounded-xl border border-slate-100">
                {[...saved.sections]
                  .sort((a, b) => a.order - b.order)
                  .map((s, i) => (
                    <li key={s.id} className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
                      <span className="text-slate-700">
                        <span className="mr-2 text-xs text-slate-400">{i + 1}</span>
                        {s.subjectName || subjectsById.get(s.subjectId)?.name || s.subjectId}
                      </span>
                      <span className="text-slate-500">
                        {s.questionCount !== null && `${s.questionCount} ta savol · `}
                        maks. {s.maxScore === null ? '—' : formatPoints(s.maxScore)} ball
                      </span>
                    </li>
                  ))}
              </ul>
            </div>
          ) : isManual ? (
            <div className="space-y-2">
              {sections.map((s, index) => {
                const max = parseDecimal(s.maxScore)
                const maxBad = max === null || Number.isNaN(max) || max <= 0 || max > MAX_SECTION_SCORE
                return (
                  <div key={s.key} className="flex flex-wrap items-center gap-2">
                    <span className="w-5 text-center text-xs font-medium text-slate-400">{index + 1}</span>
                    <select
                      value={s.subjectId}
                      onChange={(e) => patchSection(index, { subjectId: e.target.value })}
                      aria-label={`${index + 1}-fan`}
                      className={cn(field, 'min-w-[12rem] flex-1', submitted && !s.subjectId && 'border-red-300')}
                    >
                      <option value="">Fanni tanlang</option>
                      {subjectOptions(index).map((sub) => (
                        <option key={sub.id} value={sub.id}>
                          {sub.name}
                        </option>
                      ))}
                    </select>
                    <label className="flex items-center gap-2 text-sm text-slate-500">
                      Maks. ball
                      <input
                        value={s.maxScore}
                        onChange={(e) => patchSection(index, { maxScore: e.target.value })}
                        inputMode="decimal"
                        placeholder="100"
                        aria-label={`${index + 1}-fan: maksimal ball`}
                        className={cn(field, 'w-24 text-center', submitted && maxBad && 'border-red-300')}
                      />
                    </label>
                    <div className="flex items-center">
                      <button
                        type="button"
                        onClick={() => setSections((prev) => move(prev, index, -1))}
                        disabled={index === 0}
                        title="Yuqoriga"
                        aria-label="Yuqoriga"
                        className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:text-slate-200"
                      >
                        <ArrowUp className="h-4 w-4" />
                      </button>
                      <button
                        type="button"
                        onClick={() => setSections((prev) => move(prev, index, 1))}
                        disabled={index === sections.length - 1}
                        title="Pastga"
                        aria-label="Pastga"
                        className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:text-slate-200"
                      >
                        <ArrowDown className="h-4 w-4" />
                      </button>
                      <button
                        type="button"
                        onClick={() => setSections((prev) => prev.filter((_, i) => i !== index))}
                        title="Olib tashlash"
                        aria-label="Olib tashlash"
                        className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                      >
                        <X className="h-4 w-4" />
                      </button>
                    </div>
                  </div>
                )
              })}
              <button
                type="button"
                onClick={() => setSections((prev) => [...prev, newSectionDraft()])}
                className="inline-flex items-center gap-1.5 rounded-lg px-2 py-1.5 text-sm font-medium text-brand-600 transition-colors hover:bg-brand-50"
              >
                <Plus className="h-4 w-4" /> Fan qo'shish
              </button>
            </div>
          ) : grade === '' ? (
            <p className="rounded-lg bg-slate-50 px-3 py-3 text-sm text-slate-500">
              Avval qaysi sinfga qabul ekanini tanlang — shu sinfning test bazalari chiqadi.
            </p>
          ) : (
            <BankSectionsEditor
              key={grade}
              grade={Number(grade)}
              sections={sections}
              showProblems={submitted}
              onChange={(next, seeded) => {
                setSections(next)
                if (!timeLimitTouched) setTimeLimit(seeded === null ? '' : String(seeded))
              }}
            />
          )}

          {!isManual && totalQuestions > MAX_QUESTIONS_PER_EXAM && (
            <p className="mt-2 flex items-center gap-1.5 text-xs font-medium text-amber-700">
              <AlertTriangle className="h-3.5 w-3.5" />
              Jami {totalQuestions} ta savol — bir imtihonda {MAX_QUESTIONS_PER_EXAM} tadan ortiq savol bo'lsa,
              uni e'lon qilib bo'lmaydi.
            </p>
          )}
        </div>

        {/* Classes → participants (block test only). */}
        {isBlock ? (
          <div>
            <div className="mb-2 flex flex-wrap items-baseline justify-between gap-3">
              <span className="text-sm font-medium text-slate-600">
                {isNew ? 'Sinflar' : "Yana sinf qo'shish"}
                {isNew && <span className="text-red-500"> *</span>}
              </span>
              <span className="text-xs text-slate-400">
                {classIds.size > 0 ? `${classIds.size} ta sinf tanlandi` : "Sinfdagi barcha faol o'quvchilar qo'shiladi"}
              </span>
            </div>
            {!isNew && (
              <p className="mb-2 text-xs text-slate-400">
                Imtihonda allaqachon bor o'quvchilar takrorlanmaydi — faqat yangilari qo'shiladi.
              </p>
            )}
            {classOptions.length === 0 ? (
              <p className="rounded-lg bg-slate-50 px-3 py-3 text-sm text-slate-500">
                Faol sinf yo'q — avval "Sinflar" bo'limida sinf yarating.
              </p>
            ) : (
              <div
                className={cn(
                  'flex max-h-44 flex-wrap gap-1.5 overflow-y-auto rounded-xl border p-2',
                  submitted && isNew && classIds.size === 0 ? 'border-red-300' : 'border-slate-100',
                )}
              >
                {classOptions.map((c) => {
                  const on = classIds.has(c.id)
                  return (
                    <button
                      key={c.id}
                      type="button"
                      onClick={() => toggleClass(c.id)}
                      aria-pressed={on}
                      className={cn(
                        'rounded-lg border px-2.5 py-1 text-sm transition-colors',
                        on
                          ? 'border-brand-200 bg-brand-50 font-medium text-brand-700'
                          : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
                      )}
                    >
                      {c.name}
                    </button>
                  )
                })}
              </div>
            )}
          </div>
        ) : (
          <p className="rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
            Nomzodlar imtihonga "Qabul → Nomzodlar" sahifasidan biriktiriladi va havola o'sha yerda
            chiqariladi.
          </p>
        )}

        {submitted && problem && (
          <p className="flex items-center gap-1.5 text-sm text-red-600">
            <AlertTriangle className="h-4 w-4 shrink-0" /> {problem}
          </p>
        )}
        {error && (
          <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {error}
          </p>
        )}
      </form>
    </Modal>
  )
}
