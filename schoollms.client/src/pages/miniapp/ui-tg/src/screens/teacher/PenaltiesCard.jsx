/**
 * JARIMALAR — the teacher's own fines (client, 2026-10-07: "qo'yilgan jarima ustozga shaxsiy
 * kabinetida ko'rinishi"). The bot also sends a message when one is written or cancelled.
 *
 * Like the lesson analysis card: it loads itself (a failure here never takes the day's lessons
 * down) and stays hidden while there is nothing to show. Cancelled fines stay in the list, struck
 * through, with the reason — the teacher saw the message and should see where it went.
 *
 * No salary figure appears here (client, 2026-09-28) — only the fines themselves.
 */
import { useState } from 'react'
import { AlertTriangle } from 'lucide-react'
import { Badge, Card } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { fullDate, money, monthTitle } from '../../lib/format'
import { haptic } from '../../lib/telegram'
import { teacherApi } from '../../lib/teacherApi'

/** Rows shown before `Barchasi` is tapped. */
const PREVIEW = 3

export function PenaltiesCard() {
  const q = useAsync(() => teacherApi.penalties(), [])
  const [expanded, setExpanded] = useState(false)

  const rows = q.data ?? []
  // Always on the screen (2026-10-07: the same windows for every teacher) — empty says so.
  if (q.error || rows.length === 0)
    return (
      <Card title="Jarimalar">
        <p className="px-4 pb-4 text-[14px] text-slate-500">
          {q.loading && !q.data ? 'Yuklanmoqda…' : q.error ? "Ma'lumot olinmadi" : "Jarima yo'q"}
        </p>
      </Card>
    )

  const now = new Date()
  const thisMonth = rows.filter(
    (r) => !r.cancelled && r.periodYear === now.getFullYear() && r.periodMonth === now.getMonth() + 1,
  )
  const total = thisMonth.reduce((s, r) => s + Number(r.amount), 0)
  const shown = expanded ? rows : rows.slice(0, PREVIEW)

  return (
    <Card
      title="Jarimalar"
      action={
        rows.length > PREVIEW ? (
          <button
            type="button"
            onClick={() => {
              haptic('light')
              setExpanded((v) => !v)
            }}
            className="shrink-0 rounded-full bg-brand/25 px-3 py-1 text-[13px] font-semibold text-brand-ink"
          >
            {expanded ? 'Kamroq' : `Barchasi (${rows.length})`}
          </button>
        ) : null
      }
    >
      <p className="px-4 pb-2 text-[13px] text-slate-500">
        {thisMonth.length > 0 ? `Shu oy: ${thisMonth.length} ta · ${money(total)}` : "Shu oy jarima yo'q"}
      </p>
      {shown.map((r) => (
        <PenaltyRow key={r.id} row={r} />
      ))}
    </Card>
  )
}

function PenaltyRow({ row }) {
  // The photo is optional and the file behind it may be gone — a broken image reads as a fault.
  const [imageFailed, setImageFailed] = useState(false)
  const meta = [`${monthTitle(row.periodYear, row.periodMonth - 1)} uchun`, fullDate(row.createdAt), row.createdByName]

  return (
    <div className="flex items-start gap-3 border-t border-slate-100 px-4 py-3">
      <div
        className={
          'flex h-11 w-11 shrink-0 items-center justify-center rounded-xl ' +
          (row.cancelled ? 'bg-slate-100 text-slate-400' : 'bg-red-50 text-red-600')
        }
      >
        <AlertTriangle className="h-5 w-5" />
      </div>
      <div className="min-w-0 flex-1">
        <div className="flex items-start justify-between gap-2">
          <p className="break-words text-[15px] font-semibold leading-snug">{row.reasonName}</p>
          <p
            className={
              'shrink-0 text-[15px] font-semibold tabular-nums ' +
              (row.cancelled ? 'text-slate-400 line-through' : 'text-red-600')
            }
          >
            −{money(row.amount)}
          </p>
        </div>
        <p className="mt-0.5 text-[13px] text-slate-500">{meta.filter(Boolean).join(' · ')}</p>
        {row.comment && (
          <p className="mt-1 whitespace-pre-line break-words text-[14px] text-slate-700">{row.comment}</p>
        )}
        {row.imageUrl && !imageFailed && (
          <img
            src={row.imageUrl}
            alt=""
            loading="lazy"
            onError={() => setImageFailed(true)}
            className="mt-2 max-h-48 rounded-xl object-cover"
          />
        )}
        {row.cancelled && (
          <div className="mt-2">
            <Badge tone="success">Bekor qilingan{row.cancelReason ? `: ${row.cancelReason}` : ''}</Badge>
          </div>
        )}
      </div>
    </div>
  )
}
