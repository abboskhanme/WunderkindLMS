import { useEffect, useState } from 'react'
import { Archive } from 'lucide-react'
import type { TeacherReportRow, TeacherReportDetail } from '@/types'
import { getTeacherReport, getTeacherReportDetail } from '@/api/services/teacherReports'
import { quarters } from '@/config/constants'
import { formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'

const statusMeta: Record<TeacherReportRow['status'], { label: string; cls: string }> = {
  active: { label: 'Faol', cls: 'bg-emerald-50 text-emerald-700' },
  low: { label: 'Sust', cls: 'bg-amber-50 text-amber-700' },
  none: { label: "Ma'lumot yo'q", cls: 'bg-slate-100 text-slate-500' },
}

export function TeacherReportsPage() {
  const [quarter, setQuarter] = useState(0) // 0 = barcha choraklar
  const [rows, setRows] = useState<TeacherReportRow[]>([])
  const [loading, setLoading] = useState(true)

  const [detail, setDetail] = useState<TeacherReportDetail | null>(null)
  const [detailOpen, setDetailOpen] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- chorak o'zgarganda hisobotni qayta yuklash (maqsadli)
    setLoading(true)
    setDetailOpen(false)
    getTeacherReport(quarter)
      .then(setRows)
      .finally(() => setLoading(false))
  }, [quarter])

  const openDetail = (id: string) => {
    setDetailOpen(true)
    setDetail(null)
    setDetailLoading(true)
    getTeacherReportDetail(id, quarter)
      .then(setDetail)
      .finally(() => setDetailLoading(false))
  }

  const counts = {
    active: rows.filter((r) => r.status === 'active').length,
    low: rows.filter((r) => r.status === 'low').length,
    none: rows.filter((r) => r.status === 'none').length,
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">O'qituvchilar hisoboti</h1>
          <p className="text-sm text-slate-400">
            Dars o'tilishi (jadvalga nisbatan), baho, mavzu va uy vazifa faolligi
          </p>
        </div>
        <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
          <QuarterBtn active={quarter === 0} onClick={() => setQuarter(0)}>
            Barcha
          </QuarterBtn>
          {quarters.map((q) => (
            <QuarterBtn key={q} active={quarter === q} onClick={() => setQuarter(q)}>
              {q}-chorak
            </QuarterBtn>
          ))}
        </div>
      </div>

      <div className="grid grid-cols-3 gap-3">
        <StatCard label="Faol" value={counts.active} cls="text-emerald-600" />
        <StatCard label="Sust" value={counts.low} cls="text-amber-600" />
        <StatCard label="Ma'lumot yo'q" value={counts.none} cls="text-slate-400" />
      </div>

      <Card className="p-0">
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">O'qituvchi</th>
                  <th className="px-4 py-3 text-center">Reja</th>
                  <th className="px-4 py-3 text-center">O'tdi</th>
                  <th className="px-4 py-3 text-center">Bajarildi</th>
                  <th className="px-4 py-3 text-center">Baho</th>
                  <th className="px-4 py-3 text-center">Mavzu</th>
                  <th className="px-4 py-3 text-center">Uy vaz.</th>
                  <th className="px-4 py-3">Oxirgi faollik</th>
                  <th className="px-4 py-3">Holat</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r) => (
                  <tr
                    key={r.teacherId}
                    onClick={() => openDetail(r.teacherId)}
                    className="cursor-pointer hover:bg-slate-50/60"
                  >
                    <td className="px-4 py-3 font-medium text-slate-800">
                      <span className="flex items-center gap-2">
                        {r.fullName}
                        {r.isArchived && (
                          <span className="inline-flex items-center gap-1 rounded bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-slate-500">
                            <Archive className="h-3 w-3" /> arxiv
                          </span>
                        )}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-center text-slate-600">{r.expected}</td>
                    <td className="px-4 py-3 text-center text-slate-600">{r.conducted}</td>
                    <td className="px-4 py-3 text-center">
                      <PctCell v={r.donePct} />
                    </td>
                    <td className="px-4 py-3 text-center text-slate-600">{r.grades}</td>
                    <td className="px-4 py-3 text-center">
                      <PctCell v={r.topicPct} />
                    </td>
                    <td className="px-4 py-3 text-center">
                      <PctCell v={r.homeworkPct} />
                    </td>
                    <td className="px-4 py-3 text-slate-500">
                      {r.lastActivity ? formatDate(r.lastActivity) : '—'}
                    </td>
                    <td className="px-4 py-3">
                      <StatusBadge status={r.status} />
                    </td>
                  </tr>
                ))}
                {rows.length === 0 && (
                  <tr>
                    <td colSpan={9} className="px-4 py-12 text-center text-slate-400">
                      O'qituvchi yo'q
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Modal
        open={detailOpen}
        onClose={() => setDetailOpen(false)}
        size="xl"
        title={detail ? `${detail.fullName} — hisobot` : 'Hisobot'}
      >
        {detailLoading || !detail ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              <Metric label="Reja" value={String(detail.expected)} />
              <Metric label="O'tildi" value={`${detail.conducted}`} />
              <Metric label="Bajarildi" value={detail.donePct == null ? '—' : `${detail.donePct}%`} />
              <Metric label="Baholar" value={String(detail.grades)} />
            </div>

            <div className="overflow-x-auto rounded-xl border border-slate-100">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-3 py-2">Sinf</th>
                    <th className="px-3 py-2">Fan</th>
                    <th className="px-3 py-2 text-center">Reja</th>
                    <th className="px-3 py-2 text-center">O'tdi</th>
                    <th className="px-3 py-2 text-center">Bajarildi</th>
                    <th className="px-3 py-2 text-center">Baho</th>
                    <th className="px-3 py-2 text-center">Mavzu</th>
                    <th className="px-3 py-2 text-center">Uy vaz.</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {detail.rows.map((b, i) => (
                    <tr key={i}>
                      <td className="px-3 py-2 font-medium text-slate-700">{b.className}</td>
                      <td className="px-3 py-2 text-slate-600">
                        {b.subjectName}
                        {b.subGroup > 0 && (
                          <span
                            className={cn(
                              'ml-1 rounded px-1 text-[10px] font-semibold',
                              b.subGroup === 1
                                ? 'bg-sky-200 text-sky-800'
                                : 'bg-violet-200 text-violet-800',
                            )}
                          >
                            G{b.subGroup}
                          </span>
                        )}
                      </td>
                      <td className="px-3 py-2 text-center text-slate-600">{b.expected}</td>
                      <td className="px-3 py-2 text-center text-slate-600">{b.conducted}</td>
                      <td className="px-3 py-2 text-center">
                        <PctCell v={b.donePct} />
                      </td>
                      <td className="px-3 py-2 text-center text-slate-600">{b.grades}</td>
                      <td className="px-3 py-2 text-center">
                        <PctCell v={b.topicPct} />
                      </td>
                      <td className="px-3 py-2 text-center">
                        <PctCell v={b.homeworkPct} />
                      </td>
                    </tr>
                  ))}
                  {detail.rows.length === 0 && (
                    <tr>
                      <td colSpan={8} className="px-3 py-8 text-center text-slate-400">
                        Bu davr uchun ma'lumot yo'q
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </Modal>
    </div>
  )
}

function PctCell({ v }: { v: number | null }) {
  if (v == null) return <span className="text-slate-300">—</span>
  const color = v >= 70 ? 'text-emerald-600' : v >= 40 ? 'text-amber-600' : 'text-red-600'
  return <span className={cn('font-medium', color)}>{v}%</span>
}

function StatusBadge({ status }: { status: TeacherReportRow['status'] }) {
  const m = statusMeta[status]
  return (
    <span className={cn('rounded-md px-2 py-0.5 text-xs font-medium', m.cls)}>{m.label}</span>
  )
}

function StatCard({ label, value, cls }: { label: string; value: number; cls: string }) {
  return (
    <Card className="flex flex-col">
      <span className="text-xs text-slate-400">{label}</span>
      <span className={cn('text-2xl font-semibold', cls)}>{value}</span>
    </Card>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl bg-slate-50 px-3 py-2">
      <p className="text-xs text-slate-400">{label}</p>
      <p className="text-lg font-semibold text-slate-800">{value}</p>
    </div>
  )
}

function QuarterBtn({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
        active ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
      )}
    >
      {children}
    </button>
  )
}
