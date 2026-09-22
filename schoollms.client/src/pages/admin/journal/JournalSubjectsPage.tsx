/**
 * Jurnal — 2-bosqich: tanlangan sinfning fanlari va ularni o'tadigan o'qituvchilar
 * (EduSchool oqimi, 2026-09-23). Ro'yxat dars jadvalidan — jurnal ustunlari ham shundan.
 */
import { Link, useParams } from 'react-router-dom'
import { ChevronRight } from 'lucide-react'
import { getJournalOwners, getJournalSubjects } from '@/api/services/journal'
import { useAsync } from '@/hooks/useAsync'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'

export function JournalSubjectsPage() {
  const { classId = '' } = useParams<{ classId: string }>()
  const subjects = useAsync(() => getJournalSubjects(classId), [classId])
  const owners = useAsync(getJournalOwners, [])
  const owner = owners.data?.find((o) => o.id === classId)
  const title = owner ? (owner.kind === 'group' ? `Guruh: ${owner.name}` : `${owner.name}-sinf`) : ''

  return (
    <div className="space-y-6">
      <div>
        <nav className="flex items-center gap-1 text-sm text-slate-400" aria-label="Yo'l">
          <Link to="/admin/journal" className="hover:text-slate-700">
            Jurnal
          </Link>
          <ChevronRight className="h-4 w-4" />
          <span>{title || '...'}</span>
        </nav>
        <h1 className="mt-1 text-xl font-semibold text-slate-800">{title || 'Fanlar'}</h1>
        {owner?.homeroomTeacher && <p className="text-sm text-slate-400">Sinf rahbari: {owner.homeroomTeacher}</p>}
      </div>

      {subjects.loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : subjects.error ? (
        <p className="text-red-600">Xatolik: {subjects.error}</p>
      ) : (
        <Card className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[560px] table-fixed text-sm">
              <colgroup>
                <col className="w-16" />
                <col className="w-[32%]" />
                <col />
                <col className="w-32" />
              </colgroup>
              <thead className="whitespace-nowrap">
                <tr className="border-b border-slate-100 text-left text-xs uppercase tracking-wide text-slate-400">
                  <th className="px-4 py-3 font-medium">№</th>
                  <th className="px-3 py-3 font-medium">Fan</th>
                  <th className="px-3 py-3 font-medium">O'qituvchi</th>
                  <th className="px-4 py-3 text-right font-medium">Jurnal</th>
                </tr>
              </thead>
              <tbody>
                {(subjects.data ?? []).map((s, i) => (
                  <tr key={s.subjectId} className="border-b border-slate-50 last:border-0 hover:bg-slate-50/60">
                    <td className="px-4 py-2.5 tabular-nums text-slate-400">{i + 1}</td>
                    <td className="truncate px-3 py-2.5 font-medium text-slate-800">{s.subjectName}</td>
                    <td className="truncate px-3 py-2.5 text-slate-600" title={s.teachers.join(', ')}>
                      {s.teachers.length ? s.teachers.join(', ') : '—'}
                    </td>
                    <td className="px-4 py-2.5 text-right">
                      <Link
                        to={`/admin/journal/${classId}/${s.subjectId}`}
                        className="inline-flex items-center gap-1 rounded-lg bg-slate-100 px-3 py-1.5 text-xs font-medium text-slate-700 transition-colors hover:bg-slate-200"
                      >
                        Ko'rish <ChevronRight className="h-3.5 w-3.5" />
                      </Link>
                    </td>
                  </tr>
                ))}
                {(subjects.data ?? []).length === 0 && (
                  <tr>
                    <td colSpan={4} className="px-4 py-10 text-center text-slate-400">
                      Bu sinfda fanlar yo'q — avval dars jadvali yarating
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </Card>
      )}
    </div>
  )
}
