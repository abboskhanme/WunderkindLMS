import { useMemo, useState } from 'react'
import { ArrowDown, ArrowUp, Copy, Eye, Heading, Pencil, Plus, Sparkles, Trash2, X } from 'lucide-react'
import {
  createPoll,
  hasOptions,
  updatePoll,
  POLL_AUDIENCE,
  POLL_KIND,
  POLL_OTHER,
  POLL_TARGET,
  type Poll,
  type PollAudience,
  type PollQuestion,
  type PollQuestionInput,
  type PollQuestionKind,
  type PollTarget,
} from '@/api/services/polls'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { DatePicker } from '@/components/ui/DatePicker'
import { cn } from '@/lib/utils'
import { PollQuestionsForm } from '@/pages/public/PollFormFields'
import type { Answers } from '@/pages/public/pollAnswers'

/** Sections count — the server's limit (PollService.MaxQuestions). */
const MAX_BLOCKS = 50

/** A question being edited — numbers as typed, options one per row. */
interface Draft {
  key: string
  kind: PollQuestionKind
  text: string
  description: string
  options: string[]
  allowOther: boolean
  required: boolean
  target: PollTarget
  minValue: string
  maxValue: string
  minLabel: string
  maxLabel: string
  minChoices: string
  maxChoices: string
}

let seq = 0
const newKey = () => `q${++seq}`

const blank = (kind: PollQuestionKind = 'nps', target: PollTarget = 'school'): Draft => ({
  key: newKey(),
  kind,
  text: '',
  description: '',
  options: hasOptions(kind) ? ['', ''] : [],
  allowOther: false,
  required: kind !== 'section',
  target,
  minValue: kind === 'scale' ? '1' : '',
  maxValue: kind === 'scale' ? '5' : '',
  minLabel: '',
  maxLabel: '',
  minChoices: '',
  maxChoices: '',
})

const str = (n: number | null) => (n === null ? '' : String(n))

const fromQuestion = (q: PollQuestion): Draft => ({
  key: newKey(),
  kind: q.kind,
  text: q.text,
  description: q.description ?? '',
  options: q.options,
  allowOther: q.allowOther,
  required: q.required,
  target: q.target,
  minValue: str(q.minValue),
  maxValue: str(q.maxValue),
  minLabel: q.minLabel ?? '',
  maxLabel: q.maxLabel ?? '',
  minChoices: str(q.minChoices),
  maxChoices: str(q.maxChoices),
})

const num = (s: string) => (s.trim() === '' ? null : Number(s.replace(',', '.')))

/** What the server takes — settings the kind does not use go as null. */
const toInput = (d: Draft): PollQuestionInput => {
  const section = d.kind === 'section'
  const ranged = d.kind === 'scale' || d.kind === 'number'
  return {
    kind: d.kind,
    text: d.text.trim(),
    description: d.description.trim() || null,
    required: !section && d.required,
    target: section ? 'school' : d.target,
    options: hasOptions(d.kind) ? d.options.map((o) => o.trim()).filter(Boolean) : null,
    allowOther: (d.kind === 'choice' || d.kind === 'multi') && d.allowOther,
    minValue: ranged ? num(d.minValue) : null,
    maxValue: ranged ? num(d.maxValue) : null,
    minLabel: d.kind === 'scale' ? d.minLabel.trim() || null : null,
    maxLabel: d.kind === 'scale' ? d.maxLabel.trim() || null : null,
    minChoices: d.kind === 'multi' ? num(d.minChoices) : null,
    maxChoices: d.kind === 'multi' ? num(d.maxChoices) : null,
  }
}

