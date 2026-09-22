import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Users } from 'lucide-react'
import {
  getCoverageDetail,
  seasonalErrorMessage,
  type CoverageRowDto,
} from '@/api/services/seasonalMarks'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'
import { Segmented } from './PeriodPicker'
import { EmptyState, ErrorState, Pager } from './StateViews'
import {
  SCORE_COLUMN_LABEL,
  formatScore,
  periodCaption,
  periodSearch,
  toPeriodQuery,
  type PeriodSelection,
} from './periods'
import { useRemote } from './useRemote'

/** Which pupils behind a coverage number: all, marked, not marked. */
export type CoverageSlice = 'all' | 'marked' | 'unmarked'

const SLICES: readonly CoverageSlice[] = ['all', 'marked', 'unmarked']
const sliceLabels: Record<CoverageSlice, string> = {
  all: 'Hammasi',
  marked: 'Baholangan',
  unmarked: 'Baholanmagan',
}

const PAGE_SIZE = 50

export interface CoverageDrill {
  teacher: CoverageRowDto
  slice: CoverageSlice
}

interface CoverageDetailModalProps {
  drill: CoverageDrill | null
  period: PeriodSelection
  onClose: () => void
}

/**
 * The pupil list behind one coverage cell — `GET /coverage/detail` with
 * `hasMark` (§6.6). The counts in the header are the teacher row's own
 * server numbers; the list itself is paged by the server.
 */
export function CoverageDetailModal({ drill, period, onClose }: CoverageDetailModalProps) {
  return (
    <Modal open={drill !== null} onClose={onClose} size="lg" title={drill?.teacher.fullName ?? ''}>
      {drill && (
        <DetailBody
          key={`${drill.teacher.teacherId}|${drill.slice}`}
          drill={drill}
          period={period}
        />
      )}
    </Modal>
  )
}

function DetailBody({ drill, period }: { drill: CoverageDrill; period: PeriodSelection }) {
  const [slice, setSlice] = useState<CoverageSlice>(drill.slice)
  const [page, setPage] = useState(1)
  const teacherId = drill.teacher.teacherId
  const hasMark = slice === 'all' ? undefined : slice === 'marked'

  const detail = useRemote(JSON.stringify({ teacherId, period, slice, page }), () =>
    getCoverageDetail({ teacherId, ...toPeriodQuery(period), hasMark, page, limit: PAGE_SIZE }),
  )

  const counts: Record<CoverageSlice, number> = {
    all: drill.teacher.totalStudents,
    marked: drill.teacher.marked,
    unmarked: drill.teacher.unmarked,
  }
  const listLink = `/admin/seasonal-marks?${periodSearch(period).toString()}`

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <Segmented<CoverageSlice>
          options={SLICES}
          value={slice}
          label={(s) => `${sliceLabels[s]} · ${counts[s]}`}
          onChange={(s) => {
            setPage(1)
            setSlice(s)
          }}
          ariaLabel="O'quvchilar"
        />
        <span className="text-sm text-slate-400">{periodCaption(period)}</span>
      </div>

      <div className="overflow-hidden rounded-xl border border-slate-100">
        {detail.loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : detail.error ? (
          <ErrorState
            title="Ro'yxatni yuklab bo'lmadi"
            message={seasonalErrorMessage(detail.error)}
            onRetry={detail.reload}
          />
        ) : !detail.data || detail.data.items.length === 0 ? (
          <EmptyState
            icon={Users}
            title={
              slice === 'unmarked'
                ? "Baholanmagan o'quvchi yo'q"
                : slice === 'marked'
                  ? "Hali birorta o'quvchi baholanmagan"
                  : "O'quvchi topilmadi"
            }
          />
        ) : (
          <>
            <div className="max-h-[50vh] overflow-auto">
              <table className="w-full text-left text-sm">
                <thead className="sticky top-0 whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-2.5">O'quvchi</th>
                    <th className="px-4 py-2.5">Sinf</th>
                    <th className="px-4 py-2.5">Fan</th>
                    <th className="px-4 py-2.5 normal-case">{SCORE_COLUMN_LABEL}</th>
                    <th className="px-4 py-2.5" />
                  </tr>
                </thead>
                <tbody className={cn('divide-y divide-slate-100', detail.refreshing && 'opacity-60')}>
                  {detail.data.items.map((r) => (
                    <tr key={`${r.studentId}|${r.subjectId}`} className="hover:bg-slate-50/60">
                      <td className="px-4 py-2.5 font-medium text-slate-800">
                        <span className="block max-w-[14rem] truncate" title={r.fullName}>
                          {r.fullName}
                        </span>
                      </td>
                      <td className="whitespace-nowrap px-4 py-2.5 text-slate-500">{r.className}</td>
                      <td className="px-4 py-2.5 text-slate-600">
                        <span className="block max-w-[12rem] truncate" title={r.subjectName}>
                          {r.subjectName}
                        </span>
                      </td>
                      <td className="whitespace-nowrap px-4 py-2.5 tabular-nums">
                        {r.hasMark ? (
                          <span className="font-semibold text-slate-800">
                            {formatScore(r.score) || 'faqat izoh'}
                          </span>
                        ) : (
                          <span className="text-xs text-amber-600">Baholanmagan</span>
                        )}
                      </td>
                      <td className="whitespace-nowrap px-4 py-2.5 text-right">
                        {r.hasMark && (
                          <Link
                            to={`${listLink}&studentId=${encodeURIComponent(r.studentId)}&subjectId=${encodeURIComponent(r.subjectId)}`}
                            className="text-xs font-medium text-brand-600 hover:text-brand-700"
                          >
                            Bahoni ochish
                          </Link>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pager
              page={page}
              limit={PAGE_SIZE}
              total={detail.data.total}
              disabled={detail.loading}
              onPage={setPage}
            />
          </>
        )}
      </div>
    </div>
  )
}
