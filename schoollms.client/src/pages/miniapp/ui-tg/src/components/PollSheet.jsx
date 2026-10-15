/**
 * SO'ROVNOMALAR — the respondent's side, shared by the parent and the teacher panels (client,
 * 2026-10-07: "ota-onalar va o'qituvchilar shaxsiy kabinetiga so'rovnoma olishni kiritib ketish
 * va NPS ko'rsatkichini o'lchay olish").
 *
 *   PollsCard — one row on the home screen while an open poll waits for an answer; hidden otherwise.
 *   PollSheet — full screen like the feedback sheet: the open polls, then one poll's questions.
 *
 * A question about "each teacher" (parents) or "each manager" (teachers) repeats for every person
 * the server listed; nobody has to rate everyone. The panel gives its own API (`list`, `submit`).
 *
 * Google Forms kinds (2026-10-15, docs/modules/surveys-forms.md): scale, multi (with min/max
 * ticks), dropdown, short, number, date, a "Boshqa: ___" option on choice/multi, and sections —
 * shown as headings in the one scrolling form. The admin SPA's public page renders the same
 * kinds in `pages/public/PollFormFields.tsx`; this build cannot import it.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { CheckCircle2, ChevronRight, ClipboardList, Lock, Star, X } from 'lucide-react'
import { Badge, Card, EmptyState, Hero, Row } from './ui'
import { useAsync } from '../lib/useAsync'
import { haptic, hapticNotify, showBackButton } from '../lib/telegram'

/** `alwaysShow` — the teacher panel keeps the row even with nothing open (the same windows for everyone). */
export function PollsCard({ api, alwaysShow = false }) {
  const [open, setOpen] = useState(false)
  const q = useAsync(() => api.list().catch(() => []), [])
  const waiting = (q.data ?? []).filter((p) => !p.answered)
  if (waiting.length === 0 && !open && !alwaysShow) return null

  return (
    <>
      {waiting.length === 0 && alwaysShow && (
        <Card>
          <Row
            lead={
              <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
                <ClipboardList className="h-5 w-5" />
              </div>
            }
            title="So'rovnomalar"
            subtitle={q.loading && !q.data ? 'Yuklanmoqda…' : "Hozir ochiq so'rovnoma yo'q"}
            right={<ChevronRight className="h-5 w-5 text-slate-400" />}
            onClick={() => {
              haptic('light')
              setOpen(true)
            }}
          />
        </Card>
      )}
      {waiting.length > 0 && (
        <Card>
          <Row
            lead={
              <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand text-brand-ink">
                <ClipboardList className="h-5 w-5" />
              </div>
            }
            title={waiting.length === 1 ? waiting[0].title : `${waiting.length} ta so'rovnoma`}
            subtitle="Fikringiz biz uchun muhim — 1–2 daqiqa"
            right={<Badge tone="brand">Javob berish</Badge>}
            onClick={() => {
              haptic('light')
              setOpen(true)
            }}
          />
        </Card>
      )}
      {open && (
        <PollSheet
          api={api}
          onClose={() => {
            setOpen(false)
            q.reload()
          }}
        />
      )}
    </>
  )
}

