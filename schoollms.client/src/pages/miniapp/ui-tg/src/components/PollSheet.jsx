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
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { CheckCircle2, ChevronRight, ClipboardList, Lock, Star, X } from 'lucide-react'
import { Badge, Card, EmptyState, Hero, Row } from './ui'
import { useAsync } from '../lib/useAsync'
import { haptic, hapticNotify, showBackButton } from '../lib/telegram'

export function PollsCard({ api }) {
  const [open, setOpen] = useState(false)
  const q = useAsync(() => api.list().catch(() => []), [])
  const waiting = (q.data ?? []).filter((p) => !p.answered)
  if (waiting.length === 0 && !open) return null

  return (
    <>
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
                    subtitle={`${p.questions.length} ta savol${p.closesOn ? ` · ${fmt(p.closesOn)} gacha` : ''}`}
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

function PollForm({ poll, api, onDone }) {
  const [answers, setAnswers] = useState({})
  const [sending, setSending] = useState(false)
  const [error, setError] = useState('')

  const subjectsOf = useCallback(
    (q) => (q.target === 'teachers' ? poll.teachers : q.target === 'management' ? poll.managers : null),
    [poll],
  )
  const set = (q, subjectId, value) => setAnswers((a) => ({ ...a, [keyOf(q.id, subjectId)]: value }))

  const missing = useMemo(
    () =>
      poll.questions.filter((q) => {
        if (!q.required) return false
        const subjects = subjectsOf(q)
        if (subjects === null) return !filled(answers[keyOf(q.id, null)])
        return subjects.length > 0 && !subjects.some((s) => filled(answers[keyOf(q.id, s.id)]))
      }),
    [poll, answers, subjectsOf],
  )

  const send = async () => {
    if (sending) return
    if (missing.length) {
      setError(`«${missing[0].text}» savoliga javob bering`)
      hapticNotify('error')
      return
    }
    setSending(true)
    setError('')
    try {
      const list = []
      for (const q of poll.questions) {
        const subjects = subjectsOf(q) ?? [null]
        for (const s of subjects) {
          const v = answers[keyOf(q.id, s?.id ?? null)]
          if (!filled(v)) continue
          list.push({
            questionId: q.id,
            subjectId: s?.id ?? null,
            score: q.kind === 'nps' || q.kind === 'stars' ? v : null,
            choice: q.kind === 'choice' ? v : null,
            text: q.kind === 'text' ? v : null,
          })
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
      {poll.questions.map((q, i) => {
        const subjects = subjectsOf(q)
        return (
          <Card key={q.id}>
            <div className="space-y-3 px-4 py-4">
              <p className="text-[15px] font-semibold leading-snug text-slate-800">
                {i + 1}. {q.text}
                {q.required && <span className="text-red-500"> *</span>}
              </p>
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

const filled = (v) => v !== undefined && v !== null && String(v).trim() !== ''

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
        <div className="mt-1 flex justify-between text-[11px] text-slate-400">
          <span>Umuman yo'q</span>
          <span>Albatta</span>
        </div>
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
  if (q.kind === 'choice')
    return (
      <div className="flex flex-wrap gap-2">
        {q.options.map((o) => (
          <button
            key={o}
            type="button"
            onClick={() => {
              haptic('light')
              onChange(value === o ? null : o)
            }}
            className={
              'rounded-xl px-3.5 py-2 text-[14px] font-medium ' +
              (value === o ? 'bg-brand-ink text-brand' : 'bg-slate-100 text-slate-700')
            }
          >
            {o}
          </button>
        ))}
      </div>
    )
  return (
    <textarea
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value)}
      rows={3}
      maxLength={2000}
      placeholder="Javobingiz"
      className="w-full rounded-xl border border-slate-200 bg-white px-3 py-3 text-[15px]"
    />
  )
}
