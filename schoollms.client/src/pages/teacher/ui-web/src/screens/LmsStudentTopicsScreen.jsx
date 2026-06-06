import { Check, CheckCircle2, ListChecks } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import { AsyncView } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'

// LMS student topics — one student's per-topic completion checklist.
export default function LmsStudentTopicsScreen({ params, onBack }) {
  const student = params?.student
  const subjectId = params?.subjectId
  // Topics may be passed from the progress view; otherwise derive from the progress report.
  const passedTopics = params?.topics

  const q = useFetch(
    () => (passedTopics ? Promise.resolve({ topics: passedTopics }) : api.lmsProgress(subjectId)),
    [subjectId, !!passedTopics]
  )

  const completedIds = student?.completedTopicIds || []
  const completed = new Set(completedIds)
  const completedCount = student?.completedCount ?? completedIds.length
  const totalCount = student?.totalCount ?? (q.data?.topics?.length || 0)
  const pct = totalCount > 0 ? completedCount / totalCount : 0
  const barColor = pct >= 1 ? '#10B981' : 'var(--primary)'

  return (
    <div className="h-full flex flex-col bg-bg">
      <ScreenHeader
        title={student?.fullName || 'O\'quvchi'}
        subtitle={`${completedCount}/${totalCount} mavzu tugatgan`}
        onBack={onBack}
        titleSize={16}
        trailing={
          <span className="px-3 py-1 rounded-[10px] text-[14px] font-extrabold font-mono" style={{ background: `color-mix(in srgb, ${barColor} 12%, transparent)`, color: barColor }}>
            {Math.round(pct * 100)}%
          </span>
        }
      />
      <div className="px-4 pt-1 pb-3">
        <div className="h-[7px] rounded bg-border overflow-hidden">
          <div className="h-full rounded" style={{ width: `${pct * 100}%`, background: barColor }} />
        </div>
      </div>

      <AsyncView
        query={q}
        empty={
          <EmptyState
            icon={<EmptyIllustration><ListChecks size={30} /></EmptyIllustration>}
            title="Mavzular yo'q"
            subtitle="Bu fanda hozircha mavzular mavjud emas."
          />
        }
      >
        <TopicsList topics={q.data?.topics || []} completed={completed} />
      </AsyncView>
    </div>
  )
}

function TopicsList({ topics, completed }) {
  const sorted = [...topics].sort((a, b) => a.order - b.order)
  if (sorted.length === 0) {
    return (
      <EmptyState
        icon={<EmptyIllustration><ListChecks size={30} /></EmptyIllustration>}
        title="Mavzular yo'q"
        subtitle="Bu fanda hozircha mavzular mavjud emas."
      />
    )
  }
  return (
    <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-1 pb-8 space-y-2">
      {sorted.map((t) => {
        const done = completed.has(t.id)
        return (
          <div
            key={t.id}
            className="px-3.5 py-3 rounded-2xl flex items-center gap-3"
            style={{ background: done ? 'rgba(16,185,129,0.05)' : 'var(--surface)', border: done ? '1px solid rgba(16,185,129,0.28)' : '1px solid var(--border)' }}
          >
            <span
              className="w-[34px] h-[34px] rounded-full flex items-center justify-center"
              style={{ background: done ? 'rgba(16,185,129,0.15)' : 'var(--surface3)' }}
            >
              {done ? <Check size={18} className="text-success" /> : <span className="text-[13px] font-bold text-faint font-mono">{t.order}</span>}
            </span>
            <span className={['flex-1 text-[14px] leading-snug', done ? 'font-bold text-text' : 'font-medium text-muted'].join(' ')}>{t.title}</span>
            {done && <CheckCircle2 size={18} className="text-success" />}
          </div>
        )
      })}
    </div>
  )
}