export function PollSheet({ api, onClose }) {
  const list = useAsync(() => api.list(), [])
  const [current, setCurrent] = useState(null)
  const [thanks, setThanks] = useState(false)

  const back = useCallback(() => (current ? setCurrent(null) : onClose()), [current, onClose])
  useEffect(() => showBackButton(back), [back])

  const polls = list.data ?? []

  return (
    <div role="dialog" aria-label="So'rovnomalar" className="fixed inset-0 z-40 overflow-y-auto overscroll-contain bg-ground">
      <div className="pb-[calc(24px+var(--tg-safe-bottom))]">
        <Hero
          title={current ? current.title : "So'rovnomalar"}
          subtitle={current ? (current.anonymous ? 'Anonim — ismingiz ko\'rinmaydi' : null) : 'Fikringizni bildiring'}
          right={
            <button
              type="button"
              onClick={onClose}
              aria-label="Yopish"
              className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-brand-ink/10 text-brand-ink"
            >
              <X className="h-5 w-5" />
            </button>
          }
        />

        {current ? (
          <PollForm
            poll={current}
            api={api}
            onDone={() => {
              setCurrent(null)
              setThanks(true)
              list.reload()
            }}
          />
        ) : (
          <>
            {thanks && (
              <Card>
                <div className="flex items-center gap-3 px-4 py-4 text-emerald-700">
                  <CheckCircle2 className="h-6 w-6 shrink-0" />
                  <p className="text-[15px] font-semibold">Rahmat! Javobingiz qabul qilindi.</p>
                </div>
              </Card>
            )}
            {list.loading && !list.data ? (
              <p className="px-4 py-6 text-center text-[14px] text-slate-500">Yuklanmoqda…</p>
            ) : list.error ? (
              <p className="px-4 py-6 text-center text-[14px] text-red-600">{String(list.error)}</p>
            ) : polls.length === 0 ? (
              <Card>
                <EmptyState icon={<ClipboardList className="h-9 w-9" />} title="Ochiq so'rovnoma yo'q" note="Yangi so'rovnoma chiqsa, shu yerda ko'rinadi." />
              </Card>
            ) : (
              <Card>
                {polls.map((p) => (
                  <Row
                    key={p.id}
                    lead={
                      <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
                        {p.answered ? <CheckCircle2 className="h-5 w-5" /> : <ClipboardList className="h-5 w-5" />}
                      </div>
                    }
                    title={p.title}
                    subtitle={`${p.questions.filter((x) => x.kind !== 'section').length} ta savol${p.closesOn ? ` · ${fmt(p.closesOn)} gacha` : ''}`}
                    right={
                      p.answered ? (
                        <Badge tone="success">Javob berilgan</Badge>
                      ) : (
                        <ChevronRight className="h-5 w-5 text-slate-400" />
                      )
                    }
                    onClick={
                      p.answered
                        ? undefined
                        : () => {
                            haptic('light')
                            setThanks(false)
                            setCurrent(p)
                          }
                    }
                  />
                ))}
              </Card>
            )}
          </>
        )}
      </div>
    </div>
  )
}

const fmt = (iso) => {
  const [y, m, d] = String(iso).split('-')
  return d && m && y ? `${d}.${m}.${y}` : iso
}

/** Answer key: question id + rated person ('' for the school). */
const keyOf = (questionId, subjectId) => `${questionId}|${subjectId ?? ''}`

const OTHER = 'Boshqa'

/** "12,5" → 12.5; null when empty, NaN when not a number. */
const parseNumber = (raw) => {
  const t = String(raw ?? '').trim().replace(',', '.')
  if (!t) return null
  const n = Number(t)
  return Number.isFinite(n) ? n : NaN
}

/** The "Boshqa" text of a choice / multi answer — only while "Boshqa" is picked. */
const otherText = (v) => (v?.otherOn ? String(v.other ?? '').trim() : '')

/** Ticks plus a filled-in "Boshqa". */
const multiCount = (v) => (v?.choices?.length ?? 0) + (otherText(v) ? 1 : 0)

/**
 * Whether a value counts as an answer. Scores are numbers; choice / dropdown / multi are
 * `{ choice, choices, otherOn, other }`; text, short, number and date are the typed string.
 */
function answered(q, v) {
  if (v === undefined || v === null) return false
  switch (q.kind) {
    case 'nps':
    case 'stars':
    case 'scale':
      return typeof v === 'number'
    case 'choice':
    case 'dropdown':
      return v.otherOn ? Boolean(otherText(v)) : Boolean(v.choice)
    case 'multi':
      return multiCount(v) > 0
    case 'number':
      return parseNumber(v) !== null
    default:
      return String(v).trim() !== ''
  }
}

