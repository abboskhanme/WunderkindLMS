import type { LmsProgressReport } from '@/types'
import { Card } from '@/components/ui/Card'
import { cn } from '@/lib/utils'

/**
 * O'quvchilar × mavzular progress matritsasi (kim qaysi mavzuni tugatgan).
 * Admin va o'qituvchi LMS sahifalarida bir xil ko'rinish uchun umumiy komponent.
 */
export function LmsProgressMatrix({ progress }: { progress: LmsProgressReport }) {
  if (progress.students.length === 0)
    return <Card className="py-12 text-center text-slate-400">O'quvchilar yo'q</Card>

  return (
    <Card className="p-0">
      <div className="overflow-x-auto">
        <table className="w-full border-separate border-spacing-0 text-sm">
          <thead>
            <tr className="bg-slate-50">
              <th className="sticky left-0 z-10 min-w-[180px] bg-slate-50 px-4 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
                O'quvchi
              </th>
              {progress.topics.map((topic) => (
                <th key={topic.id} title={topic.title} className="min-w-[60px] px-2 py-2.5 text-center">
                  <div className="mx-auto flex h-6 w-6 items-center justify-center rounded-md bg-slate-200 text-xs font-semibold text-slate-600">
                    {topic.order}
                  </div>
                  <p className="mt-1 max-w-[60px] truncate text-[10px] font-normal text-slate-400">
                    {topic.title}
                  </p>
                </th>
              ))}
              <th className="px-3 py-2.5 text-center text-xs font-semibold uppercase tracking-wide text-slate-500">
                Jami
              </th>
            </tr>
          </thead>

          <tbody>
            {progress.students.map((student, idx) => {
              const doneSet = new Set(student.completedTopicIds)
              const pct =
                student.totalCount > 0
                  ? Math.round((student.completedCount / student.totalCount) * 100)
                  : 0
              return (
                <tr
                  key={student.studentId}
                  className={cn(
                    'border-t border-slate-100 hover:bg-slate-50/60',
                    idx % 2 === 1 && 'bg-slate-50/30',
                  )}
                >
                  <td className="sticky left-0 z-10 bg-white px-4 py-2 font-medium text-slate-800">
                    {student.fullName}
                  </td>

                  {progress.topics.map((topic) => (
                    <td key={topic.id} className="px-2 py-2 text-center">
                      {doneSet.has(topic.id) ? (
                        <span className="inline-flex h-6 w-6 items-center justify-center rounded-full bg-emerald-100 text-emerald-700">
                          ✓
                        </span>
                      ) : (
                        <span className="inline-block h-6 w-6 rounded-full border-2 border-slate-200" />
                      )}
                    </td>
                  ))}

                  <td className="px-3 py-2 text-center">
                    <div className="mx-auto w-14">
                      <p
                        className={cn(
                          'text-sm font-bold',
                          pct === 100 ? 'text-emerald-600' : pct >= 50 ? 'text-brand-600' : 'text-slate-500',
                        )}
                      >
                        {pct}%
                      </p>
                      <p className="text-[10px] text-slate-400">
                        {student.completedCount}/{student.totalCount}
                      </p>
                    </div>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>

      <div className="border-t border-slate-100 px-4 py-2.5 text-xs text-slate-400">
        <span className="mr-4 inline-flex items-center gap-1">
          <span className="inline-flex h-4 w-4 items-center justify-center rounded-full bg-emerald-100 text-[10px] text-emerald-700">✓</span>
          Tugallagan
        </span>
        <span className="inline-flex items-center gap-1">
          <span className="inline-block h-4 w-4 rounded-full border-2 border-slate-200" />
          Tugallamagan
        </span>
      </div>
    </Card>
  )
}
