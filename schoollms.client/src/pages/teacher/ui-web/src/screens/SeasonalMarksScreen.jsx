import { useMemo, useState } from 'react'
import { BarChart3, ChevronLeft, ChevronRight } from 'lucide-react'
import { ScreenHeader, FieldLabel } from '../components/ui'
import SegmentedControl from '../components/SegmentedControl'
import { TapScale } from '../components/AppCard'
import { Loading, ErrorState } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'
import {
  PERIOD_KIND_OPTIONS,
  QUARTER_OPTIONS,
  SCORE_LABEL,
  loadPeriod,
  periodCaption,
  savePeriod,
  seasonalErrorView,
  shiftMonth,
  shiftYear,
  yearBounds,
} from '../lib/seasonal'

// Mavsumiy baholash — the teacher's entry point (admission-and-testing.md §3.4,
// screen 14 = screens 10 + 11 restricted to the teacher's own pairs).
//
// Order is the spec's: tur → davr → sinf → fan. The period is chosen here, then
// every (class, subject) pair the teacher teaches is one row; a tap opens the
// bulk-entry screen for that pair and period. The pairs come from
// `GET /api/teacher/seasonal-marks/scope`, which the server already limits to
// this teacher — the screen renders what it gets and filters nothing itself.
//
// The teacher API has no list-of-marks endpoint, so the admin "list" (screen 10)
// is the entry screen itself: stored marks load prefilled there, and emptying
// both fields removes one.
export default function SeasonalMarksScreen({ onNavigate, onBack }) {
  const [period, setPeriodState] = useState(loadPeriod)
  const setPeriod = (next) => {
    setPeriodState(next)
    savePeriod(next)
  }

  const scopeQ = useFetch(() => api.seasonalScope(), [])
  const groups = useMemo(() => groupPairs(scopeQ.data), [scopeQ.data])

  return (
    <div className="h-full flex flex-col bg-bg">
      <ScreenHeader title="Mavsumiy baholash" subtitle={`${SCORE_LABEL} · oylik, choraklik, yillik`} onBack={onBack} />

      <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-2 pb-6">
        <PeriodCard period={period} onChange={setPeriod} />

        {scopeQ.loading && !scopeQ.data ? (
          <Loading label="Sinflar yuklanmoqda…" />
        ) : scopeQ.error ? (
          <ErrorState error={seasonalErrorView(scopeQ.error)} onRetry={scopeQ.reload} />
        ) : groups.length === 0 ? (
          <EmptyState
            icon={<EmptyIllustration><BarChart3 size={30} /></EmptyIllustration>}
            title="Baholanadigan sinf va fan yo'q"
            subtitle="Sinf va fanlar dars jadvalidan olinadi — jadvalda sizga dars biriktirilmagan."
          />
        ) : (
          <div className="mt-4">
            {groups.map((g) => (
              <div key={g.key}>
                <p className="px-1 pb-2 text-[12px] font-bold text-muted tracking-wide">{g.title}</p>
                {g.pairs.map((p) => (
                  <PairRow
                    key={`${p.classId}|${p.subjectId}`}
                    pair={p}
                    onOpen={() =>
                      onNavigate?.('seasonalEntry', {
                        classId: p.classId,
                        className: p.className,
                        subjectId: p.subjectId,
                        subjectName: p.subjectName,
                        period,
                      })
                    }
                  />
                ))}
                <div className="h-2" />
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

// Tur (segmented) + davr (stepper; quarters as a second segmented row).
function PeriodCard({ period, onChange }) {
  const { min, max } = yearBounds()
  const monthly = period.kind === 'monthly'
  const monthIndex = period.year * 12 + period.month - 1
  const canPrev = monthly ? monthIndex > min * 12 : period.year > min
  const canNext = monthly ? monthIndex < max * 12 + 11 : period.year < max
  const step = (delta) => onChange(monthly ? shiftMonth(period, delta) : shiftYear(period, delta))
  const caption = monthly ? periodCaption(period) : `${period.year}-yil`

  return (
    <div className="p-4 rounded-4xl bg-surface border border-border shadow-card space-y-3">
      <div className="space-y-2">
        <FieldLabel>Baholash turi</FieldLabel>
        <SegmentedControl
          value={period.kind}
          onChange={(kind) => onChange({ ...period, kind })}
          options={PERIOD_KIND_OPTIONS}
        />
      </div>

      <div className="space-y-2">
        <FieldLabel>{monthly ? 'Oy' : 'Yil'}</FieldLabel>
        <div className="flex items-center gap-2">
          <StepButton label="Oldingi" disabled={!canPrev} onClick={() => step(-1)}>
            <ChevronLeft size={20} />
          </StepButton>
          <div className="flex-1 h-11 rounded-xl bg-surface2 border border-border flex items-center justify-center">
            <span className="text-[15px] font-bold text-text">{caption}</span>
          </div>
          <StepButton label="Keyingi" disabled={!canNext} onClick={() => step(1)}>
            <ChevronRight size={20} />
          </StepButton>
        </div>
      </div>

      {period.kind === 'quarterly' && (
        <div className="space-y-2">
          <FieldLabel>Chorak</FieldLabel>
          <SegmentedControl
            value={period.quarter}
            onChange={(quarter) => onChange({ ...period, quarter })}
            options={QUARTER_OPTIONS}
          />
        </div>
      )}
    </div>
  )
}

function StepButton({ label, disabled, onClick, children }) {
  return (
    <button
      type="button"
      aria-label={label}
      disabled={disabled}
      onClick={onClick}
      className="w-11 h-11 shrink-0 rounded-xl bg-surface2 border border-border flex items-center justify-center text-text disabled:opacity-40"
    >
      {children}
    </button>
  )
}

// Same class palette as the journal picker, as Tailwind classes.
const CLASS_BADGES = ['bg-teal-600', 'bg-cyan-600', 'bg-violet-600', 'bg-pink-600', 'bg-warning', 'bg-success']
function classBadge(name) {
  const hash = [...(name || '')].reduce((a, c) => a + c.charCodeAt(0), 0)
  return CLASS_BADGES[hash % CLASS_BADGES.length]
}

function PairRow({ pair, onOpen }) {
  return (
    <div className="mb-2.5">
      <TapScale onClick={onOpen}>
        <div className="p-3.5 rounded-3xl bg-surface border border-border flex items-center gap-3">
          <div
            className={[
              'w-12 h-12 shrink-0 rounded-xl flex items-center justify-center text-white text-[13px] font-extrabold',
              classBadge(pair.className),
            ].join(' ')}
          >
            {(pair.className || '').slice(0, 4)}
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-[15px] font-bold text-text truncate">{pair.className} sinfi</p>
            <p className="text-[12px] text-muted truncate">{pair.subjectName}</p>
          </div>
          <ChevronRight size={20} className="text-faint shrink-0" />
        </div>
      </TapScale>
    </div>
  )
}

const byName = (a, b) => String(a).localeCompare(String(b), 'uz', { numeric: true, sensitivity: 'base' })

/**
 * ScopeDto → [{ key, title, pairs: [{ classId, className, subjectId, subjectName }] }],
 * grouped by grade ("5-sinflar"), classes and subjects in reading order.
 * A pair is listed once even if the schedule has it more than once.
 */
function groupPairs(scope) {
  if (!scope) return []
  const classes = new Map((scope.classes || []).map((c) => [c.id, c]))
  const seen = new Set()
  const rows = []
  for (const p of scope.pairs || []) {
    const cls = classes.get(p.classId)
    const key = `${p.classId}|${p.subjectId}`
    if (!cls || seen.has(key)) continue
    seen.add(key)
    rows.push({
      classId: p.classId,
      className: cls.name,
      grade: Number(cls.grade) || 0,
      subjectId: p.subjectId,
      subjectName: p.subjectName,
    })
  }
  rows.sort((a, b) => a.grade - b.grade || byName(a.className, b.className) || byName(a.subjectName, b.subjectName))

  const groups = []
  for (const r of rows) {
    const key = String(r.grade)
    let g = groups.find((x) => x.key === key)
    if (!g) {
      g = { key, title: r.grade > 0 ? `${r.grade}-sinflar` : 'Boshqa', pairs: [] }
      groups.push(g)
    }
    g.pairs.push(r)
  }
  return groups
}
