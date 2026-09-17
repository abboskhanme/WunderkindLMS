import { useEffect, useState } from 'react'
import { Crown, Users } from 'lucide-react'
import { getMyGroups, type TeacherGroup } from '@/api/services/teacher'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { GroupRosterModal } from './GroupRosterModal'

/**
 * "Guruhlarim" — X-3 (students-parity.md §2.11, EduSchool `getStudentGroups` /
 * `updatedStudentGroup`). O'qituvchiga BIRIKTIRILGAN yoki jadvalda dars beradigan
 * o'quv guruhlari; ro'yxatni tahrirlash (GroupRosterModal ichida) FAQAT
 * biriktirilgan ("yetakchi") o'qituvchiga ochiq — <c>canEditRoster</c> backend
 * (TeacherPortalController.cs) belgilaydi, front esa faqat ko'rsatadi.
 */
export function TeacherGroupsPage() {
  const [groups, setGroups] = useState<TeacherGroup[]>([])
  const [loading, setLoading] = useState(true)
  const [openId, setOpenId] = useState<string | null>(null)

  const load = () => {
    getMyGroups()
      .then(setGroups)
      .finally(() => setLoading(false))
  }

  useEffect(load, [])

  const active = groups.find((g) => g.id === openId) ?? null

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Guruhlarim</h1>
        <p className="text-sm text-slate-400">
          Sizga biriktirilgan yoki dars beradigan o'quv guruhlari
        </p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : groups.length === 0 ? (
        <Card>
          <p className="py-8 text-center text-slate-400">
            Sizga biriktirilgan o'quv guruhi yo'q.
          </p>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {groups.map((g) => (
            <button
              key={g.id}
              type="button"
              onClick={() => setOpenId(g.id)}
              className="w-full space-y-2 rounded-2xl border border-slate-200/80 bg-white p-4 text-left shadow-sm transition-colors hover:border-brand-200 hover:bg-brand-50/30"
            >
              <div className="flex items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                  <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                    <Users className="h-5 w-5" />
                  </div>
                  <p className="font-semibold text-slate-800">{g.name}</p>
                </div>
                {g.canEditRoster && (
                  <span className="inline-flex shrink-0 items-center gap-1 rounded-full bg-amber-50 px-2 py-0.5 text-[11px] font-medium text-amber-700">
                    <Crown className="h-3 w-3" /> Yetakchi
                  </span>
                )}
              </div>
              <p className="text-xs text-slate-500">{g.subjectName}</p>
              <div className="flex flex-wrap items-center gap-1.5 text-xs text-slate-400">
                <span>{g.memberCount} o'quvchi</span>
                {g.classes.length > 0 && <span>· {g.classes.map((c) => c.name).join(', ')}</span>}
              </div>
            </button>
          ))}
        </div>
      )}

      {active && (
        <GroupRosterModal
          open
          groupId={active.id}
          groupName={active.name}
          canEdit={active.canEditRoster}
          onClose={() => setOpenId(null)}
          onChanged={load}
        />
      )}
    </div>
  )
}
