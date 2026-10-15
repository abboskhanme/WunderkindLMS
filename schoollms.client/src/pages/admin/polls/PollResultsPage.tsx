import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ArrowLeft, BarChart3, Download, Lock, MessageSquareText, Star, Table2 } from 'lucide-react'
import {
  downloadPollResponses,
  getPollResponses,
  getPollResults,
  hasOptions,
  npsTone,
  POLL_AUDIENCE,
  POLL_OTHER,
  POLL_TARGET,
  type PollAudience,
  type PollQuestionResult,
  type PollResponses,
  type PollResults,
} from '@/api/services/polls'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Pager } from '@/components/ui/Pager'
import { fmtDate } from '@/components/lesson-analysis/format'
import { cn } from '@/lib/utils'

type Tab = 'summary' | 'responses'

export function PollResultsPage() {
  const { id = '' } = useParams()
  const [className, setClassName] = useState('')
  const [tab, setTab] = useState<Tab>('summary')
  const [data, setData] = useState<PollResults | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    getPollResults(id, className || undefined)
      .then(setData)
      .catch((e) => setError(hrErrorMessage(e, "Natijalarni yuklab bo'lmadi")))
  }, [id, className])

  if (error) return <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>
  if (!data) return <Loader label="Yuklanmoqda..." />

  // Sections are headings, not numbered questions.
  const numbers = new Map(data.questions.filter((q) => q.kind !== 'section').map((q, i) => [q.questionId, i + 1]))
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

      <div className="flex w-fit gap-1 rounded-xl bg-slate-100 p-1">
        {[
          { key: 'summary' as const, label: 'Xulosa', icon: BarChart3 },
          { key: 'responses' as const, label: 'Javoblar', icon: Table2 },
        ].map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setTab(t.key)}
            className={cn(
              'flex items-center gap-1.5 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
              tab === t.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
            )}
          >
            <t.icon className="h-4 w-4" /> {t.label}
          </button>
        ))}
      </div>

      {tab === 'responses' ? (
        // Keyed by the class filter: a new filter starts again from page 1.
        <ResponsesTable key={className} pollId={id} className={className} />
      ) : data.responses === 0 ? (
        <Card>
          <p className="py-10 text-center text-slate-400">Hali javob yo'q</p>
        </Card>
      ) : (
        data.questions.map((q) =>
          q.kind === 'section' ? (
            <div key={q.questionId} className="pt-2">
              <h2 className="text-lg font-semibold text-slate-800">{q.text}</h2>
              {q.description && <p className="text-sm text-slate-500">{q.description}</p>}
            </div>
          ) : (
            <QuestionResult key={q.questionId} index={numbers.get(q.questionId) ?? 0} q={q} audience={data.audience} />
          ),
        )
      )}
    </div>
  )
}

