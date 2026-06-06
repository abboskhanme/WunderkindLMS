import { ArrowLeft, MonitorPlay, ExternalLink, FileText } from 'lucide-react'
import { AsyncView } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'

// LMS topic detail — title, description, video link, text, materials.
export default function LmsTopicDetailScreen({ params, onBack }) {
  const passed = params?.topic
  const subjectId = params?.subjectId ?? passed?.subjectId
  const topicId = params?.topicId ?? passed?.id

  // If the full topic object was passed, no fetch needed; otherwise look it up by id.
  const q = useFetch(
    () => (passed ? Promise.resolve([passed]) : api.lmsTopics(subjectId)),
    [subjectId, !!passed]
  )

  const topic = passed || (q.data || []).find((t) => t.id === topicId) || null

  return (
    <div className="h-full flex flex-col bg-bg">
      <div className="flex items-center gap-2 px-3 pt-2 pb-1">
        <button onClick={onBack} className="w-10 h-10 rounded-xl bg-surface2 flex items-center justify-center text-text">
          <ArrowLeft size={20} />
        </button>
        <p className="flex-1 text-[16px] font-extrabold text-text" style={{ letterSpacing: '-0.02em' }}>
          {topic ? `Mavzu ${topic.order}` : 'Mavzu'}
        </p>
        {topic && topic.completedCount > 0 && (
          <span className="px-2.5 py-1 rounded-[10px] bg-success/10 text-[12px] font-bold text-success">✓ {topic.completedCount}</span>
        )}
      </div>

      <AsyncView
        query={q}
        empty={
          <EmptyState
            icon={<EmptyIllustration><FileText size={30} /></EmptyIllustration>}
            title="Mavzu topilmadi"
            subtitle="Bu mavzu mavjud emas yoki o'chirilgan."
          />
        }
      >
        {topic ? <TopicBody topic={topic} /> : (
          <EmptyState
            icon={<EmptyIllustration><FileText size={30} /></EmptyIllustration>}
            title="Mavzu topilmadi"
            subtitle="Bu mavzu mavjud emas yoki o'chirilgan."
          />
        )}
      </AsyncView>
    </div>
  )
}

function TopicBody({ topic }) {
  const materials = topic.materials || []
  return (
    <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-2 pb-8">
      <p className="text-[20px] font-extrabold text-text leading-snug">{topic.title}</p>
      {topic.description && <p className="mt-2 text-[14px] text-muted leading-relaxed">{topic.description}</p>}

      {topic.videoUrl && (
        <>
          <p className="mt-5 text-[13px] font-bold text-text">Video</p>
          <a
            href={topic.videoUrl}
            target="_blank"
            rel="noreferrer"
            className="mt-2 p-3.5 rounded-2xl flex items-center gap-3"
            style={{ background: 'rgba(255,0,0,0.06)', border: '1px solid rgba(255,0,0,0.18)' }}
          >
            <div className="w-11 h-11 rounded-xl flex items-center justify-center" style={{ background: 'rgba(255,0,0,0.12)', color: '#FF0000' }}>
              <MonitorPlay size={22} />
            </div>
            <div className="flex-1 min-w-0">
              <p className="text-[13px] font-bold" style={{ color: '#FF0000' }}>Videoni ochish</p>
              <p className="text-[11px] text-muted truncate">{topic.videoUrl}</p>
            </div>
            <ExternalLink size={16} className="text-faint" />
          </a>
        </>
      )}

      {topic.textContent && (
        <>
          <p className="mt-5 text-[13px] font-bold text-text">Matn</p>
          <div className="mt-2 p-4 rounded-2xl bg-surface border border-border">
            <p className="text-[14px] text-text leading-relaxed whitespace-pre-line">{topic.textContent}</p>
          </div>
        </>
      )}

      {materials.length > 0 && (
        <>
          <p className="mt-5 text-[13px] font-bold text-text">Materiallar ({materials.length})</p>
          <div className="mt-2 space-y-2">
            {materials.map((m) => {
              const kb = Math.floor((m.size || 0) / 1024)
              const sizeLabel = kb >= 1024 ? `${(kb / 1024).toFixed(1)} MB` : `${kb} KB`
              return (
                <a
                  key={m.id || m.url || m.name}
                  href={m.url}
                  target="_blank"
                  rel="noreferrer"
                  download
                  className="px-3.5 py-3 rounded-xl bg-surface border border-border flex items-center gap-3"
                >
                  <div className="w-[42px] h-[42px] rounded-[10px] flex items-center justify-center" style={{ background: 'rgba(239,68,68,0.1)', color: '#EF4444' }}>
                    <FileText size={20} />
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className="text-[13px] font-bold text-text truncate">{m.name}</p>
                    <p className="text-[11px] text-muted">{sizeLabel}</p>
                  </div>
                  <ExternalLink size={16} className="text-faint" />
                </a>
              )
            })}
          </div>
        </>
      )}
    </div>
  )
}
