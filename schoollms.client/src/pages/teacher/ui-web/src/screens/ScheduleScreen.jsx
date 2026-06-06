import { useState, useMemo } from 'react'
import { SlidersHorizontal, ChevronRight, Calendar } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import SegmentedControl from '../components/SegmentedControl'
import EmptyState from '../components/EmptyState'
import { TapScale } from '../components/AppCard'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'
import { Loading, ErrorState } from '../components/State'

const DAY_SHORT = ['Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh']
const DAY_NAMES = ['Dushanba', 'Seshanba', 'Chorshanba', 'Payshanba', 'Juma', 'Shanba']

// Schedule — quarter/week header, day/week toggle, day picker, timeline.
export default function ScheduleScreen({ onBack }) {
  const [view, setView] = useState('day')
  const [selectedDay, setSelectedDay] = useState(() => (new Date().getDay() + 6) % 7)

  const metaQ = useFetch(() => api.meta(), [])
  const quarter = metaQ.data?.currentQuarter
  const week = metaQ.data?.currentWeek
  const q = useFetch(() => api.schedule(quarter, week), [quarter, week])

  // Group lessons by day (0..5), sorted by period within each day.
  const byDay = useMemo(() => {
    const map = { 0: [], 1: [], 2: [], 3: [], 4: [], 5: [] }
    for (const l of q.data || []) {
      if (map[l.day]) map[l.day].push(l)
    }
    for (const k of Object.keys(map)) map[k].sort((a, b) => a.period - b.period)
    return map
  }, [q.data])

  const dayLessons = byDay[selectedDay] || []
  const loading = metaQ.loading || (q.loading && !q.data)

  return (
    <div className="h-full flex flex-col bg-bg">
      <ScreenHeader
        title="Dars jadvali"
        subtitle={quarter ? `${quarter}-chorak · ${week}-hafta` : ' '}
        onBack={onBack}
        trailing={
          <div className="w-10 h-10 rounded-xl bg-surface2 flex items-center justify-center text-text">
            <SlidersHorizontal size={18} />
          </div>
        }
      />

      <div className="px-4 pt-2 pb-3">
        <SegmentedControl
          value={view}
          onChange={setView}
          options={[
            { value: 'day', label: 'Kun' },
            { value: 'week', label: 'Hafta' },
          ]}
        />
      </div>

      {view === 'day' && (
        <div className="flex gap-2 px-4 pb-1 overflow-x-auto no-scrollbar">
          {DAY_SHORT.map((d, i) => {
            const on = selectedDay === i
            const count = (byDay[i] || []).length
            return (
              <button
                key={i}
                onClick={() => setSelectedDay(i)}
                className="relative shrink-0 w-[60px] py-2.5 rounded-2xl border flex flex-col items-center transition-all"
                style={{ background: on ? 'var(--primary)' : 'var(--surface)', borderColor: on ? 'var(--primary)' : 'var(--border)' }}
              >
                <span className="text-[11px] font-semibold" style={{ color: on ? 'rgba(255,255,255,0.85)' : 'var(--muted)' }}>{d}</span>
                <span className="text-[17px] font-extrabold font-mono" style={{ color: on ? '#fff' : 'var(--text)' }}>{i + 16}</span>
                {count > 0 && <span className="absolute top-1 right-2 w-1.5 h-1.5 rounded-full" style={{ background: on ? '#fff' : 'var(--faint)' }} />}
              </button>
            )
          })}
        </div>
      )}

      <div className="flex-1 overflow-y-auto no-scrollbar">
        {loading ? (
          <Loading />
        ) : q.error ? (
          <ErrorState error={q.error} onRetry={q.reload} />
        ) : view === 'day' ? (
          dayLessons.length === 0 ? (
            <EmptyState icon={<Calendar size={40} className="text-faint" />} title="Bu kun darslar yo'q" subtitle="Boshqa kunni tanlang yoki dam oling." />
          ) : (
            <div className="px-4 pt-4 pb-6">
              {dayLessons.map((l, i) => (
                <div key={i} className="relative pl-14 pb-3">
                  <div className="absolute left-0 top-3.5 w-12 flex flex-col items-end">
                    <span className="text-[13px] font-bold text-text font-mono">{l.startTime}</span>
                    <span className="text-[10px] text-faint font-mono">{l.endTime}</span>
                  </div>
                  <span className="absolute left-6 top-5 w-4 h-4 rounded-full bg-surface" style={{ border: '3px solid var(--primary)' }} />
                  <TapScale>
                    <div className="p-3.5 rounded-2xl bg-surface border border-border flex items-center gap-3">
                      <span className="w-1 h-10 rounded bg-primary" />
                      <div className="flex-1">
                        <p className="text-[15px] font-bold text-text">{l.subjectName}</p>
                        <p className="text-[12px] text-muted">{l.className}{l.subGroup > 0 ? ` · ${l.subGroup}-guruh` : ''}</p>
                      </div>
                      <ChevronRight size={18} className="text-faint" />
                    </div>
                  </TapScale>
                </div>
              ))}
            </div>
          )
        ) : (
          <div className="px-4 pt-2 pb-6">
            {DAY_NAMES.map((name, idx) => {
              const lessons = byDay[idx] || []
              if (lessons.length === 0) return null
              return (
                <div key={idx} className="mb-4">
                  <div className="flex items-center gap-2 py-2 px-1">
                    <span className="text-[15px] font-extrabold text-text">{name}</span>
                    <span className="text-[12px] text-faint">· {lessons.length} dars</span>
                  </div>
                  {lessons.map((l, i) => (
                    <div key={i} className="mb-1.5 p-3 rounded-xl bg-surface border border-border flex items-center gap-3">
                      <span className="w-11 text-[12px] font-bold text-text font-mono">{l.startTime}</span>
                      <span className="w-px h-8 bg-border" />
                      <div className="flex-1">
                        <p className="text-[14px] font-bold text-text">{l.subjectName}</p>
                        <p className="text-[12px] text-muted">{l.className}</p>
                      </div>
                    </div>
                  ))}
                </div>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}
