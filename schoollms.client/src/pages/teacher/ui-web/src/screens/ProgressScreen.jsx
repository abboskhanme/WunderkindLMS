import { useState } from 'react'
import { ArrowLeft, AlertTriangle } from 'lucide-react'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'
import { Loading, ErrorState } from '../components/State'

// Progress (Dars o'tilishi) — quarter tabs + gradient hero + per-class bars.
// Behind-schedule items (conducted < expectedByToday) shown in red.
export default function ProgressScreen({ onBack }) {
  const metaQ = useFetch(() => api.meta(), [])
  const [quarter, setQuarter] = useState(null)
  const effQuarter = quarter ?? metaQ.data?.currentQuarter ?? 1
  const q = useFetch(() => api.progress(effQuarter), [effQuarter])

  return (
    <div className="h-full flex flex-col bg-bg">
      <div className="flex items-center gap-1 px-2 pt-2 pb-1">
        {onBack && (
          <button onClick={onBack} className="w-10 h-10 rounded-xl flex items-center justify-center text-text">
            <ArrowLeft size={20} />
          </button>
        )}
        <div>
          <p className="text-[20px] font-extrabold text-text" style={{ letterSpacing: '-0.025em' }}>Dars o'tilishi</p>
          <p className="text-[12px] text-muted">O'tilgan darslar progresi</p>
        </div>
      </div>

      {/* Quarter tabs */}
      <div className="px-4 pt-1 pb-2">
        <div className="flex p-[3px] rounded-xl bg-surface2">
          {[1, 2, 3, 4].map((qq) => {
            const on = effQuarter === qq
            return (
              <button
                key={qq}
                onClick={() => setQuarter(qq)}
                className={['flex-1 py-2 rounded-[10px] text-[13px] transition-all', on ? 'bg-surface shadow-soft font-bold text-text' : 'font-semibold text-muted'].join(' ')}
              >
                {qq}-chorak
              </button>
            )
          })}
        </div>
      </div>

      {q.loading && !q.data ? (
        <Loading />
      ) : q.error ? (
        <ErrorState error={q.error} onRetry={q.reload} />
      ) : (
        <ProgressBody d={q.data} />
      )}
    </div>
  )
}

function ProgressBody({ d }) {
  const totalRemaining = Math.max(0, (d.totalPlanned || 0) - (d.totalConducted || 0))
  return (
    <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-2 pb-6">
      {/* Hero */}
      <div className="p-5 rounded-6xl text-white shadow-glow" style={{ background: 'linear-gradient(135deg,#134E4A,#0F766E,#0D9488)' }}>
        <p className="text-[11px] font-bold tracking-wide text-white/80">{d.quarter}-CHORAK · UMUMIY PROGRESS</p>
        <div className="flex items-baseline gap-1 mt-1.5">
          <span className="text-[40px] font-extrabold font-mono leading-none">{d.totalPercent}</span>
          <span className="text-[20px] font-bold text-white/90">%</span>
        </div>
        <p className="mt-1 text-[13px] text-white/85">{d.totalConducted} / {d.totalPlanned} dars o'tildi</p>
        <div className="mt-4 h-2 rounded bg-white/20 overflow-hidden">
          <div className="h-full rounded" style={{ width: `${d.totalPercent}%`, background: '#FFE48F' }} />
        </div>
        <div className="mt-4 flex gap-2.5">
          <HeroMini label="Reja" value={d.totalPlanned} unit="dars" />
          <HeroMini label="O'tildi" value={d.totalConducted} unit="dars" />
          <HeroMini label="Qoldi" value={totalRemaining} unit="dars" />
        </div>
      </div>

      <p className="px-1 pt-4 pb-2 text-[13px] font-bold text-text">Sinf va fanlar bo'yicha</p>
      <div className="space-y-2.5">
        {(d.items || []).map((it, i) => {
          const isBehind = it.conducted < it.expectedByToday
          const barColor = isBehind ? '#EF4444' : 'var(--primary)'
          return (
            <div key={i} className="p-4 rounded-4xl bg-surface border border-border">
              <div className="flex items-start">
                <div className="flex-1">
                  <div className="flex items-center gap-2">
                    <span className="text-[15px] font-bold text-text">{it.subjectName}</span>
                    {it.subGroup > 0 && <span className="px-1.5 py-0.5 rounded bg-surface3 text-[10px] font-bold text-muted">{it.subGroup}-G</span>}
                  </div>
                  <p className="text-[13px] text-muted">{it.className}</p>
                </div>
                <span className="text-[18px] font-extrabold font-mono" style={{ color: barColor }}>{it.percent}%</span>
              </div>
              <div className="mt-3 h-1.5 rounded bg-surface3 overflow-hidden">
                <div className="h-full rounded" style={{ width: `${it.percent}%`, background: barColor }} />
              </div>
              <div className="mt-2.5 flex items-center text-[12px]">
                <span className="font-semibold text-muted">{it.conducted} / {it.planned} o'tildi</span>
                {it.remaining > 0 && <span className="ml-auto text-faint">{it.remaining} qoldi</span>}
              </div>
              {isBehind && (
                <div className="mt-2 px-2.5 py-1.5 rounded-lg bg-danger/10 flex items-center gap-1.5">
                  <AlertTriangle size={14} className="text-danger" />
                  <span className="text-[11px] font-semibold text-danger">Rejadan orqada · bugungacha {it.expectedByToday} ta kerak edi</span>
                </div>
              )}
            </div>
          )
        })}
      </div>
    </div>
  )
}

function HeroMini({ label, value, unit }) {
  return (
    <div className="flex-1 p-2.5 rounded-xl bg-white/[0.12]">
      <p className="text-[10px] text-white/85">{label}</p>
      <p className="text-[18px] font-extrabold font-mono mt-1">{value}</p>
      <p className="text-[10px] text-white/70">{unit}</p>
    </div>
  )
}