/** Ready-made NPS sets — the classic "would you recommend" plus "what to improve". */
const TEMPLATES: Record<PollAudience, Partial<Draft>[]> = {
  parents: [
    { kind: 'nps', text: 'Maktabimizni tanishlaringizga tavsiya qilasizmi?', required: true, target: 'school' },
    { kind: 'nps', text: 'Farzandingizning ustozini boshqalarga tavsiya qilasizmi?', required: false, target: 'teachers' },
    { kind: 'text', text: 'Nimani yaxshilashimiz kerak?', required: false, target: 'school' },
  ],
  teachers: [
    { kind: 'nps', text: 'Maktabimizni ish joyi sifatida tanishlaringizga tavsiya qilasizmi?', required: true, target: 'school' },
    { kind: 'nps', text: 'Rahbaringiz bilan ishlashni boshqalarga tavsiya qilasizmi?', required: false, target: 'management' },
    { kind: 'text', text: 'Rahbariyat nimani yaxshilashi kerak?', required: false, target: 'school' },
  ],
  public: [
    { kind: 'nps', text: 'Maktabimizni tanishlaringizga tavsiya qilasizmi?', required: true, target: 'school' },
    { kind: 'text', text: 'Nimani yaxshilashimiz kerak?', required: false, target: 'school' },
  ],
}

const fromTemplate = (audience: PollAudience) =>
  TEMPLATES[audience].map((t) => ({ ...blank(t.kind), ...t, key: newKey() }))

const targetsFor = (audience: PollAudience): PollTarget[] =>
  audience === 'parents' ? ['school', 'teachers'] : audience === 'teachers' ? ['school', 'management'] : ['school']

interface Props {
  poll: Poll | null
  onClose: () => void
  onSaved: (poll: Poll) => void
}

