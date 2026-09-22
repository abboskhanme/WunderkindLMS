/**
 * Add / edit one question (§3.1 screen 4; §6.2 `POST /questions`,
 * `PUT /questions/{id}`; §5.2–§5.3).
 *
 * Rules the server enforces (§5.3) and this form mirrors, so the user is told
 * before a round trip: the text is not blank; 2–6 options, lettered A–F by
 * position; no blank option; no two identical options; exactly one correct.
 * The server remains the judge — its sentence is shown if it disagrees.
 *
 * On edit, an option keeps its `id` so the server edits it in place; a new
 * option goes without one; a removed option is simply not sent.
 *
 * Mounted conditionally by the parent — every opening starts clean.
 */
import { useState } from 'react'
import type { FormEvent } from 'react'
import { Loader2, Plus, X } from 'lucide-react'
import {
  MAX_OPTIONS,
  MIN_OPTIONS,
  admissionBankError,
  createQuestion,
  updateQuestion,
  type Question,
  type QuestionOptionInput,
} from '@/api/services/admissionBanks'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { PhotoUpload } from '@/components/ui/PhotoUpload'
import { cn, uid } from '@/lib/utils'
import { optionLetter } from './BankHelpers'

interface Props {
  bankId: string
  /** `null` — a new question. */
  editing: Question | null
  /** Shown in the title of a new question ("12-savol"). */
  nextNumber: number
  onClose: () => void
  onSaved: (saved: Question, created: boolean) => void
}

interface OptionDraft {
  /** React key — the option id when it has one. */
  key: string
  id?: string
  text: string
}

/** A new question starts with four blank options — the usual school test. */
const NEW_OPTION_COUNT = 4

function initialOptions(editing: Question | null): OptionDraft[] {
  if (!editing) {
    return Array.from({ length: NEW_OPTION_COUNT }, () => ({ key: uid(), text: '' }))
  }
  return [...editing.options]
    .sort((a, b) => a.order - b.order)
    .map((o) => ({ key: o.id, id: o.id, text: o.text }))
}

/** The first problem, in the words of §6.2's import chips where they overlap. */
function problemOf(text: string, options: OptionDraft[], correctKey: string | null): string | null {
  if (!text.trim()) return 'Savol matnini kiriting'
  if (options.length < MIN_OPTIONS) return `Kamida ${MIN_OPTIONS} ta variant bo'lsin`
  if (options.length > MAX_OPTIONS) return `Ko'pi bilan ${MAX_OPTIONS} ta variant`
  const blank = options.findIndex((o) => !o.text.trim())
  if (blank >= 0) {
    return `${optionLetter(blank)} variant bo'sh — to'ldiring yoki olib tashlang`
  }
  const seen = new Map<string, number>()
  for (const [i, o] of options.entries()) {
    const value = o.text.trim()
    const earlier = seen.get(value)
    if (earlier !== undefined) {
      return `Bir xil variant: ${optionLetter(earlier)} va ${optionLetter(i)}`
    }
    seen.set(value, i)
  }
  if (!correctKey || !options.some((o) => o.key === correctKey)) {
    return "To'g'ri javobni belgilang"
  }
  return null
}