/** What is wrong with one given answer (ranges, tick counts) — the server checks the same. */
function rangeProblem(q, v) {
  if (q.kind === 'multi') {
    const count = multiCount(v)
    if (q.minChoices != null && count < q.minChoices) return `kamida ${q.minChoices} ta variant tanlang`
    if (q.maxChoices != null && count > q.maxChoices) return `ko'pi bilan ${q.maxChoices} ta variant tanlang`
  }
  if (q.kind === 'number') {
    const n = parseNumber(v)
    if (Number.isNaN(n)) return 'son kiriting'
    if (q.minValue != null && n < q.minValue) return `kamida ${q.minValue}`
    if (q.maxValue != null && n > q.maxValue) return `ko'pi bilan ${q.maxValue}`
  }
  return null
}

/** The API fields of one answer. */
function payloadOf(q, v) {
  switch (q.kind) {
    case 'nps':
    case 'stars':
    case 'scale':
      return { score: v }
    case 'choice':
    case 'dropdown':
      return v.otherOn ? { text: otherText(v) } : { choice: v.choice }
    case 'multi':
      return { choices: v.choices ?? [], text: otherText(v) || null }
    case 'number':
      return { number: parseNumber(v) }
    case 'date':
      return { date: v }
    default:
      return { text: String(v).trim() }
  }
}

function PollForm({ poll, api, onDone }) {
  const [answers, setAnswers] = useState({})
  const [sending, setSending] = useState(false)
  const [error, setError] = useState('')

  const subjectsOf = useCallback(
    (q) => (q.target === 'teachers' ? poll.teachers : q.target === 'management' ? poll.managers : null),
    [poll],
  )
  const set = (q, subjectId, value) => setAnswers((a) => ({ ...a, [keyOf(q.id, subjectId)]: value }))

  /** Question numbers — a section is a heading, not a question. */
  const numbers = useMemo(() => {
    const map = {}
    let n = 0
    for (const q of poll.questions) if (q.kind !== 'section') map[q.id] = ++n
    return map
  }, [poll])

  /** The first thing to fix, as a sentence; null when the sheet can go. */
  const problem = useMemo(() => {
    for (const q of poll.questions) {
      if (q.kind === 'section') continue
      const subjects = subjectsOf(q)
      const values = (subjects ?? [null]).map((s) => answers[keyOf(q.id, s?.id ?? null)]).filter((v) => answered(q, v))
      if (q.required && values.length === 0 && (subjects === null || subjects.length > 0)) return `«${q.text}» savoliga javob bering`
      for (const v of values) {
        const p = rangeProblem(q, v)
        if (p) return `«${q.text}» — ${p}`
      }
    }
    return null
  }, [poll, answers, subjectsOf])

  const send = async () => {
    if (sending) return
    if (problem) {
      setError(problem)
      hapticNotify('error')
      return
    }
    setSending(true)
    setError('')
    try {
      const list = []
      for (const q of poll.questions) {
        if (q.kind === 'section') continue
        const subjects = subjectsOf(q) ?? [null]
        for (const s of subjects) {
          const v = answers[keyOf(q.id, s?.id ?? null)]
          if (!answered(q, v)) continue
          list.push({ questionId: q.id, subjectId: s?.id ?? null, score: null, choice: null, text: null, ...payloadOf(q, v) })
        }
      }
      await api.submit(poll.id, list)
      hapticNotify('success')
      onDone()
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || "Yuborib bo'lmadi")
    } finally {
      setSending(false)
    }
  }

  return (
    <>
      {poll.description && (
        <p className="whitespace-pre-line px-5 pb-3 text-[14px] leading-snug text-slate-600">{poll.description}</p>
      )}
      {poll.questions.map((q) => {
        if (q.kind === 'section')
          return (
            <div key={q.id} className="px-5 pb-1 pt-5">
              <h2 className="text-[17px] font-bold text-slate-800">{q.text}</h2>
              {q.description && <p className="mt-0.5 whitespace-pre-line text-[13px] leading-snug text-slate-500">{q.description}</p>}
            </div>
          )
        const subjects = subjectsOf(q)
        return (
          <Card key={q.id}>
            <div className="space-y-3 px-4 py-4">
              <div>
                <p className="text-[15px] font-semibold leading-snug text-slate-800">
                  {numbers[q.id]}. {q.text}
                  {q.required && <span className="text-red-500"> *</span>}
                </p>
                {q.description && <p className="mt-0.5 whitespace-pre-line text-[13px] leading-snug text-slate-500">{q.description}</p>}
              </div>
              {subjects === null ? (
                <Answer q={q} value={answers[keyOf(q.id, null)]} onChange={(v) => set(q, null, v)} />
              ) : subjects.length === 0 ? (
                <p className="text-[13px] text-slate-400">Baholanadigan odam topilmadi</p>
              ) : (
                subjects.map((s) => (
                  <div key={s.id} className="space-y-2 border-t border-slate-100 pt-3 first:border-0 first:pt-0">
                    <p className="text-[14px] font-medium text-slate-700">
                      {s.name}
                      {s.note && <span className="font-normal text-slate-400"> · {s.note}</span>}
                    </p>
                    <Answer q={q} value={answers[keyOf(q.id, s.id)]} onChange={(v) => set(q, s.id, v)} />
                  </div>
                ))
              )}
            </div>
          </Card>
        )
      })}

      <div className="space-y-3 px-4 pt-1">
        {poll.anonymous && (
          <p className="flex items-center justify-center gap-1.5 text-[12px] text-slate-400">
            <Lock className="h-3.5 w-3.5" /> Javoblar anonim saqlanadi
          </p>
        )}
        {error && <p className="rounded-xl bg-red-50 px-3 py-2 text-[14px] text-red-600">{error}</p>}
        <button
          type="button"
          onClick={send}
          disabled={sending}
          className="w-full rounded-2xl bg-brand-ink py-3.5 text-[16px] font-semibold text-brand disabled:opacity-40"
        >
          {sending ? 'Yuborilmoqda…' : 'Yuborish'}
        </button>
      </div>
    </>
  )
}

