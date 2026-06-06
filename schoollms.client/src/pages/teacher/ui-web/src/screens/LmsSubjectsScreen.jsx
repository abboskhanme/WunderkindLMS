import { useState } from 'react'
import { BookOpen, ChevronRight, FileText, LockOpen } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import { TapScale } from '../components/AppCard'
import { AsyncView } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'

const UNLOCK = {
  sequential: ['Ketma-ket', '#3B82F6'],
  batch: ['Guruh', '#F59E0B'],
  all: ['Hammasi ochiq', '#10B981'],
}

// LMS subjects — class filter chips + subject cards with topic/unlock badges.
export default function LmsSubjectsScreen({ onNavigate, onBack }) {
  const [classFilter, setClassFilter] = useState('all')
  const q = useFetch(() => api.lmsSubjects(), [])
  const subjects = q.data || []
  const classes = [...new Set(subjects.map((s) => s.className))].sort()
  const filtered = classFilter === 'all' ? subjects : subjects.filter((s) => s.className === classFilter)

  return (
    <div className="h-full flex flex-col bg-bg">
      <ScreenHeader title="Ta'lim" subtitle="O'quv materiallar" onBack={onBack} />

      {classes.length > 0 && (
        <div className="flex gap-1.5 px-4 pb-2 overflow-x-auto no-scrollbar">
          {['all', ...classes].map((c) => {
            const on = classFilter === c
            return (
              <button
                key={c}
                onClick={() => setClassFilter(c)}
                className={['shrink-0 px-3.5 py-1.5 rounded-full border text-[13px] font-semibold', on ? 'bg-primary-soft border-primary text-primary' : 'bg-surface border-border text-text'].join(' ')}
              >
                {c === 'all' ? 'Barchasi' : c}
              </button>
            )
          })}
        </div>
      )}

      <AsyncView
        query={q}
        empty={
          <EmptyState
            icon={<EmptyIllustration><BookOpen size={30} /></EmptyIllustration>}
            title="Fanlar yo'q"
            subtitle="Hozircha siz uchun LMS materiallari mavjud emas."
          />
        }
      >
        <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-1 pb-6 space-y-3">
          {filtered.map((s) => {
            const [unlockLabel, unlockColor] = UNLOCK[s.unlockMode] || UNLOCK.all
            return (
              <TapScale
                key={s.id}
                onClick={() => onNavigate?.('lmsSubject', { subjectId: s.id, title: s.title, className: s.className })}
                className="block"
              >
                <div className="p-4 rounded-4xl bg-surface border border-border">
                  <div className="flex items-center gap-3">
                    <div className="w-11 h-11 rounded-xl bg-primary-soft flex items-center justify-center text-primary">
                      <BookOpen size={22} />
                    </div>
                    <div className="flex-1">
                      <span className="inline-block px-2 py-0.5 rounded-md bg-chip text-[11px] font-bold text-muted">{s.className}</span>
                      <p className="mt-1 text-[15px] font-bold text-text leading-snug">{s.title}</p>
                    </div>
                    <ChevronRight size={18} className="text-faint" />
                  </div>
                  {s.description && <p className="mt-2.5 text-[13px] text-muted leading-snug line-clamp-2">{s.description}</p>}
                  <div className="mt-3 flex gap-2">
                    <Badge icon={<FileText size={13} />} label={`${s.topicsCount} mavzu`} color="#0D9488" />
                    <Badge icon={<LockOpen size={13} />} label={s.unlockMode === 'batch' ? `Guruh (${s.batchSize} ta)` : unlockLabel} color={unlockColor} />
                  </div>
                </div>
              </TapScale>
            )
          })}
        </div>
      </AsyncView>
    </div>
  )
}

function Badge({ icon, label, color }) {
  return (
    <span className="px-2.5 py-1 rounded-lg flex items-center gap-1.5 text-[12px] font-semibold" style={{ background: `color-mix(in srgb, ${color} 10%, transparent)`, color }}>
      {icon}
      {label}
    </span>
  )
}