export function QuestionFormModal({ bankId, editing, nextNumber, onClose, onSaved }: Props) {
  const [text, setText] = useState(editing?.text ?? '')
  const [imageUrl, setImageUrl] = useState<string | null>(editing?.imageUrl ?? null)
  const [options, setOptions] = useState<OptionDraft[]>(() => initialOptions(editing))
  const [correctKey, setCorrectKey] = useState<string | null>(
    () => editing?.options.find((o) => o.isCorrect)?.id ?? null,
  )
  /** Outlines blank fields only after the first attempt — not while typing. */
  const [attempted, setAttempted] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const problem = problemOf(text, options, correctKey)

  const setOptionText = (key: string, value: string) => {
    setOptions((prev) => prev.map((o) => (o.key === key ? { ...o, text: value } : o)))
    setError(null)
  }

  const addOption = () => {
    if (options.length >= MAX_OPTIONS) return
    setOptions((prev) => [...prev, { key: uid(), text: '' }])
  }

  const removeOption = (key: string) => {
    if (options.length <= MIN_OPTIONS) return
    setOptions((prev) => prev.filter((o) => o.key !== key))
    if (correctKey === key) setCorrectKey(null)
    setError(null)
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setAttempted(true)
    if (saving) return
    if (problem) {
      setError(problem)
      return
    }
    const payloadOptions: QuestionOptionInput[] = options.map((o) => ({
      ...(o.id ? { id: o.id } : {}),
      text: o.text.trim(),
      isCorrect: o.key === correctKey,
    }))
    setSaving(true)
    setError(null)
    try {
      const saved = editing
        ? await updateQuestion(editing.id, {
            text: text.trim(),
            imageUrl,
            options: payloadOptions,
          })
        : await createQuestion({ bankId, text: text.trim(), imageUrl, options: payloadOptions })
      onSaved(saved, !editing)
    } catch (err) {
      setError(admissionBankError(err, 'saveQuestion').message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={editing ? 'Savolni tahrirlash' : `Yangi savol · ${nextNumber}-savol`}
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button type="submit" form="question-form" disabled={saving}>
            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
            Saqlash
          </Button>
        </>
      }
    >
      <form id="question-form" onSubmit={(e) => void submit(e)} className="space-y-5">
        <Textarea
          label="Savol matni"
          required
          rows={3}
          placeholder="Savolni yozing..."
          value={text}
          onChange={(e) => {
            setText(e.target.value)
            setError(null)
          }}
          className={cn(attempted && !text.trim() && 'border-red-300')}
        />

        <PhotoUpload label="Rasm (ixtiyoriy)" value={imageUrl} onChange={setImageUrl} />

        <fieldset className="space-y-2">
          <legend className="mb-1 flex w-full flex-wrap items-baseline justify-between gap-2">
            <span className="text-sm font-medium text-slate-600">
              Variantlar<span className="text-red-500"> *</span>
            </span>
            <span className="text-xs text-slate-400">
              To'g'ri javobni chapdagi doira bilan belgilang · {MIN_OPTIONS}–{MAX_OPTIONS} ta
            </span>
          </legend>

          {options.map((option, i) => {
            const correct = option.key === correctKey
            const blank = attempted && !option.text.trim()
            return (
              <div
                key={option.key}
                className={cn(
                  'flex items-center gap-2 rounded-xl border px-2 py-1.5 transition-colors',
                  correct ? 'border-emerald-200 bg-emerald-50/70' : 'border-slate-200 bg-white',
                  blank && 'border-red-300',
                )}
              >
                <label
                  className="flex shrink-0 cursor-pointer items-center gap-2 pl-1"
                  title="To'g'ri javob"
                >
                  <input
                    type="radio"
                    name="correct-option"
                    checked={correct}
                    onChange={() => {
                      setCorrectKey(option.key)
                      setError(null)
                    }}
                    className="h-4 w-4 accent-emerald-600"
                  />
                  <span
                    className={cn(
                      'flex h-6 w-6 items-center justify-center rounded-full text-xs font-semibold',
                      correct ? 'bg-emerald-500 text-white' : 'bg-slate-100 text-slate-500',
                    )}
                  >
                    {optionLetter(i)}
                  </span>
                </label>
                <input
                  value={option.text}
                  onChange={(e) => setOptionText(option.key, e.target.value)}
                  placeholder={`${optionLetter(i)} variant`}
                  aria-label={`${optionLetter(i)} variant`}
                  className="min-w-0 flex-1 bg-transparent px-1 py-1 text-sm text-slate-800 outline-none placeholder:text-slate-300"
                />
                <button
                  type="button"
                  title="Variantni olib tashlash"
                  aria-label="Variantni olib tashlash"
                  disabled={options.length <= MIN_OPTIONS}
                  onClick={() => removeOption(option.key)}
                  className="shrink-0 rounded-lg p-1 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600 disabled:invisible"
                >
                  <X className="h-4 w-4" />
                </button>
              </div>
            )
          })}

          {options.length < MAX_OPTIONS && (
            <button
              type="button"
              onClick={addOption}
              className="inline-flex items-center gap-1.5 rounded-lg px-2 py-1.5 text-sm font-medium text-brand-600 transition-colors hover:bg-brand-50"
            >
              <Plus className="h-4 w-4" /> Variant qo'shish
            </button>
          )}
        </fieldset>

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      </form>
    </Modal>
  )
}
