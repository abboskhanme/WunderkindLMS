/**
 * XULQ-ATVOR — the child's behaviour in the family's cabinet (client, 2026-10-15; docs/modules/behaviour.md).
 *
 * The top line is the score: every pupil starts at 100, positive incidents add, negative ones and
 * attendance reasons with points take away — the same number the school sees. Below — the latest
 * entries: type, reason, points, date. `Barchasi` shows the rest of what the server returned.
 *
 * It loads itself (`/api/student/discipline`), so a failure never takes the home screen down, and it
 * does NOT hide when empty (client, 2026-10-07: profiles look the same for everyone).
 */
import { useState } from 'react'
import { ShieldCheck } from 'lucide-react'
import { Badge, Card, EmptyState } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { dayMonth } from '../../lib/format'
import { haptic } from '../../lib/telegram'
import { getDiscipline } from '../../lib/parentApi'

/** Rows shown before `Barchasi` is tapped. */
const PREVIEW = 5

function signed(p) {
  if (p > 0) return `+${p}`
  if (p < 0) return `−${Math.abs(p)}`
  return '0'
}

export function BehaviourCard({ childId }) {
  const q = useAsync(() => getDiscipline(childId), [childId])
  const [expanded, setExpanded] = useState(false)

  const data = q.data
  const items = data?.items ?? []

  if (q.error || !data) {
    return (
      <Card title="Xulq-atvor">
        {q.error ? (
          <p className="px-4 pb-4 pt-1 text-[13px] text-slate-500">Yuklab bo'lmadi. Ilovani qayta ochib ko'ring.</p>
        ) : (
          <EmptyState icon={<ShieldCheck className="h-8 w-8" />} title="Yuklanmoqda…" />
        )}
      </Card>
    )
  }

  const shown = expanded ? items : items.slice(0, PREVIEW)
  const tone = data.remaining < 100 ? 'text-red-600' : data.remaining > 100 ? 'text-emerald-600' : 'text-brand-ink'

  return (
    <Card
      title="Xulq-atvor"
      action={
        items.length > PREVIEW ? (
          <button
            type="button"
            onClick={() => {
              haptic('light')
              setExpanded((v) => !v)
            }}
            className="shrink-0 rounded-full bg-brand/25 px-3 py-1 text-[13px] font-semibold text-brand-ink"
          >
            {expanded ? 'Kamroq' : `Barchasi (${items.length})`}
          </button>
        ) : null
      }
    >
      <div className="flex items-center gap-3 px-4 pb-3 pt-1">
        <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-brand/25 text-brand-ink">
          <ShieldCheck className="h-6 w-6" />
        </div>
        <div className="min-w-0 flex-1">
          <p className={'text-[24px] font-extrabold leading-tight ' + tone}>{data.remaining} ball</p>
          <p className="text-[13px] text-slate-500">
            100 dan boshlanadi · <span className="text-emerald-600">+{data.plus}</span> ·{' '}
            <span className="text-red-600">−{data.minus}</span>
          </p>
        </div>
      </div>

      {items.length === 0 ? (
        <p className="border-t border-slate-100 px-4 py-3 text-[13px] text-slate-400">
          Hozircha yozuv yo'q — o'qituvchi yoki maktab yozsa, shu yerda ko'rinadi.
        </p>
      ) : (
        shown.map((i) => (
          <div key={`${i.source}-${i.id}`} className="flex items-start gap-3 border-t border-slate-100 px-4 py-3">
            <span
              className={
                'mt-0.5 w-12 shrink-0 rounded-lg py-1 text-center text-[14px] font-bold ' +
                (i.points < 0 ? 'bg-red-50 text-red-600' : 'bg-emerald-50 text-emerald-700')
              }
            >
              {signed(i.points)}
            </span>
            <div className="min-w-0 flex-1">
              <p className="break-words text-[15px] font-semibold leading-snug">{i.reasonName}</p>
              <p className="mt-0.5 text-[13px] text-slate-500">
                {[dayMonth(i.createdAt), i.source === 'attendance' ? 'davomat' : null, i.source === 'manual' ? i.note : null]
                  .filter(Boolean)
                  .join(' · ')}
              </p>
            </div>
            <Badge tone={i.points < 0 ? 'danger' : 'success'}>{i.points < 0 ? 'Salbiy' : 'Ijobiy'}</Badge>
          </div>
        ))
      )}
    </Card>
  )
}