const fieldClass = 'w-full rounded-xl border border-slate-200 bg-white px-3 py-3 text-[15px]'

/** A pill — the same look for single and multiple choice. */
function Chip({ selected, onClick, children }) {
  return (
    <button
      type="button"
      onClick={() => {
        haptic('light')
        onClick()
      }}
      className={
        'rounded-xl px-3.5 py-2 text-[14px] font-medium ' + (selected ? 'bg-brand-ink text-brand' : 'bg-slate-100 text-slate-700')
      }
    >
      {children}
    </button>
  )
}

function OtherInput({ value, onChange }) {
  return (
    <input
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value)}
      maxLength={500}
      placeholder="O'z javobingiz"
      className={fieldClass}
    />
  )
}

function ScaleEnds({ low, high }) {
  return (
    <div className="mt-1 flex justify-between gap-3 text-[11px] text-slate-400">
      <span>{low}</span>
      <span className="text-right">{high}</span>
    </div>
  )
}

function Answer({ q, value, onChange }) {
  if (q.kind === 'nps')
    return (
      <div>
        <div className="grid grid-cols-11 gap-1">
          {Array.from({ length: 11 }, (_, n) => (
            <button
              key={n}
              type="button"
              onClick={() => {
                haptic('light')
                onChange(n)
              }}
              className={
                'h-9 rounded-lg text-[14px] font-semibold ' +
                (value === n
                  ? n >= 9
                    ? 'bg-emerald-500 text-white'
                    : n >= 7
                      ? 'bg-amber-400 text-white'
                      : 'bg-red-500 text-white'
                  : 'bg-slate-100 text-slate-600')
              }
            >
              {n}
            </button>
          ))}
        </div>
        <ScaleEnds low="Umuman yo'q" high="Albatta" />
      </div>
    )
  if (q.kind === 'stars')
    return (
      <div className="flex gap-2">
        {[1, 2, 3, 4, 5].map((n) => (
          <button
            key={n}
            type="button"
            aria-label={`${n} yulduz`}
            onClick={() => {
              haptic('light')
              onChange(n)
            }}
          >
            <Star className={'h-9 w-9 ' + (value >= n ? 'fill-amber-400 text-amber-400' : 'text-slate-300')} />
          </button>
        ))}
      </div>
    )
  if (q.kind === 'scale') {
    const low = q.minValue ?? 1
    const high = q.maxValue ?? 5
    return (
      <div>
        <div className="flex gap-1">
          {Array.from({ length: high - low + 1 }, (_, i) => low + i).map((n) => (
            <button
              key={n}
              type="button"
              onClick={() => {
                haptic('light')
                onChange(n)
              }}
              className={
                'h-9 flex-1 rounded-lg text-[14px] font-semibold ' + (value === n ? 'bg-brand-ink text-brand' : 'bg-slate-100 text-slate-600')
              }
            >
              {n}
            </button>
          ))}
        </div>
        {(q.minLabel || q.maxLabel) && <ScaleEnds low={q.minLabel ?? ''} high={q.maxLabel ?? ''} />}
      </div>
    )
  }
  if (q.kind === 'choice') {
    const v = value ?? {}
    return (
      <div className="space-y-2">
        <div className="flex flex-wrap gap-2">
          {q.options.map((o) => (
            <Chip key={o} selected={!v.otherOn && v.choice === o} onClick={() => onChange(!v.otherOn && v.choice === o ? null : { choice: o })}>
              {o}
            </Chip>
          ))}
          {q.allowOther && (
            <Chip selected={Boolean(v.otherOn)} onClick={() => onChange(v.otherOn ? null : { otherOn: true, other: v.other ?? '' })}>
              {OTHER}
            </Chip>
          )}
        </div>
        {v.otherOn && <OtherInput value={v.other} onChange={(other) => onChange({ otherOn: true, other })} />}
      </div>
    )
  }
  if (q.kind === 'multi') {
    const v = value ?? {}
    const picked = v.choices ?? []
    const hint =
      q.minChoices != null && q.maxChoices != null
        ? q.minChoices === q.maxChoices
          ? `${q.minChoices} ta tanlang`
          : `${q.minChoices}–${q.maxChoices} ta tanlang`
        : q.minChoices != null
          ? `Kamida ${q.minChoices} ta tanlang`
          : q.maxChoices != null
            ? `Ko'pi bilan ${q.maxChoices} ta tanlang`
            : 'Bir nechtasini tanlash mumkin'
    return (
      <div className="space-y-2">
        <p className="text-[12px] text-slate-400">{hint}</p>
        <div className="flex flex-wrap gap-2">
          {q.options.map((o) => (
            <Chip
              key={o}
              selected={picked.includes(o)}
              onClick={() => onChange({ ...v, choices: picked.includes(o) ? picked.filter((x) => x !== o) : [...picked, o] })}
            >
              {o}
            </Chip>
          ))}
          {q.allowOther && (
            <Chip selected={Boolean(v.otherOn)} onClick={() => onChange({ ...v, otherOn: !v.otherOn })}>
              {OTHER}
            </Chip>
          )}
        </div>
        {v.otherOn && <OtherInput value={v.other} onChange={(other) => onChange({ ...v, other })} />}
      </div>
    )
  }
  if (q.kind === 'dropdown')
    return (
      <select
        value={value?.choice ?? ''}
        onChange={(e) => onChange(e.target.value ? { choice: e.target.value } : null)}
        className={fieldClass}
      >
        <option value="">Tanlang</option>
        {q.options.map((o) => (
          <option key={o} value={o}>
            {o}
          </option>
        ))}
      </select>
    )
  if (q.kind === 'number')
    return (
      <input
        inputMode="decimal"
        value={value ?? ''}
        onChange={(e) => onChange(e.target.value)}
        placeholder={q.minValue != null && q.maxValue != null ? `${q.minValue} – ${q.maxValue}` : 'Son'}
        className={fieldClass}
      />
    )
  if (q.kind === 'date')
    return <input type="date" value={value ?? ''} onChange={(e) => onChange(e.target.value)} className={fieldClass} />
  if (q.kind === 'short')
    return (
      <input
        value={value ?? ''}
        onChange={(e) => onChange(e.target.value)}
        maxLength={500}
        placeholder="Javobingiz"
        className={fieldClass}
      />
    )
  return (
    <textarea
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value)}
      rows={3}
      maxLength={2000}
      placeholder="Javobingiz"
      className={fieldClass}
    />
  )
}