function QuestionResult({ index, q, audience }: { index: number; q: PollQuestionResult; audience: PollAudience }) {
  const max = Math.max(1, ...q.distribution.map((d) => d.count))
  return (
    <Card className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-xs font-medium uppercase tracking-wide text-slate-400">
            {index}-savol{audience === 'public' ? '' : ` · ${POLL_TARGET[q.target]}`} · {q.answers} ta javob
          </p>
          <h2 className="mt-0.5 text-base font-semibold text-slate-800">{q.text}</h2>
          {q.kind === 'multi' && <p className="text-xs text-slate-400">Bir nechta variant tanlash mumkin — foiz javob berganlardan</p>}
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
        {q.kind === 'scale' && q.average !== null && (
          <div className="text-right">
            <div className="text-2xl font-bold text-slate-800">{q.average.toFixed(2)}</div>
            <div className="text-xs text-slate-400">o'rtacha</div>
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

      {q.kind === 'number' && q.answers > 0 && (
        <div className="grid grid-cols-3 gap-3">
          {[
            { label: 'Eng kichik', value: q.min },
            { label: "O'rtacha", value: q.average },
            { label: 'Eng katta', value: q.max },
          ].map((s) => (
            <div key={s.label} className="rounded-xl bg-slate-50 px-3 py-2.5 text-center">
              <div className="text-lg font-semibold text-slate-800">{s.value === null ? '—' : s.value.toLocaleString('ru-RU')}</div>
              <div className="text-xs text-slate-400">{s.label}</div>
            </div>
          ))}
        </div>
      )}

      {q.distribution.length > 0 && q.kind !== 'nps' && (
        <div className="space-y-1.5">
          {q.distribution.map((d) => (
            <div key={d.label} className="flex items-center gap-3 text-sm">
              <span className="w-40 shrink-0 truncate text-slate-600" title={d.label}>
                {q.kind === 'stars' ? `${'★'.repeat(Number(d.label))}` : q.kind === 'date' ? fmtDate(d.label) : d.label}
              </span>
              <div className="h-2 flex-1 overflow-hidden rounded-full bg-slate-100">
                <div
                  className={cn('h-full rounded-full', d.label === POLL_OTHER && hasOptions(q.kind) ? 'bg-slate-400' : 'bg-brand-500')}
                  style={{ width: `${(100 * d.count) / max}%` }}
                />
              </div>
              <span className="w-8 text-right font-medium text-slate-700">{d.count}</span>
              <span className="w-10 text-right text-xs text-slate-400">
                {q.answers > 0 ? `${Math.round((100 * d.count) / q.answers)}%` : ''}
              </span>
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
          {hasOptions(q.kind) && <p className="text-xs font-medium uppercase tracking-wide text-slate-400">«{POLL_OTHER}» javoblari</p>}
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

/** "15.10.2026 14:30" in the browser's (the school's) time. */
function fmtDateTime(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getDate())}.${p(d.getMonth() + 1)}.${d.getFullYear()} ${p(d.getHours())}:${p(d.getMinutes())}`
}

/** "Javoblar": one row per response, a column per question — the same cells as the Excel file. */
function ResponsesTable({ pollId, className }: { pollId: string; className: string }) {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [data, setData] = useState<PollResponses | null>(null)
  const [error, setError] = useState('')
  const [downloading, setDownloading] = useState(false)

  useEffect(() => {
    let active = true
    getPollResponses(pollId, page, pageSize, className || undefined)
      .then((r) => active && setData(r))
      .catch((e) => active && setError(hrErrorMessage(e, "Javoblarni yuklab bo'lmadi")))
    return () => {
      active = false
    }
  }, [pollId, page, pageSize, className])

  const download = async () => {
    setDownloading(true)
    setError('')
    try {
      await downloadPollResponses(pollId, className || undefined)
    } catch (e) {
      setError(hrErrorMessage(e, "Faylni yuklab bo'lmadi"))
    } finally {
      setDownloading(false)
    }
  }

  if (!data) return error ? <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p> : <Loader label="Yuklanmoqda..." />
  const showClass = data.classes.length > 0
  const th = 'whitespace-nowrap px-3 py-2 text-left text-xs font-medium uppercase tracking-wide text-slate-400'

  return (
    <Card className="space-y-3 p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 px-4 pt-4">
        <p className="text-sm text-slate-500">{data.total} ta javob</p>
        <Button variant="secondary" disabled={downloading || data.total === 0} onClick={download}>
          <Download className="h-4 w-4" /> {downloading ? 'Tayyorlanmoqda...' : 'Excel'}
        </Button>
      </div>
      {error && <p className="mx-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      {data.total === 0 ? (
        <p className="py-10 text-center text-slate-400">Hali javob yo'q</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-y border-slate-100 bg-slate-50/60">
                <th className={th}>Vaqt</th>
                {data.showRespondent && <th className={th}>Kim</th>}
                {data.showPhone && <th className={th}>Telefon</th>}
                {showClass && <th className={th}>Sinf</th>}
                {data.showSource && <th className={th}>Manba</th>}
                {data.columns.map((c) => (
                  <th key={c.questionId} className={cn(th, 'min-w-40 max-w-xs whitespace-normal normal-case tracking-normal')} title={c.text}>
                    {c.text}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {data.rows.map((r) => (
                <tr key={r.id} className="border-b border-slate-50 align-top last:border-0">
                  <td className="whitespace-nowrap px-3 py-2 text-slate-500">{fmtDateTime(r.submittedAt)}</td>
                  {data.showRespondent && <td className="whitespace-nowrap px-3 py-2 text-slate-700">{r.respondent || '—'}</td>}
                  {data.showPhone && <td className="whitespace-nowrap px-3 py-2 text-slate-700">{r.phone ?? '—'}</td>}
                  {showClass && <td className="whitespace-nowrap px-3 py-2 text-slate-600">{r.className ?? '—'}</td>}
                  {data.showSource && <td className="whitespace-nowrap px-3 py-2 text-slate-500">{r.viaLink ? 'Havola' : 'Ilova'}</td>}
                  {r.cells.map((c, i) => (
                    <td key={i} className="max-w-xs px-3 py-2 text-slate-700">
                      <span className="line-clamp-3 whitespace-pre-line break-words" title={c}>
                        {c || <span className="text-slate-300">—</span>}
                      </span>
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <Pager
        total={data.total}
        page={data.page}
        lastPage={Math.max(1, Math.ceil(data.total / data.pageSize))}
        pageSize={data.pageSize}
        onPage={setPage}
        onPageSize={(n) => {
          setPageSize(n)
          setPage(1)
        }}
      />
    </Card>
  )
}
