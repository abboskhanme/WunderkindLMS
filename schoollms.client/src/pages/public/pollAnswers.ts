/**
 * A poll form's answers: what the respondent entered, the checks, and the API shape
 * (docs/modules/surveys-forms.md). Kept apart from `PollFormFields.tsx` because that file
 * exports components only (the `phone.ts` / `SurveyFields.tsx` split).
 *
 * The checks mirror the server's (`PollService.Read`) so the respondent hears about an
 * empty required question at once; the server stays the authority.
 */
import type { PollQuestion } from '@/api/services/polls'
import type { PollAnswerInput } from '@/api/services/publicPoll'

/** What the respondent has entered for one question; which fields are used depends on the kind. */
export interface AnswerValue {
  /** nps, stars, scale */
  score?: number
  /** choice, dropdown */
  choice?: string
  /** multi */
  choices?: string[]
  /** choice: "Boshqa" picked; multi: "Boshqa" ticked */
  otherOn?: boolean
  /** the "Boshqa" text */
  other?: string
  /** short, text */
  text?: string
  /** number, as typed */
  number?: string
  /** date, yyyy-MM-dd */
  date?: string
}

export type Answers = Record<string, AnswerValue>

/** "12,5" → 12.5; null when empty, NaN when not a number. */
export const parseNumber = (raw: string | undefined) => {
  const t = (raw ?? '').trim().replace(',', '.')
  if (!t) return null
  const n = Number(t)
  return Number.isFinite(n) ? n : NaN
}

const otherText = (v: AnswerValue) => (v.otherOn ? (v.other ?? '').trim() : '')

/** How many options a multi answer counts — the ticks plus a filled-in "Boshqa". */
const multiCount = (v: AnswerValue) => (v.choices?.length ?? 0) + (otherText(v) ? 1 : 0)

/** Question id → its number on the form; sections are not numbered. */
export function questionNumbers(questions: PollQuestion[]): Record<string, number> {
  const numbers: Record<string, number> = {}
  let n = 0
  for (const q of questions) if (q.kind !== 'section') numbers[q.id] = ++n
  return numbers
}

export function isAnswered(q: PollQuestion, v: AnswerValue | undefined): boolean {
  if (!v) return false
  switch (q.kind) {
    case 'nps':
    case 'stars':
    case 'scale':
      return v.score !== undefined
    case 'choice':
    case 'dropdown':
      return v.otherOn ? Boolean(otherText(v)) : Boolean(v.choice)
    case 'multi':
      return multiCount(v) > 0
    case 'number':
      return parseNumber(v.number) !== null
    case 'date':
      return Boolean(v.date)
    case 'section':
      return false
    default:
      return Boolean((v.text ?? '').trim())
  }
}

/** Question id → what is wrong; empty when the sheet can be sent. */
export function validateAnswers(questions: PollQuestion[], answers: Answers): Record<string, string> {
  const errors: Record<string, string> = {}
  for (const q of questions) {
    if (q.kind === 'section') continue
    const v = answers[q.id]
    if (!v || !isAnswered(q, v)) {
      if (q.required) errors[q.id] = 'Bu savolga javob bering'
      continue
    }
    if (q.kind === 'multi') {
      const count = multiCount(v)
      if (q.minChoices !== null && count < q.minChoices) errors[q.id] = `Kamida ${q.minChoices} ta variant tanlang`
      else if (q.maxChoices !== null && count > q.maxChoices) errors[q.id] = `Ko'pi bilan ${q.maxChoices} ta variant tanlang`
    }
    if (q.kind === 'number') {
      const n = parseNumber(v.number) ?? NaN
      if (Number.isNaN(n)) errors[q.id] = 'Son kiriting'
      else if (q.minValue !== null && n < q.minValue) errors[q.id] = `Kamida ${q.minValue}`
      else if (q.maxValue !== null && n > q.maxValue) errors[q.id] = `Ko'pi bilan ${q.maxValue}`
    }
  }
  return errors
}

/** The answered questions as the API takes them (run `validateAnswers` first). */
export function toAnswerInputs(questions: PollQuestion[], answers: Answers): PollAnswerInput[] {
  const list: PollAnswerInput[] = []
  for (const q of questions) {
    const v = answers[q.id]
    if (q.kind === 'section' || !v || !isAnswered(q, v)) continue
    const base: PollAnswerInput = {
      questionId: q.id,
      subjectId: null,
      score: null,
      choice: null,
      text: null,
      choices: null,
      number: null,
      date: null,
    }
    switch (q.kind) {
      case 'nps':
      case 'stars':
      case 'scale':
        list.push({ ...base, score: v.score ?? null })
        break
      case 'choice':
      case 'dropdown':
        list.push(v.otherOn ? { ...base, text: otherText(v) } : { ...base, choice: v.choice ?? null })
        break
      case 'multi':
        list.push({ ...base, choices: v.choices ?? [], text: otherText(v) || null })
        break
      case 'number':
        list.push({ ...base, number: parseNumber(v.number) })
        break
      case 'date':
        list.push({ ...base, date: v.date ?? null })
        break
      default:
        list.push({ ...base, text: (v.text ?? '').trim() })
    }
  }
  return list
}
