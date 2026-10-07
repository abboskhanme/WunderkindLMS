import { useState } from 'react'
import { ArrowDown, ArrowUp, Plus, Sparkles, Trash2 } from 'lucide-react'
import {
  createPoll,
  updatePoll,
  POLL_AUDIENCE,
  POLL_KIND,
  POLL_TARGET,
  type Poll,
  type PollAudience,
  type PollQuestionInput,
  type PollQuestionKind,
  type PollTarget,
} from '@/api/services/polls'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { DatePicker } from '@/components/ui/DatePicker'

/** A question being edited — options as one line each. */
interface Draft extends Omit<PollQuestionInput, 'options'> {
  key: string
  options: string
}

let seq = 0
const newKey = () => `q${++seq}`

const blank = (target: PollTarget = 'school'): Draft => ({
  key: newKey(),
  kind: 'nps',
  text: '',
  options: '',
  required: true,
  target,
})

/** Ready-made NPS sets — the classic "would you recommend" plus "what to improve". */
const TEMPLATES: Record<PollAudience, Omit<Draft, 'key'>[]> = {
  parents: [
    { kind: 'nps', text: "Maktabimizni tanishlaringizga tavsiya qilasizmi?", options: '', required: true, target: 'school' },
    { kind: 'nps', text: 'Farzandingizning ustozini boshqalarga tavsiya qilasizmi?', options: '', required: false, target: 'teachers' },
    { kind: 'text', text: 'Nimani yaxshilashimiz kerak?', options: '', required: false, target: 'school' },
  ],
  teachers: [
    { kind: 'nps', text: 'Maktabimizni ish joyi sifatida tanishlaringizga tavsiya qilasizmi?', options: '', required: true, target: 'school' },
    { kind: 'nps', text: 'Rahbaringiz bilan ishlashni boshqalarga tavsiya qilasizmi?', options: '', required: false, target: 'management' },
    { kind: 'text', text: 'Rahbariyat nimani yaxshilashi kerak?', options: '', required: false, target: 'school' },
  ],
}

