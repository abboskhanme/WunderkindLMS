import { useState } from 'react'
import { Plus, Edit3, Paperclip, CheckCircle, Video, Clock, Inbox, Trash2, FileText } from 'lucide-react'
import { BigTitle } from '../components/ui'
import { TapScale } from '../components/AppCard'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { AsyncView } from '../components/State'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'

const FORMAT_META = {
  written: { icon: Edit3, label: 'Yozma', color: '#0EA5E9' },
  text: { icon: Edit3, label: 'Yozma', color: '#0EA5E9' },
  file: { icon: Paperclip, label: 'Fayl', color: '#7C3AED' },
  test: { icon: CheckCircle, label: 'Test', color: '#0D9488' },
  video: { icon: Video, label: 'Video', color: '#DB2777' },
}
const DEFAULT_META = { icon: FileText, label: 'Topshiriq', color: '#64748B' }
const metaOf = (fmt) => FORMAT_META[fmt] || DEFAULT_META

const FILTERS = [
  ['all', 'Hammasi'],
  ['test', 'Test'],
  ['written', 'Yozma'],
  ['file', 'Fayl'],
  ['video', 'Video'],
]

// Assignments list — format filter chips + class chips + cards + FAB. Live API.
export default function AssignmentsScreen({ onNavigate }) {
  const q = useFetch(() => api.assignments(), [])
  const [filter, setFilter] = useState('all')
  const [classFilter, setClassFilter] = useState('all')
  const [deletingId, setDeletingId] = useState(null)

  const items = q.data || []
  const classList = [...new Set(items.flatMap((a) => a.classNames || []))].sort()

  const matchesFilter = (a) =>
    filter === 'all' || a.format === filter || (filter === 'written' && a.format === 'text')

  const filtered = items.filter(
    (a) => matchesFilter(a) && (classFilter === 'all' || (a.classNames || []).includes(classFilter)),
  )

  const onDelete = async (id) => {
    if (!confirm('Topshiriqni o\'chirishni tasdiqlaysizmi?')) return
    setDeletingId(id)
    try {
      await api.deleteAssignment(id)
      await q.reload()
    } catch (e) {
      alert((e && e.message) || 'O\'chirib bo\'lmadi')
    } finally {
      setDeletingId(null)
    }
  }

  return (
    <div className="relative h-full flex flex-col bg-bg">
      <BigTitle title="Topshiriqlar" subtitle={`${items.length} ta faol`} />

      {/* Format chips */}
      <div className="flex gap-1.5 px-4 py-3 overflow-x-auto no-scrollbar">
        {FILTERS.map(([val, label]) => {
          const on = filter === val
          const count =
            val === 'all'
              ? items.length
              : items.filter((a) => a.format === val || (val === 'written' && a.format === 'text')).length
          return (
            <button
              key={val}
              onClick={() => setFilter(val)}
              className={['shrink-0 px-3 py-2 rounded-full border flex items-center gap-1.5 text-[13px] font-semibold', on ? 'bg-primary border-primary text-white' : 'bg-surface border-border text-text'].join(' ')}
            >
              {label}
              <span className={['px-1.5 rounded-lg text-[10px] font-extrabold font-mono', on ? 'bg-white/25 text-white' : 'bg-surface3 text-muted'].join(' ')}>{count}</span>
            </button>
          )
        })}
      </div>

      {/* Class chips */}
      <div className="flex gap-1.5 px-4 pb-2 overflow-x-auto no-scrollbar">
        {['all', ...classList].map((c) => {
          const on = classFilter === c
          return (
            <button
              key={c}
              onClick={() => setClassFilter(c)}
              className={['shrink-0 px-3.5 py-1.5 rounded-full border text-[13px] font-semibold', on ? 'bg-primary-soft border-primary text-primary' : 'bg-surface border-border text-text'].join(' ')}
            >
              {c === 'all' ? 'Barcha sinflar' : c}
            </button>
          )
        })}
      </div>

      <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-1 pb-24">
        <AsyncView
          query={q}
          empty={
            <EmptyState
              icon={<EmptyIllustration><Inbox size={40} /></EmptyIllustration>}
              title="Topshiriqlar yo'q"
              subtitle="Hozircha topshiriq yaratilmagan."
            />
          }
        >
          {filtered.length === 0 ? (
            <EmptyState
              icon={<EmptyIllustration><Inbox size={40} /></EmptyIllustration>}
              title="Topshiriqlar topilmadi"
              subtitle="Bu turdagi topshiriqlar hozircha yo'q."
            />
          ) : (
            filtered.map((a) => (
              <AssignmentCard
                key={a.id}
                a={a}
                deleting={deletingId === a.id}
                onOpen={() => onNavigate?.('assignmentResults', { id: a.id, title: a.title })}
                onDelete={() => onDelete(a.id)}
              />
            ))
          )}
        </AsyncView>
      </div>

      <button
        onClick={() => onNavigate?.('assignmentCreate')}
        className="absolute bottom-6 right-5 w-[60px] h-[60px] rounded-3xl bg-primary text-white flex items-center justify-center shadow-fab"
      >
        <Plus size={28} />
      </button>
    </div>
  )
}

