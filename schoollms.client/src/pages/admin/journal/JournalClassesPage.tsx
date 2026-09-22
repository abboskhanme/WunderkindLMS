/**
 * Jurnal — 1-bosqich: sinflar ro'yxati (EduSchool oqimi, 2026-09-23).
 * Jurnal → sinf → fan: sinf tanlanadi, keyin uning fanlari, keyin fan jurnali.
 * Ko'rinish bizniki; funksiya — ularniki (CLAUDE.md).
 */
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { ChevronRight, Search } from 'lucide-react'
import { getJournalOwners } from '@/api/services/journal'
import { useAsync } from '@/hooks/useAsync'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Pager } from '@/components/ui/Pager'
import { PAGE_SIZES } from '@/lib/pagination'

const LANGUAGE_LABELS: Record<string, string> = { uz: "O'zbek", ru: 'Rus', en: 'Ingliz' }

export function JournalClassesPage() {
  const { data, loading, error } = useAsync(getJournalOwners, [])
  const [q, setQ] = useState('')
  const [pageSize, setPageSize] = useState<number>(PAGE_SIZES[0])
  const [page, setPage] = useState(1)

  const rows = useMemo(() => {
    const needle = q.trim().toLocaleLowerCase('uz')
    return (data ?? []).filter(
      (o) =>
        !needle ||
        o.name.toLocaleLowerCase('uz').includes(needle) ||
        (o.homeroomTeacher ?? '').toLocaleLowerCase('uz').includes(needle),
    )
  }, [data, q])

  const lastPage = Math.max(1, Math.ceil(rows.length / pageSize))
  const current = Math.min(page, lastPage)
  const pageRows = rows.slice((current - 1) * pageSize, current * pageSize)

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Jurnal</h1>
          <p className="text-sm text-slate-400">Sinfni tanlang — keyin fan, keyin uning jurnali</p>
        </div>
        <label className="flex h-10 w-full max-w-xs items-center gap-2 rounded-full bg-white px-4 ring-1 ring-slate-200">
          <Search className="h-4 w-4 text-slate-400" />
          <input
            value={q}
            onChange={(e) => {
              setQ(e.target.value)
              setPage(1)
            }}
            placeholder="Sinf yoki sinf rahbari"
            className="min-w-0 flex-1 bg-transparent text-sm outline-none placeholder:text-slate-400"
          />
        </label>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : error ? (
        <p className="text-red-600">Xatolik: {error}</p>
      ) : (
        <Card className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px] table-fixed text-sm">
              <colgroup>
                <col className="w-16" />
                <col className="w-[22%]" />
                <col className="w-[16%]" />
                <col />
                <col className="w-[14%]" />
                <col className="w-32" />
              </colgroup>
              <thead className="whitespace-nowrap">
                <tr className="border-b border-slate-100 text-left text-xs uppercase tracking-wide text-slate-400">
                  <th className="px-4 py-3 font-medium">№</th>
                  <th className="px-3 py-3 font-medium">Sinf</th>
                  <th className="px-3 py-3 font-medium">Ta'lim tili</th>
                  <th className="px-3 py-3 font-medium">Sinf rahbari</th>
                  <th className="px-3 py-3 font-medium">O'quvchilar</th>
                  <th className="px-4 py-3 text-right font-medium">Jurnal</th>
                </tr>
              </thead>
              <tbody>
                {pageRows.map((o, i) => (
                  <tr key={o.id} className="border-b border-slate-50 last:border-0 hover:bg-slate-50/60">
                    <td className="px-4 py-2.5 tabular-nums text-slate-400">{(current - 1) * pageSize + i + 1}</td>
                    <td className="truncate px-3 py-2.5 font-medium text-slate-800">
                      {o.kind === 'group' ? (
                        <span title={o.subjectName ?? undefined}>
                          {o.name}{' '}
                          <span className="rounded-full bg-violet-50 px-1.5 py-0.5 text-[10px] font-medium text-violet-600">
                            guruh
                          </span>
                        </span>
                      ) : (
                        o.name
                      )}
                    </td>
                    <td className="px-3 py-2.5 text-slate-500">
                      {o.language ? (LANGUAGE_LABELS[o.language] ?? o.language) : '—'}
                    </td>
                    <td className="truncate px-3 py-2.5 text-slate-600" title={o.homeroomTeacher ?? undefined}>
                      {o.homeroomTeacher || '—'}
                    </td>
                    <td className="px-3 py-2.5 tabular-nums text-slate-500">{o.studentCount}</td>
                    <td className="px-4 py-2.5 text-right">
                      <Link
                        to={`/admin/journal/${o.id}`}
                        className="inline-flex items-center gap-1 rounded-lg bg-slate-100 px-3 py-1.5 text-xs font-medium text-slate-700 transition-colors hover:bg-slate-200"
                      >
                        Ko'rish <ChevronRight className="h-3.5 w-3.5" />
                      </Link>
                    </td>
                  </tr>
                ))}
                {pageRows.length === 0 && (
                  <tr>
                    <td colSpan={6} className="px-4 py-10 text-center text-slate-400">
                      Sinf topilmadi
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
          <Pager
            total={rows.length}
            page={current}
            lastPage={lastPage}
            pageSize={pageSize}
            onPage={setPage}
            onPageSize={(n) => {
              setPageSize(n)
              setPage(1)
            }}
          />
        </Card>
      )}
    </div>
  )
}
