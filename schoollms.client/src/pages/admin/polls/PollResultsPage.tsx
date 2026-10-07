import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ArrowLeft, Lock, MessageSquareText, Star } from 'lucide-react'
import {
  getPollResults,
  npsTone,
  POLL_AUDIENCE,
  POLL_TARGET,
  type PollQuestionResult,
  type PollResults,
} from '@/api/services/polls'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { fmtDate } from '@/components/lesson-analysis/format'

export function PollResultsPage() {
  const { id = '' } = useParams()
  const [className, setClassName] = useState('')
  const [data, setData] = useState<PollResults | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    getPollResults(id, className || undefined)
      .then(setData)
      .catch((e) => setError(hrErrorMessage(e, "Natijalarni yuklab bo'lmadi")))
  }, [id, className])

  if (error) return <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>
  if (!data) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <Link to="/admin/boshqaruv/polls" className="mb-1 inline-flex items-center gap-1 text-sm text-slate-400 hover:text-slate-600">
            <ArrowLeft className="h-4 w-4" /> So'rovnomalar
          </Link>
          <h1 className="text-xl font-semibold text-slate-800">{data.title}</h1>
          <p className="flex items-center gap-2 text-sm text-slate-400">
            {POLL_AUDIENCE[data.audience]} · {data.responses} ta javob
            {data.anonymous && (
              <span className="inline-flex items-center gap-1">
                · <Lock className="h-3 w-3" /> anonim
              </span>
            )}
          </p>
        </div>
        {data.classes.length > 0 && (
          <select
            value={className}
            onChange={(e) => setClassName(e.target.value)}
            className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
          >
            <option value="">Barcha sinflar</option>
            {data.classes.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
        )}
      </div>

      {data.responses === 0 ? (
        <Card>
          <p className="py-10 text-center text-slate-400">Hali javob yo'q</p>
        </Card>
      ) : (
        data.questions.map((q, i) => <QuestionResult key={q.questionId} index={i + 1} q={q} />)
      )}
    </div>
  )
}

function QuestionResult({ index, q }: { index: number; q: PollQuestionResult }) {
  const max = Math.max(1, ...q.distribution.map((d) => d.count))
  return (
    <Card className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-xs font-medium uppercase tracking-wide text-slate-400">
            {index}-savol · {POLL_TARGET[q.target]} · {q.answers} ta javob
          </p>
          <h2 className="mt-0.5 text-base font-semibold text-slate-800">{q.text}</h2>
        </div>
        {q.kind === 'nps' && (
          <div className="text-right">
            <div className={'text-3xl font-bold ' + npsTone(q.nps)}>{q.nps === null ? '—' : q.nps > 0 ? `+${q.nps}` : q.nps}</div>
            <div className="text-xs text-slate-400">NPS</div>
          </div>
        )}
        {q.kind === 'stars' && q.average !== null && (
          <div className="flex items-center gap-1 text-2xl font-bold text-amber-500">
            <Star className="h-6 w-6 fill-amber-400" /> {q.average.toFixed(2)}
          </div>
        )}
      </div>

      {q.kind === 'nps' && q.answers > 0 && (
        <div>
          <div className="flex h-2.5 overflow-hidden rounded-full bg-slate-100">
            <div className="bg-emerald-500" style={{ width: `${(100 * q.promoters) / q.answers}%` }} />
            <div className="bg-amber-400" style={{ width: `${(100 * q.passives) / q.answers}%` }} />
            <div className="bg-red-500" style={{ width: `${(100 * q.detractors) / q.answers}%` }} />
          </div>
          <div className="mt-1.5 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-500">
            <span>
              <span className="mr-1 inline-block h-2 w-2 rounded-full bg-emerald-500" />
              Tavsiya qiluvchilar (9–10): {q.promoters}
            </span>
            <span>
              <span className="mr-1 inline-block h-2 w-2 rounded-full bg-amber-400" />
              Betaraflar (7–8): {q.passives}
            </span>
            <span>
              <span className="mr-1 inline-block h-2 w-2 rounded-full bg-red-500" />
              Tanqidchilar (0–6): {q.detractors}
            </span>
          </div>
        </div>
      )}

      {q.distribution.length > 0 && q.kind !== 'nps' && (
        <div className="space-y-1.5">
          {q.distribution.map((d) => (
            <div key={d.label} className="flex items-center gap-3 text-sm">
              <span className="w-32 shrink-0 truncate text-slate-600">
                {q.kind === 'stars' ? `${'★'.repeat(Number(d.label))}` : d.label}
              </span>
              <div className="h-2 flex-1 overflow-hidden rounded-full bg-slate-100">
                <div className="h-full rounded-full bg-brand-500" style={{ width: `${(100 * d.count) / max}%` }} />
              </div>
              <span className="w-8 text-right font-medium text-slate-700">{d.count}</span>
            </div>
          ))}
        </div>
      )}

      {q.kind === 'nps' && q.answers > 0 && (
        <div className="flex items-end gap-1">
          {q.distribution.map((d) => {
            const score = Number(d.label)
            const color = score >= 9 ? 'bg-emerald-400' : score >= 7 ? 'bg-amber-300' : 'bg-red-300'
            return (
              <div key={d.label} className="flex flex-1 flex-col items-center gap-1">
                <span className="text-[11px] text-slate-400">{d.count || ''}</span>
                <div className={'w-full rounded-t ' + color} style={{ height: `${4 + (48 * d.count) / max}px` }} />
                <span className="text-[11px] font-medium text-slate-500">{d.label}</span>
              </div>
            )
          })}
        </div>
      )}

      {q.subjects.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-100 text-left text-xs font-medium uppercase tracking-wide text-slate-400">
                <th className="py-2 pr-3">{q.target === 'teachers' ? 'Ustoz' : 'Rahbar'}</th>
                <th className="py-2 pr-3 text-center">Javob</th>
                <th className="py-2 pr-3 text-center">O'rtacha</th>
                {q.kind === 'nps' && <th className="py-2 text-right">NPS</th>}
              </tr>
            </thead>
            <tbody>
              {q.subjects.map((s) => (
                <tr key={s.id} className="border-b border-slate-50 last:border-0">
                  <td className="py-2 pr-3 text-slate-700">{s.name || '—'}</td>
                  <td className="py-2 pr-3 text-center text-slate-500">{s.answers}</td>
                  <td className="py-2 pr-3 text-center text-slate-500">{s.average?.toFixed(1) ?? '—'}</td>
                  {q.kind === 'nps' && (
                    <td className={'py-2 text-right font-semibold ' + npsTone(s.nps)}>
                      {s.nps === null ? '—' : s.nps > 0 ? `+${s.nps}` : s.nps}
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {q.texts.length > 0 && (
        <div className="space-y-2">
          {q.texts.map((t, i) => (
            <div key={i} className="flex gap-3 rounded-xl bg-slate-50 px-3 py-2.5">
              <MessageSquareText className="mt-0.5 h-4 w-4 shrink-0 text-slate-300" />
              <div className="min-w-0">
                <p className="whitespace-pre-line break-words text-sm text-slate-700">{t.text}</p>
                <p className="mt-0.5 text-xs text-slate-400">
                  {[t.respondentName, t.className, t.subjectName, fmtDate(t.at)].filter(Boolean).join(' · ')}
                </p>
              </div>
            </div>
          ))}
        </div>
      )}
    </Card>
  )
}