function AssignmentCard({ a, onOpen, onDelete, deleting }) {
  const meta = metaOf(a.format)
  const Icon = meta.icon
  const classNames = a.classNames || []
  const daysLeft = a.dueDate ? Math.ceil((new Date(a.dueDate) - new Date()) / 86400000) : null
  return (
    <TapScale onClick={onOpen} className="block mb-3">
      <div className="p-4 rounded-4xl bg-surface border border-border">
        <div className="flex items-center gap-3">
          <div className="w-11 h-11 rounded-xl flex items-center justify-center" style={{ background: `${meta.color}1F`, color: meta.color }}>
            <Icon size={20} />
          </div>
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-1 text-[10px] font-bold tracking-wide" style={{ color: meta.color }}>
              {meta.label.toUpperCase()}
              <span className="text-faint"> · </span>
              <span className="text-[11px] font-semibold text-muted truncate">{a.subjectName}</span>
            </div>
            <p className="mt-0.5 text-[15px] font-bold text-text leading-snug">{a.title}</p>
          </div>
          <button
            onClick={(e) => {
              e.stopPropagation()
              if (!deleting) onDelete()
            }}
            className="w-9 h-9 shrink-0 rounded-xl bg-surface2 flex items-center justify-center text-danger disabled:opacity-50"
            disabled={deleting}
            aria-label="O'chirish"
          >
            <Trash2 size={16} />
          </button>
        </div>
        {classNames.length > 0 && (
          <div className="mt-3 flex flex-wrap gap-1.5">
            {classNames.map((c) => (
              <span key={c} className="px-2.5 py-1 rounded-lg bg-chip text-[12px] font-semibold text-text">{c}</span>
            ))}
          </div>
        )}
        {a.description && (
          <p className="mt-3 text-[12px] text-muted leading-relaxed line-clamp-2">{a.description}</p>
        )}
        <div className="mt-3 pt-3 border-t border-border/50 flex items-center gap-1.5">
          <Clock size={14} className="text-muted" />
          <span className={['text-[11px] font-semibold', daysLeft != null && daysLeft < 0 ? 'text-danger' : 'text-muted'].join(' ')}>
            {daysLeft == null ? 'Muddat yo\'q' : daysLeft > 0 ? `${daysLeft} kun qoldi` : daysLeft === 0 ? 'Bugun tugaydi' : `${Math.abs(daysLeft)} kun kech`}
          </span>
          {a.maxScore != null && <span className="ml-auto text-[11px] text-muted font-mono">Max {a.maxScore}</span>}
        </div>
      </div>
    </TapScale>
  )
}
