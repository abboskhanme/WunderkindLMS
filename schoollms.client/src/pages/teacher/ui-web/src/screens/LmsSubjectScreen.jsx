import { useState } from 'react'
import { PlayCircle, Paperclip, ChevronRight, BarChart3, FileText, Users } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import SegmentedControl from '../components/SegmentedControl'
import { TapScale } from '../components/AppCard'
import { AsyncView } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'

// LMS subject — Mavzular (topics) tab + Progress (per-student) tab.
export default function LmsSubjectScreen({ params, onBack, onNavigate }) {
  const subjectId = params?.subjectId
  const title = params?.title || 'Fan'
  const className = params?.className
  const [tab, setTab] = useState('topics')

  const topicsQ = useFetch(() => api.lmsTopics(subjectId), [subjectId])
  const progressQ = useFetch(() => api.lmsProgress(subjectId), [subjectId])

  const topics = topicsQ.data || []
  const headerTitle = className ? `${className} · ${title}` : title

  return (
    <div className="h-full flex flex-col bg-bg">
      <ScreenHeader title={headerTitle} subtitle={`${topics.length} mavzu`} onBack={onBack} titleSize={15} />
      <div className="px-4 pt-1 pb-2">
        <SegmentedControl
          value={tab}
          onChange={setTab}
          options={[
            { value: 'topics', label: 'Mavzular' },
            { value: 'progress', label: 'Progress' },
          ]}
        />
      </div>

      {tab === 'topics' ? (
        <AsyncView
          query={topicsQ}
          empty={
            <EmptyState
              icon={<EmptyIllustration><FileText size={30} /></EmptyIllustration>}
              title="Mavzular yo'q"
              subtitle="Bu fanda hozircha mavzular qo'shilmagan."
            />
          }
        >
          <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-1 pb-6">
            {topics.map((t) => {
              const materials = t.materials || []
              return (
                <TapScale key={t.id} onClick={() => onNavigate?.('lmsTopicDetail', { topic: t })} className="block mb-2.5">
                  <div className="p-3.5 rounded-3xl bg-surface border border-border flex items-center gap-3">
                    <div className="w-10 h-10 rounded-xl flex items-center justify-center text-primary font-extrabold font-mono text-[15px]" style={{ background: 'color-mix(in srgb, var(--primary) 10%, transparent)' }}>
                      {t.order}
                    </div>
                    <div className="flex-1 min-w-0">
                      <p className="text-[14px] font-bold text-text">{t.title}</p>
                      {t.description && <p className="text-[12px] text-muted truncate">{t.description}</p>}
                      {(t.videoUrl || materials.length > 0) && (
                        <div className="mt-1.5 flex items-center gap-2 text-faint">
                          {t.videoUrl && <PlayCircle size={14} />}
                          {materials.length > 0 && (
                            <span className="flex items-center gap-0.5">
                              <Paperclip size={13} />
                              <span className="text-[11px]">{materials.length}</span>
                            </span>
                          )}
                        </div>
                      )}
                    </div>
                    <div className="flex flex-col items-end gap-1">
                      {t.completedCount > 0 && (
                        <span className="px-2 py-0.5 rounded-lg bg-success/10 text-[11px] font-bold text-success">✓ {t.completedCount}</span>
                      )}
                      <ChevronRight size={18} className="text-faint" />
                    </div>
                  </div>
                </TapScale>
              )
            })}
          </div>
        </AsyncView>
      ) : (
        <AsyncView
          query={progressQ}
          empty={
            <EmptyState
              icon={<EmptyIllustration><Users size={30} /></EmptyIllustration>}
              title="O'quvchilar yo'q"
              subtitle="Bu fan bo'yicha progress ma'lumoti mavjud emas."
            />
          }
        >
          <ProgressView data={progressQ.data} subjectId={subjectId} onNavigate={onNavigate} />
        </AsyncView>
      )}
    </div>
  )
}

function ProgressView({ data, subjectId, onNavigate }) {
  const report = data || { topics: [], students: [] }
  const students = report.students || []
  const topics = report.topics || []

  if (students.length === 0) {
    return (
      <EmptyState
        icon={<EmptyIllustration><Users size={30} /></EmptyIllustration>}
        title="O'quvchilar yo'q"
        subtitle="Bu fan bo'yicha progress ma'lumoti mavjud emas."
      />
    )
  }

  return (
    <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-1 pb-6">
      <div className="mb-3 px-4 py-3 rounded-2xl bg-primary-soft border border-primary/20 flex items-center gap-2.5">
        <BarChart3 size={18} className="text-primary" />
        <span className="flex-1 text-[13px] font-semibold text-primary">{topics.length} ta mavzu · {students.length} o'quvchi</span>
        <span className="text-[12px] font-bold text-primary">
          {students.filter((s) => s.totalCount > 0 && s.completedCount === s.totalCount).length} ta yakunladi
        </span>
      </div>
      {students.map((st) => {
        const pct = st.totalCount > 0 ? st.completedCount / st.totalCount : 0
        const complete = pct >= 1
        const barColor = complete ? '#10B981' : 'var(--primary)'
        return (
          <TapScale
            key={st.studentId}
            onClick={() => onNavigate?.('lmsStudentTopics', { subjectId, student: st, topics })}
            className="block mb-2"
          >
            <div className="px-3.5 py-3 rounded-2xl bg-surface flex items-center gap-3" style={{ border: complete ? '1px solid #10B98150' : '1px solid var(--border)' }}>
              <div className="flex-1 min-w-0">
                <p className="text-[14px] font-bold text-text truncate">{st.fullName}</p>
                <div className="mt-1.5 h-[5px] rounded bg-border overflow-hidden">
                  <div className="h-full rounded" style={{ width: `${pct * 100}%`, background: barColor }} />
                </div>
              </div>
              <div className="text-right">
                <p className="text-[14px] font-extrabold font-mono" style={{ color: barColor }}>{st.completedCount}/{st.totalCount}</p>
                <p className="text-[11px] font-semibold text-muted">{Math.round(pct * 100)}%</p>
              </div>
              <ChevronRight size={16} className="text-faint" />
            </div>
          </TapScale>
        )
      })}
    </div>
  )
}
