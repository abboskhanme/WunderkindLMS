import { useCallback, useEffect, useState } from 'react'
import { ChevronDown, History } from 'lucide-react'
import type { AuditAction, AuditLog } from '@/types'
import { getStudentActivity } from '@/api/services/studentProfile'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDate } from '@/lib/utils'
import { ProfileEmpty, ProfileError, ProfileSection } from './ProfileUi'

/**
 * Kartochkaning "Faoliyat tarixi" tab'i — docs/modules/students-parity.md
 * §2.3 (S-12).
 *
 * Shu bolaga tegishli audit yozuvlari: kim, qachon, nima o'zgardi (maydon
 * kesimida "eski → yangi").
 *
 * PUL SERVERDA KESILADI. Moliya huquqi bo'lmagan xodimga moliyaviy yozuvlar
 * umuman kelmaydi, qolganlarining `before`/`after` idan esa summa maydonlari
 * olib tashlangan bo'ladi — bu yerda filtr yo'q, chunki brauzerdagi filtr
 * himoya emas.
 */

const actionConfig: Record<AuditAction, { label: string; cls: string }> = {
  create: { label: "Qo'shildi", cls: 'bg-emerald-50 text-emerald-700' },
  update: { label: 'Tahrirlandi', cls: 'bg-amber-50 text-amber-700' },
  delete: { label: "O'chirildi", cls: 'bg-red-50 text-red-700' },
}

/** "yyyy-MM-ddTHH:mm:ss" -> "21.05.2026 09:57" */
function formatDateTime(ts: string): string {
  const [d, t] = ts.split('T')
  return `${formatDate(d)}${t ? ` ${t.slice(0, 5)}` : ''}`
}

function parse(json?: string): Record<string, unknown> | null {
  if (!json) return null
  try {
    const value: unknown = JSON.parse(json)
    return value && typeof value === 'object' ? (value as Record<string, unknown>) : null
  } catch {
    return null
  }
}

function show(value: unknown): string {
  if (value === null || value === undefined || value === '') return '—'
  if (typeof value === 'boolean') return value ? 'Ha' : "Yo'q"
  if (typeof value === 'object') return JSON.stringify(value)
  return String(value)
}

/**
 * `before`/`after` ni maydon kesimida ko'rsatadi. Nomi tarjima qilinmaydi:
 * bu yerda o'nlab entity turi bor va yarim tarjima qilingan ro'yxat
 * chalg'itadi — maydon nomi serverdagi nomning o'zi bo'lib qoladi.
 */
function Detail({ before, after }: { before?: string; after?: string }) {
  const b = parse(before)
  const a = parse(after)
  const keys = [...new Set([...Object.keys(b ?? {}), ...Object.keys(a ?? {})])]
  if (keys.length === 0) return null

  return (
    <dl className="mt-2 space-y-1 rounded-lg bg-slate-50 px-3 py-2 text-xs">
      {keys.map((k) => {
        const ov = b?.[k]
        const nv = a?.[k]
        const changed = b && a && JSON.stringify(ov) !== JSON.stringify(nv)
        return (
          <div key={k} className="flex justify-between gap-3">
            <dt className="text-slate-400">{k}</dt>
            <dd className="text-right text-slate-600">
              {b && a ? (
                changed ? (
                  <>
                    <span className="text-slate-400 line-through">{show(ov)}</span>
                    {' → '}
                    <span className="font-medium text-slate-700">{show(nv)}</span>
                  </>
                ) : (
                  show(nv)
                )
              ) : (
                show(a ? nv : ov)
              )}
            </dd>
          </div>
        )
      })}
    </dl>
  )
}

export function ActivityTab({ studentId }: { studentId: string }) {
  const [rows, setRows] = useState<AuditLog[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [openId, setOpenId] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    getStudentActivity(studentId)
      .then(setRows)
      .catch((e) =>
        setError(
          (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
            "Faoliyat tarixini olib bo'lmadi — ruxsatingiz yetmasligi mumkin",
        ),
      )
      .finally(() => setLoading(false))
  }, [studentId])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- tab ochilganda tarix yuklanadi (maqsadli, loyihadagi mavjud naqsh)
    load()
  }, [load])

  return (
    <ProfileSection title="Faoliyat tarixi" icon={History}>
      {error ? (
        <ProfileError message={error} />
      ) : loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : rows.length === 0 ? (
        <ProfileEmpty>O'zgarishlar tarixi yo'q</ProfileEmpty>
      ) : (
        <ul className="space-y-2">
          {rows.map((log) => {
            const cfg = actionConfig[log.action]
            const hasDetail = !!(log.before || log.after)
            const open = openId === log.id
            return (
              <li key={log.id} className="rounded-lg border border-slate-100 px-3 py-2">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      {cfg && (
                        <span className={cn('rounded-md px-2 py-0.5 text-xs font-medium', cfg.cls)}>
                          {cfg.label}
                        </span>
                      )}
                      <span className="text-sm text-slate-700">{log.summary}</span>
                    </div>
                    <p className="mt-0.5 text-xs text-slate-400">
                      {formatDateTime(log.timestamp)} · {log.actorName || 'Tizim'}
                    </p>
                  </div>
                  {hasDetail && (
                    <button
                      type="button"
                      onClick={() => setOpenId(open ? null : log.id)}
                      className="shrink-0 rounded-md p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                      title="Tafsilot"
                    >
                      <ChevronDown className={cn('h-4 w-4 transition-transform', open && 'rotate-180')} />
                    </button>
                  )}
                </div>
                {open && hasDetail && <Detail before={log.before} after={log.after} />}
              </li>
            )
          })}
        </ul>
      )}
    </ProfileSection>
  )
}