const targetsFor = (audience: PollAudience): PollTarget[] =>
  audience === 'parents' ? ['school', 'teachers'] : ['school', 'management']

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
  const [closesOn, setClosesOn] = useState(poll?.closesOn ?? '')
  const [questions, setQuestions] = useState<Draft[]>(
    poll
      ? poll.questions.map((q) => ({ ...q, key: newKey(), options: q.options.join('\n') }))
      : TEMPLATES.parents.map((q) => ({ ...q, key: newKey() })),
  )
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const patch = (key: string, change: Partial<Draft>) =>
    setQuestions((qs) => qs.map((q) => (q.key === key ? { ...q, ...change } : q)))
  const move = (index: number, delta: number) =>
    setQuestions((qs) => {
      const next = [...qs]
      const [item] = next.splice(index, 1)
      next.splice(index + delta, 0, item)
      return next
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
        questions: questions.map((q) => ({
          kind: q.kind,
          text: q.text.trim(),
          required: q.required,
          target: q.target,
          options: q.kind === 'choice' ? q.options.split('\n').map((o) => o.trim()).filter(Boolean) : null,
        })),
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
        {locked && (
          <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-700">
            Javoblar kelgan — savollar, auditoriya va anonimlik endi o'zgarmaydi. Sarlavha, izoh va tugash sanasini o'zgartirish
            mumkin.
          </p>
        )}
        <Input label="Sarlavha" required value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} />
        <Textarea
          label="Izoh (ixtiyoriy)"
          rows={2}
          value={description}
          maxLength={2000}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="Nega so'rayapmiz — ota-ona yoki ustoz birinchi bo'lib shuni o'qiydi"
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
          <label className="flex items-center gap-2 self-end pb-2 text-sm text-slate-700">
            <input
              type="checkbox"
              checked={anonymous}
              disabled={locked}
              onChange={(e) => setAnonymous(e.target.checked)}
              className="h-4 w-4 rounded border-slate-300"
            />
            Anonim (javob bergan kishi saqlanmaydi)
          </label>
        </div>

        <div className="flex items-center justify-between pt-1">
          <h3 className="text-sm font-semibold text-slate-700">Savollar ({questions.length})</h3>
          {!locked && (
            <button
              type="button"
              onClick={() => setQuestions(TEMPLATES[audience].map((q) => ({ ...q, key: newKey() })))}
              className="inline-flex items-center gap-1 rounded-lg px-2 py-1 text-xs font-medium text-brand-700 hover:bg-brand-50"
            >
              <Sparkles className="h-3.5 w-3.5" /> NPS shabloni
            </button>
          )}
        </div>

        <div className="space-y-3">
          {questions.map((q, i) => (
            <div key={q.key} className="rounded-xl border border-slate-200 p-3">
              <div className="flex items-start gap-2">
                <span className="mt-2 w-6 shrink-0 text-sm font-semibold text-slate-400">{i + 1}.</span>
                <div className="min-w-0 flex-1 space-y-2">
                  <input
                    value={q.text}
                    disabled={locked}
                    maxLength={500}
                    onChange={(e) => patch(q.key, { text: e.target.value })}
                    placeholder="Savol matni"
                    className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none focus:border-brand-400 disabled:bg-slate-50"
                  />
                  <div className="grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
                    <select
                      value={q.kind}
                      disabled={locked}
                      onChange={(e) => patch(q.key, { kind: e.target.value as PollQuestionKind })}
                      className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400 disabled:bg-slate-50"
                    >
                      {(Object.keys(POLL_KIND) as PollQuestionKind[]).map((k) => (
                        <option key={k} value={k}>
                          {POLL_KIND[k]}
                        </option>
                      ))}
                    </select>
                    <select
                      value={q.target}
                      disabled={locked}
                      onChange={(e) => patch(q.key, { target: e.target.value as PollTarget })}
                      className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400 disabled:bg-slate-50"
                    >
                      {targetsFor(audience).map((t) => (
                        <option key={t} value={t}>
                          {POLL_TARGET[t]}
                        </option>
                      ))}
                    </select>
                    <label className="flex items-center gap-1.5 text-sm text-slate-600">
                      <input
                        type="checkbox"
                        checked={q.required}
                        disabled={locked}
                        onChange={(e) => patch(q.key, { required: e.target.checked })}
                        className="h-4 w-4 rounded border-slate-300"
                      />
                      Majburiy
                    </label>
                  </div>
                  {q.kind === 'choice' && (
                    <textarea
                      value={q.options}
                      disabled={locked}
                      rows={3}
                      onChange={(e) => patch(q.key, { options: e.target.value })}
                      placeholder={'Har bir variant alohida qatorda\nYaxshi\nO\'rtacha\nYomon'}
                      className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400 disabled:bg-slate-50"
                    />
                  )}
                </div>
                {!locked && (
                  <div className="flex shrink-0 flex-col gap-1">
                    <IconButton label="Yuqoriga" disabled={i === 0} onClick={() => move(i, -1)}>
                      <ArrowUp className="h-4 w-4" />
                    </IconButton>
                    <IconButton label="Pastga" disabled={i === questions.length - 1} onClick={() => move(i, 1)}>
                      <ArrowDown className="h-4 w-4" />
                    </IconButton>
                    <IconButton label="O'chirish" danger onClick={() => setQuestions((qs) => qs.filter((x) => x.key !== q.key))}>
                      <Trash2 className="h-4 w-4" />
                    </IconButton>
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>

        {!locked && (
          <Button variant="secondary" onClick={() => setQuestions((qs) => [...qs, blank()])}>
            <Plus className="h-4 w-4" /> Savol qo'shish
          </Button>
        )}

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
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
