/**
 * SERTIFIKATLAR VA NATIJALAR — the child's certificates in the family's cabinet (client,
 * 2026-10-07: "o'quvchilarni hozircha necha balli borligi shaxsiy kabinetda ko'rinib turishi").
 *
 * The top line answers "how many points now": the latest result per certificate type (a score,
 * a level like "B2", or both). Below — the documents themselves, newest first.
 *
 * It loads itself, so a failure here never takes the home screen down. It does NOT hide when
 * empty (client, 2026-10-07: profiles must look the same for everyone) — "hali yo'q" instead.
 */
import { useState } from 'react'
import { Award } from 'lucide-react'
import { Badge, Card, EmptyState } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { fullDate } from '../../lib/format'
import { haptic } from '../../lib/telegram'
import { getCertificates } from '../../lib/parentApi'

/** Rows shown before `Barchasi` is tapped. */
const PREVIEW = 3

/** "6.5 · B2" — whatever result the document carries. */
function result(c) {
  return [c.score != null ? String(Number(c.score)) : null, c.level].filter(Boolean).join(' · ')
}

export function CertificatesCard({ childId }) {
  const q = useAsync(() => getCertificates(childId), [childId])
  const [expanded, setExpanded] = useState(false)

  const rows = q.data ?? []
  if (q.error || rows.length === 0) {
    return (
      <Card title="Sertifikatlar va natijalar">
        {q.error ? (
          <p className="px-4 pb-4 pt-1 text-[13px] text-slate-500">Yuklab bo'lmadi. Ilovani qayta ochib ko'ring.</p>
        ) : (
          <EmptyState
            icon={<Award className="h-8 w-8" />}
            title={q.loading ? 'Yuklanmoqda…' : "Hozircha sertifikat yo'q"}
            note={q.loading ? null : "Maktab natija yoki sertifikat kiritsa, shu yerda ko'rinadi."}
          />
        )}
      </Card>
    )
  }

  // Latest result per type — rows come newest first, so the first one of each type wins.
  const latest = []
  const seen = new Set()
  for (const c of rows) {
    if (seen.has(c.typeName) || !result(c)) continue
    seen.add(c.typeName)
    latest.push(c)
  }
  const shown = expanded ? rows : rows.slice(0, PREVIEW)

  return (
    <Card
      title="Sertifikatlar va natijalar"
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
      {latest.length > 0 && (
        <div className="flex flex-wrap gap-2 px-4 pb-3">
          {latest.map((c) => (
            <span
              key={c.typeName}
              className="rounded-xl bg-brand/25 px-3 py-1.5 text-[14px] font-semibold text-brand-ink"
            >
              {c.typeName}: {result(c)}
            </span>
          ))}
        </div>
      )}
      {shown.map((c) => (
        <div key={c.id} className="flex items-start gap-3 border-t border-slate-100 px-4 py-3">
          <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
            <Award className="h-5 w-5" />
          </div>
          <div className="min-w-0 flex-1">
            <div className="flex items-start justify-between gap-2">
              <p className="break-words text-[15px] font-semibold leading-snug">{c.typeName}</p>
              {result(c) && (
                <p className="shrink-0 text-[15px] font-semibold tabular-nums text-brand-ink">{result(c)}</p>
              )}
            </div>
            <p className="mt-0.5 text-[13px] text-slate-500">
              {[c.subjectNames?.length ? c.subjectNames.join(', ') : null, fullDate(c.issuedOn)]
                .filter(Boolean)
                .join(' · ')}
            </p>
            {(c.expiresOn || c.fileUrl) && (
              <div className="mt-1.5 flex flex-wrap items-center gap-2">
                {c.expiresOn &&
                  (c.isExpired ? (
                    <Badge tone="danger">Muddati o'tgan</Badge>
                  ) : (
                    <Badge>{fullDate(c.expiresOn)} gacha</Badge>
                  ))}
                {c.fileUrl && (
                  <a
                    href={c.fileUrl}
                    target="_blank"
                    rel="noreferrer"
                    className="text-[13px] font-semibold text-brand-ink underline"
                  >
                    Hujjatni ochish
                  </a>
                )}
              </div>
            )}
          </div>
        </div>
      ))}
    </Card>
  )
}
