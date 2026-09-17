import { useState } from 'react'
import { Sparkles, BookOpen, ChevronDown, ChevronUp, ArrowRight, GraduationCap } from 'lucide-react'
import { BigTitle } from '../components/ui'
import { TapScale } from '../components/AppCard'
import { Loading, ErrorState } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'

// Journal picker — quarter banner + per-grade expandable class cards, plus a
// separate section for study groups.
//
// A study group has no grade (`ownerKind === 'group'`, `grade === 0`), so it
// cannot sit in a grade bucket: it is listed on its own below the classes.
// Groups only arrive from the server once the cut-over switch is on
// (docs/modules/students-parity.md §4.3), so this section is simply empty today.
export default function JournalPickerScreen({ onNavigate }) {
  const classesQ = useFetch(() => api.classes(), [])
  const metaQ = useFetch(() => api.meta(), [])

  if (classesQ.loading && !classesQ.data) return <ScreenShell><Loading /></ScreenShell>
  if (classesQ.error) return <ScreenShell><ErrorState error={classesQ.error} onRetry={classesQ.reload} /></ScreenShell>

  const owners = classesQ.data || []
  const classes = owners.filter((c) => c.ownerKind !== 'group')
  const groups = owners.filter((c) => c.ownerKind === 'group')
  if (owners.length === 0) {
    return (
      <ScreenShell>
        <EmptyState
          icon={<EmptyIllustration><GraduationCap size={30} /></EmptyIllustration>}
          title="Sinflar topilmadi"
          subtitle="Sizga biriktirilgan sinf, guruh yoki fan mavjud emas."
        />
      </ScreenShell>
    )
  }

  const meta = metaQ.data
  const grades = [...new Set(classes.map((c) => c.grade))].sort((a, b) => a - b)

  return (
    <div className="h-full flex flex-col bg-bg">
      <BigTitle title="Jurnal" subtitle="Sinf yoki guruh va fanni tanlang" />

      {meta && (
        <div className="px-4 pt-1 pb-3">
          <div className="px-3 py-2.5 rounded-xl bg-primary-soft flex items-center gap-2.5">
            <Sparkles size={18} className="text-primary" />
            <span className="text-[13px] font-semibold text-primary">
              {meta.currentQuarter}-chorak ochiq · {meta.currentWeek}-hafta
            </span>
          </div>
        </div>
      )}

      <div className="flex-1 overflow-y-auto no-scrollbar px-4 pb-6">
        {grades.map((g) => (
          <div key={g}>
            <p className="px-1 pb-2 text-[12px] font-bold text-muted tracking-wide">{g}-sinflar</p>
            {classes
              .filter((c) => c.grade === g)
              .map((c) => (
                <ClassCard
                  key={c.classId}
                  cls={c}
                  onPick={(subject) =>
                    onNavigate?.('journalGrid', {
                      classId: c.classId,
                      className: c.className,
                      subjectId: subject.id,
                      subjectName: subject.name,
                    })
                  }
                />
              ))}
            <div className="h-2" />
          </div>
        ))}

        {groups.length > 0 && (
          <div>
            <p className="px-1 pb-2 text-[12px] font-bold text-muted tracking-wide">O'quv guruhlari</p>
            {groups.map((g) => (
              <ClassCard
                key={g.classId}
                cls={g}
                onPick={(subject) =>
                  onNavigate?.('journalGrid', {
                    classId: g.classId,
                    className: g.className,
                    ownerKind: 'group',
                    subjectId: subject.id,
                    subjectName: subject.name,
                  })
                }
              />
            ))}
            <div className="h-2" />
          </div>
        )}
      </div>
    </div>
  )
}

function ScreenShell({ children }) {
  return (
    <div className="h-full flex flex-col bg-bg">
      <BigTitle title="Jurnal" subtitle="Sinf yoki guruh va fanni tanlang" />
      {children}
    </div>
  )
}

const CLASS_COLORS = ['#0D9488', '#0891B2', '#7C3AED', '#DB2777', '#F59E0B', '#10B981']
function classColor(name) {
  const hash = [...(name || '')].reduce((a, c) => a + c.charCodeAt(0), 0)
  return CLASS_COLORS[hash % CLASS_COLORS.length]
}

function ClassCard({ cls, onPick }) {
  const [open, setOpen] = useState(false)
  const color = classColor(cls.className)
  const subjects = cls.subjects || []
  const isGroup = cls.ownerKind === 'group'
  // A group name is free text, so the square avatar takes its first two letters.
  const badge = isGroup ? (cls.className || '').slice(0, 2).toUpperCase() : cls.className
  return (
    <div className="mb-2.5 rounded-3xl bg-surface border border-border">
      <button onClick={() => setOpen((v) => !v)} className="w-full p-3.5 flex items-center gap-3">
        <div className="w-12 h-12 rounded-xl flex items-center justify-center text-white text-[14px] font-extrabold" style={{ background: color }}>
          {badge}
        </div>
        <div className="flex-1 text-left">
          <div className="flex items-center gap-1.5">
            <span className="text-[15px] font-bold text-text">
              {isGroup ? cls.className : `${cls.className} sinfi`}
            </span>
            {isGroup && (
              <span className="px-1.5 py-0.5 rounded bg-primary-soft text-[10px] font-bold text-primary">GURUH</span>
            )}
            {!isGroup && cls.isHomeroom && (
              <span className="px-1.5 py-0.5 rounded bg-primary-soft text-[10px] font-bold text-primary">RAHBAR</span>
            )}
          </div>
          <p className="text-[12px] text-muted">{subjects.map((s) => s.name).join(', ')}</p>
        </div>
        {open ? <ChevronUp size={20} className="text-faint" /> : <ChevronDown size={20} className="text-faint" />}
      </button>
      {open && (
        <div className="px-3.5 pb-3.5 space-y-1.5">
          {subjects.map((s) => (
            <TapScale key={s.id} onClick={() => onPick(s)}>
              <div className="px-3.5 py-3 rounded-xl bg-surface2 border border-border/50 flex items-center gap-2.5">
                <div className="w-8 h-8 rounded-lg bg-surface flex items-center justify-center">
                  <BookOpen size={16} className="text-primary" />
                </div>
                <span className="flex-1 text-[14px] font-semibold text-text">{s.name}</span>
                <ArrowRight size={16} className="text-faint" />
              </div>
            </TapScale>
          ))}
        </div>
      )}
    </div>
  )
}