export function PollEditorModal({ poll, onClose, onSaved }: Props) {
  const locked = (poll?.responses ?? 0) > 0
  const [title, setTitle] = useState(poll?.title ?? '')
  const [description, setDescription] = useState(poll?.description ?? '')
  const [audience, setAudience] = useState<PollAudience>(poll?.audience ?? 'parents')
  const [anonymous, setAnonymous] = useState(poll?.anonymous ?? true)
  const [publicLink, setPublicLink] = useState(poll?.publicLink ?? false)
  const [collectContact, setCollectContact] = useState(poll?.collectContact ?? false)
  const [closesOn, setClosesOn] = useState(poll?.closesOn ?? '')
  const [questions, setQuestions] = useState<Draft[]>(poll ? poll.questions.map(fromQuestion) : fromTemplate('parents'))
  const [preview, setPreview] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const linkOpen = audience === 'public' || publicLink
  const full = questions.length >= MAX_BLOCKS

  const patch = (key: string, change: Partial<Draft>) =>
    setQuestions((qs) => qs.map((q) => (q.key === key ? { ...q, ...change } : q)))
  const move = (index: number, delta: number) =>
    setQuestions((qs) => {
      const next = [...qs]
      const [item] = next.splice(index, 1)
      next.splice(index + delta, 0, item)
      return next
    })
  const copyQuestion = (index: number) =>
    setQuestions((qs) => [...qs.slice(0, index + 1), { ...qs[index], key: newKey(), options: [...qs[index].options] }, ...qs.slice(index + 1)])

  /** A new kind keeps the text; options and ranges get sensible starting values. */
  const changeKind = (d: Draft, kind: PollQuestionKind) =>
    patch(d.key, {
      kind,
      options: hasOptions(kind) && d.options.filter((o) => o.trim()).length === 0 ? ['', ''] : d.options,
      minValue: kind === 'scale' ? (d.minValue === '0' ? '0' : '1') : kind === 'number' ? d.minValue : '',
      maxValue: kind === 'scale' ? (Number(d.maxValue) >= 2 && Number(d.maxValue) <= 10 ? d.maxValue : '5') : kind === 'number' ? d.maxValue : '',
      required: kind === 'section' ? false : d.kind === 'section' ? true : d.required,
      target: kind === 'section' ? 'school' : d.target,
    })

  const changeAudience = (value: PollAudience) => {
    setAudience(value)
    const allowed = targetsFor(value)
    setQuestions((qs) => qs.map((q) => (allowed.includes(q.target) ? q : { ...q, target: 'school' })))
  }

  const save = async () => {
    setSaving(true)
    setError('')
    try {
      const input = {
        title: title.trim(),
        description: description.trim() || null,
        audience,
        anonymous,
        closesOn: closesOn || null,
        publicLink: audience !== 'public' && publicLink,
        // Kept even while the link is off — it applies again when the link is switched back on.
        collectContact: collectContact && !anonymous,
        questions: questions.map(toInput),
      }
      onSaved(poll ? await updatePoll(poll.id, input) : await createPoll(input))
    } catch (e) {
      setError(hrErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title={poll ? "So'rovnomani tahrirlash" : "Yangi so'rovnoma"}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving || !title.trim() || questions.length === 0}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="flex w-fit gap-1 rounded-xl bg-slate-100 p-1">
          {[
            { on: false, label: 'Tahrirlash', icon: Pencil },
            { on: true, label: "Ko'rinishi", icon: Eye },
          ].map((t) => (
            <button
              key={t.label}
              type="button"
              onClick={() => setPreview(t.on)}
              className={cn(
                'flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
                preview === t.on ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              <t.icon className="h-4 w-4" /> {t.label}
            </button>
          ))}
        </div>

        {preview ? (
          <Preview title={title} description={description} questions={questions} />
        ) : (
          <>
            {locked && (
              <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-700">
                Javoblar kelgan — savollar, auditoriya, anonimlik va ism-telefon so'rash endi o'zgarmaydi. Sarlavha, izoh,
                tugash sanasi va havolani o'zgartirish mumkin.
              </p>
            )}
            <Input label="Sarlavha" required value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} />
            <Textarea
              label="Izoh (ixtiyoriy)"
              rows={2}
              value={description}
              maxLength={2000}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Nega so'rayapmiz — javob beruvchi birinchi bo'lib shuni o'qiydi"
            />
            <div className="grid gap-3 sm:grid-cols-3">
              <Select
                label="Kimlar uchun"
                value={audience}
                disabled={locked}
                onChange={(e) => changeAudience(e.target.value as PollAudience)}
              >
                {(Object.keys(POLL_AUDIENCE) as PollAudience[]).map((a) => (
                  <option key={a} value={a}>
                    {POLL_AUDIENCE[a]}
                  </option>
                ))}
              </Select>
              <div>
                <span className="mb-1 block text-sm font-medium text-slate-600">Tugash sanasi (ixtiyoriy)</span>
                <DatePicker value={closesOn} onChange={(v: string) => setClosesOn(v)} clearable />
              </div>
              <Check
                checked={anonymous}
                disabled={locked}
                onChange={(v) => {
                  setAnonymous(v)
                  if (v) setCollectContact(false)
                }}
                className="self-end pb-2"
              >
                Anonim (javob bergan kishi saqlanmaydi)
              </Check>
            </div>

            <div className="space-y-2 rounded-xl bg-slate-50 px-3 py-3">
              {audience === 'public' ? (
                <p className="text-sm text-slate-600">
                  Havolani bilgan har kim javob beradi. So'rovnomani ochgach, ro'yxatdagi «Havolani nusxalash» tugmasi bilan
                  ulashasiz.
                </p>
              ) : (
                <Check checked={publicLink} onChange={setPublicLink}>
                  Havola orqali ham ochiq
                  <span className="block text-xs font-normal text-slate-400">
                    Ilovadan tashqari, havolani bilgan har kim javob bera oladi. Har bir ustoz yoki rahbar haqidagi savollar
                    havolada ko'rinmaydi.
                  </span>
                </Check>
              )}
              {linkOpen && (
                <Check
                  checked={collectContact && !anonymous}
                  disabled={locked}
                  onChange={(v) => {
                    setCollectContact(v)
                    if (v) setAnonymous(false)
                  }}
                >
                  Havola orqali kirganlardan ism va telefon so'ralsin
                  <span className="block text-xs font-normal text-slate-400">
                    Bitta telefon raqamidan bitta javob qabul qilinadi. Anonim so'rovnomada so'ralmaydi.
                  </span>
                </Check>
              )}
            </div>

            <div className="flex items-center justify-between pt-1">
              <h3 className="text-sm font-semibold text-slate-700">
                Savollar ({questions.length}/{MAX_BLOCKS})
              </h3>
              {!locked && (
                <button
                  type="button"
                  onClick={() => setQuestions(fromTemplate(audience))}
                  className="inline-flex items-center gap-1 rounded-lg px-2 py-1 text-xs font-medium text-brand-700 hover:bg-brand-50"
                >
                  <Sparkles className="h-3.5 w-3.5" /> NPS shabloni
                </button>
              )}
            </div>

            <div className="space-y-3">
              {questions.map((q, i) => (
                <QuestionCard
                  key={q.key}
                  index={i}
                  q={q}
                  last={i === questions.length - 1}
                  locked={locked}
                  full={full}
                  targets={targetsFor(audience)}
                  onPatch={(change) => patch(q.key, change)}
                  onKind={(kind) => changeKind(q, kind)}
                  onMove={(delta) => move(i, delta)}
                  onCopy={() => copyQuestion(i)}
                  onRemove={() => setQuestions((qs) => qs.filter((x) => x.key !== q.key))}
                />
              ))}
            </div>

            {!locked && (
              <div className="flex flex-wrap gap-2">
                <Button variant="secondary" disabled={full} onClick={() => setQuestions((qs) => [...qs, blank()])}>
                  <Plus className="h-4 w-4" /> Savol qo'shish
                </Button>
                <Button variant="secondary" disabled={full} onClick={() => setQuestions((qs) => [...qs, blank('section')])}>
                  <Heading className="h-4 w-4" /> Bo'lim qo'shish
                </Button>
              </div>
            )}
          </>
        )}

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}

const fieldClass =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400 disabled:bg-slate-50'

interface CardProps {
  index: number
  q: Draft
  last: boolean
  locked: boolean
  full: boolean
  targets: PollTarget[]
  onPatch: (change: Partial<Draft>) => void
  onKind: (kind: PollQuestionKind) => void
  onMove: (delta: number) => void
  onCopy: () => void
  onRemove: () => void
}

function QuestionCard({ index, q, last, locked, full, targets, onPatch, onKind, onMove, onCopy, onRemove }: CardProps) {
  const section = q.kind === 'section'
  return (
    <div className={cn('rounded-xl border p-3', section ? 'border-brand-200 bg-brand-50/40' : 'border-slate-200')}>
      <div className="flex items-start gap-2">
        <span className="mt-2 w-6 shrink-0 text-sm font-semibold text-slate-400">{index + 1}.</span>
        <div className="min-w-0 flex-1 space-y-2">
          <input
            value={q.text}
            disabled={locked}
            maxLength={500}
            onChange={(e) => onPatch({ text: e.target.value })}
            placeholder={section ? "Bo'lim sarlavhasi" : 'Savol matni'}
            className={cn(
              'w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none focus:border-brand-400 disabled:bg-slate-50',
              section && 'font-semibold',
            )}
          />
          <div className={cn('grid gap-2', section ? 'sm:grid-cols-[1fr_1fr]' : 'sm:grid-cols-[1fr_1fr_auto]')}>
            <select
              value={q.kind}
              disabled={locked}
              onChange={(e) => onKind(e.target.value as PollQuestionKind)}
              className={fieldClass}
            >
              {(Object.keys(POLL_KIND) as PollQuestionKind[]).map((k) => (
                <option key={k} value={k}>
                  {POLL_KIND[k]}
                </option>
              ))}
            </select>
            {!section && targets.length > 1 ? (
              <select
                value={q.target}
                disabled={locked}
                onChange={(e) => onPatch({ target: e.target.value as PollTarget })}
                className={fieldClass}
              >
                {targets.map((t) => (
                  <option key={t} value={t}>
                    {POLL_TARGET[t]}
                  </option>
                ))}
              </select>
            ) : (
              <span />
            )}
            {!section && (
              <Check checked={q.required} disabled={locked} onChange={(v) => onPatch({ required: v })}>
                Majburiy
              </Check>
            )}
          </div>
          <input
            value={q.description}
            disabled={locked}
            maxLength={1000}
            onChange={(e) => onPatch({ description: e.target.value })}
            placeholder={section ? "Bo'lim izohi (ixtiyoriy)" : 'Savol ostidagi izoh (ixtiyoriy)'}
            className={fieldClass}
          />
          <Settings q={q} locked={locked} onPatch={onPatch} />
        </div>
        {!locked && (
          <div className="flex shrink-0 flex-col gap-1">
            <IconButton label="Yuqoriga" disabled={index === 0} onClick={() => onMove(-1)}>
              <ArrowUp className="h-4 w-4" />
            </IconButton>
            <IconButton label="Pastga" disabled={last} onClick={() => onMove(1)}>
              <ArrowDown className="h-4 w-4" />
            </IconButton>
            <IconButton label="Nusxa olish" disabled={full} onClick={onCopy}>
              <Copy className="h-4 w-4" />
            </IconButton>
            <IconButton label="O'chirish" danger onClick={onRemove}>
              <Trash2 className="h-4 w-4" />
            </IconButton>
          </div>
        )}
      </div>
    </div>
  )
}

/** The kind's own settings: options, the "Boshqa" switch, ranges and labels. */
function Settings({ q, locked, onPatch }: { q: Draft; locked: boolean; onPatch: (change: Partial<Draft>) => void }) {
  if (hasOptions(q.kind))
    return (
      <div className="space-y-2">
        <OptionsEditor q={q} locked={locked} onChange={(options) => onPatch({ options })} />
        {(q.kind === 'choice' || q.kind === 'multi') && (
          <Check checked={q.allowOther} disabled={locked} onChange={(v) => onPatch({ allowOther: v })}>
            «{POLL_OTHER}» varianti — javob beruvchi o'zi yozadi
          </Check>
        )}
        {q.kind === 'multi' && (
          <div className="flex flex-wrap items-center gap-2 text-sm text-slate-600">
            <span>Tanlovlar soni:</span>
            <SmallNumber label="eng kam" value={q.minChoices} disabled={locked} onChange={(v) => onPatch({ minChoices: v })} />
            <SmallNumber label="eng ko'p" value={q.maxChoices} disabled={locked} onChange={(v) => onPatch({ maxChoices: v })} />
          </div>
        )}
      </div>
    )
  if (q.kind === 'scale')
    return (
      <div className="grid gap-2 sm:grid-cols-2">
        <div className="flex items-center gap-2 text-sm text-slate-600">
          <select value={q.minValue} disabled={locked} onChange={(e) => onPatch({ minValue: e.target.value })} className={fieldClass}>
            {['0', '1'].map((v) => (
              <option key={v} value={v}>
                {v}
              </option>
            ))}
          </select>
          <span>dan</span>
          <select value={q.maxValue} disabled={locked} onChange={(e) => onPatch({ maxValue: e.target.value })} className={fieldClass}>
            {Array.from({ length: 9 }, (_, i) => String(i + 2)).map((v) => (
              <option key={v} value={v}>
                {v}
              </option>
            ))}
          </select>
          <span>gacha</span>
        </div>
        <div className="grid grid-cols-2 gap-2">
          <input
            value={q.minLabel}
            disabled={locked}
            maxLength={100}
            onChange={(e) => onPatch({ minLabel: e.target.value })}
            placeholder="Chap yozuv (Yomon)"
            className={fieldClass}
          />
          <input
            value={q.maxLabel}
            disabled={locked}
            maxLength={100}
            onChange={(e) => onPatch({ maxLabel: e.target.value })}
            placeholder="O'ng yozuv (A'lo)"
            className={fieldClass}
          />
        </div>
      </div>
    )
  if (q.kind === 'number')
    return (
      <div className="flex flex-wrap items-center gap-2 text-sm text-slate-600">
        <span>Ruxsat etilgan oraliq (ixtiyoriy):</span>
        <SmallNumber label="eng kichik" value={q.minValue} disabled={locked} onChange={(v) => onPatch({ minValue: v })} />
        <SmallNumber label="eng katta" value={q.maxValue} disabled={locked} onChange={(v) => onPatch({ maxValue: v })} />
      </div>
    )
  return null
}

/** One row per option; pasting several lines into a row splits them into options. */
function OptionsEditor({ q, locked, onChange }: { q: Draft; locked: boolean; onChange: (options: string[]) => void }) {
  const max = q.kind === 'dropdown' ? 50 : 20
  const set = (index: number, value: string) => {
    const lines = value.split(/\r?\n/)
    const next = [...q.options]
    next.splice(index, 1, ...lines)
    onChange(next.slice(0, max))
  }
  return (
    <div className="space-y-1.5">
      {q.options.map((o, i) => (
        <div key={i} className="flex items-center gap-2">
          {q.kind === 'dropdown' ? (
            <span className="w-5 shrink-0 text-right text-xs text-slate-400">{i + 1}.</span>
          ) : (
            <span className={cn('h-3.5 w-3.5 shrink-0 border-2 border-slate-300', q.kind === 'multi' ? 'rounded' : 'rounded-full')} />
          )}
          <input
            value={o}
            disabled={locked}
            maxLength={200}
            onChange={(e) => set(i, e.target.value)}
            onPaste={(e) => {
              const text = e.clipboardData.getData('text')
              if (!text.includes('\n')) return
              e.preventDefault()
              set(i, text.trim())
            }}
            placeholder={`${i + 1}-variant`}
            className={fieldClass}
          />
          {!locked && (
            <IconButton label="Variantni o'chirish" disabled={q.options.length <= 1} onClick={() => onChange(q.options.filter((_, j) => j !== i))}>
              <X className="h-4 w-4" />
            </IconButton>
          )}
        </div>
      ))}
      {!locked && q.options.length < max && (
        <button
          type="button"
          onClick={() => onChange([...q.options, ''])}
          className="inline-flex items-center gap-1 rounded-lg px-2 py-1 text-xs font-medium text-brand-700 hover:bg-brand-50"
        >
          <Plus className="h-3.5 w-3.5" /> Variant qo'shish
        </button>
      )}
    </div>
  )
}

/** The form as the respondent will see it on the public page (the Mini App looks the same in its own style). */
function Preview({ title, description, questions }: { title: string; description: string; questions: Draft[] }) {
  const [answers, setAnswers] = useState<Answers>({})
  const asQuestions = useMemo<PollQuestion[]>(
    () =>
      questions.map((d, i) => {
        const input = toInput(d)
        return {
          ...input,
          id: d.key,
          position: i + 1,
          text: input.text || (d.kind === 'section' ? "Bo'lim" : 'Savol matni'),
          options: input.options ?? [],
        }
      }),
    [questions],
  )
  return (
    <div className="space-y-3 rounded-2xl bg-slate-100 p-3 sm:p-4">
      <div className="rounded-2xl border border-t-4 border-slate-200/80 border-t-[#FFD006] bg-white p-5 shadow-sm">
        <h2 className="text-xl font-semibold text-slate-800">{title.trim() || 'Sarlavha'}</h2>
        {description.trim() && <p className="mt-2 whitespace-pre-line text-base text-slate-600">{description}</p>}
      </div>
      <PollQuestionsForm
        questions={asQuestions}
        answers={answers}
        errors={{}}
        onChange={(id, value) => setAnswers((a) => ({ ...a, [id]: value }))}
      />
    </div>
  )
}

function Check({
  checked,
  disabled,
  onChange,
  className,
  children,
}: {
  checked: boolean
  disabled?: boolean
  onChange: (value: boolean) => void
  className?: string
  children: React.ReactNode
}) {
  return (
    <label className={cn('flex items-start gap-2 text-sm text-slate-700', className)}>
      <input
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
        className="mt-0.5 h-4 w-4 shrink-0 rounded border-slate-300"
      />
      <span>{children}</span>
    </label>
  )
}

function SmallNumber({
  label,
  value,
  disabled,
  onChange,
}: {
  label: string
  value: string
  disabled?: boolean
  onChange: (value: string) => void
}) {
  return (
    <input
      type="number"
      value={value}
      disabled={disabled}
      onChange={(e) => onChange(e.target.value)}
      placeholder={label}
      aria-label={label}
      className="w-28 rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400 disabled:bg-slate-50"
    />
  )
}

function IconButton({
  label,
  onClick,
  disabled,
  danger,
  children,
}: {
  label: string
  onClick: () => void
  disabled?: boolean
  danger?: boolean
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      disabled={disabled}
      onClick={onClick}
      className={
        'rounded-lg p-1.5 disabled:opacity-30 ' +
        (danger ? 'text-red-500 hover:bg-red-50' : 'text-slate-400 hover:bg-slate-100 hover:text-slate-600')
      }
    >
      {children}
    </button>
  )
}
