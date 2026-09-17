import { ChevronRight, Crown, Users2 } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import { TapScale } from '../components/AppCard'
import { AsyncView } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'

const GROUP_COLORS = ['#0D9488', '#0891B2', '#7C3AED', '#DB2777', '#F59E0B', '#10B981']
function groupColor(name) {
  const hash = [...(name || '')].reduce((a, c) => a + c.charCodeAt(0), 0)
  return GROUP_COLORS[hash % GROUP_COLORS.length]
}

// Guruhlarim (X-3, students-parity.md §2.11) — teacher's own study groups: attached
// ("yetakchi") or with a scheduled lesson there, same set the journal picker's "O'quv
// guruhlari" section shows. The CROWN badge marks a group where this teacher may also
// edit the roster (`canEditRoster` — server-decided, TeacherPortalController.cs); a
// teacher who only teaches a lesson there sees the same card without the badge and the
// roster screen opens read-only for them.
export default function GroupsScreen({ onNavigate, onBack }) {
  const q = useFetch(() => api.groups(), [])
  const groups = q.data || []

  return (
    <div className="h-full flex flex-col bg-bg">
      <ScreenHeader
        title="Guruhlarim"
        subtitle={groups.length ? `${groups.length} ta guruh` : undefined}
        onBack={onBack}
      />
      <AsyncView query={q} loadingLabel="Yuklanmoqda…">
        {groups.length === 0 ? (
          <EmptyState
            icon={<EmptyIllustration><Users2 size={30} /></EmptyIllustration>}
            title="Guruh topilmadi"
            subtitle="Sizga biriktirilgan yoki dars beradigan o'quv guruhi yo'q"
          />
        ) : (
          <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-2 pb-6 space-y-2.5">
            {groups.map((g) => (
              <TapScale
                key={g.id}
                onClick={() =>
                  onNavigate?.('groupRoster', { id: g.id, name: g.name, canEdit: g.canEditRoster })
                }
              >
                <div className="p-3.5 rounded-3xl bg-surface border border-border flex items-center gap-3.5">
                  <div
                    className="w-12 h-12 rounded-xl flex items-center justify-center text-white text-[13px] font-extrabold shrink-0"
                    style={{ background: groupColor(g.name) }}
                  >
                    {(g.name || '').slice(0, 2).toUpperCase()}
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-1.5">
                      <span className="text-[15px] font-bold text-text truncate">{g.name}</span>
                      {g.canEditRoster && (
                        <span className="shrink-0 px-1.5 py-0.5 rounded bg-primary-soft text-[10px] font-bold text-primary flex items-center gap-0.5">
                          <Crown size={10} /> YETAKCHI
                        </span>
                      )}
                    </div>
                    <p className="text-[12px] text-muted truncate">
                      {g.subjectName} · {g.memberCount} o'quvchi
                      {g.classes?.length ? ` · ${g.classes.map((c) => c.name).join(', ')}` : ''}
                    </p>
                  </div>
                  <ChevronRight size={20} className="text-faint shrink-0" />
                </div>
              </TapScale>
            ))}
          </div>
        )}
      </AsyncView>
    </div>
  )
}
