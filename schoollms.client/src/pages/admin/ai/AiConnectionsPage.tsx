import { useCallback, useEffect, useState } from 'react'
import { Bot, Check, Copy, ShieldCheck, ShieldOff } from 'lucide-react'
import {
  getMcpAudit,
  getMcpConnections,
  getMcpStatus,
  revokeMcpConnection,
  type McpAuditPage,
  type McpConnection,
  type McpStatus,
} from '@/api/services/mcp'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Pager } from '@/components/ui/Pager'
import { cn } from '@/lib/utils'

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

/** "26.09.2026 14:05" — mahalliy vaqt. */
function formatDateTime(iso: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getDate())}.${p(d.getMonth() + 1)}.${d.getFullYear()} ${p(d.getHours())}:${p(d.getMinutes())}`
}

const ROLE_LABEL: Record<string, string> = {
  superadmin: 'Direktor',
  admin: 'Administrator',
  staff: 'Xodim',
}

const OUTCOME: Record<string, { label: string; className: string }> = {
  ok: { label: 'Bajarildi', className: 'bg-emerald-50 text-emerald-700' },
  denied: { label: 'Ruxsat yo\'q', className: 'bg-amber-50 text-amber-700' },
  error: { label: 'Xato', className: 'bg-red-50 text-red-700' },
}

/**
 * Boshqaruv → AI ulanishlar (faqat superadmin). docs/modules/mcp-readonly.md.
 *
 * Rahbariyat ChatGPT / Claude kabi AI yordamchini maktab tizimiga FAQAT O'QISH uchun
 * ulaganda shu yerda ko'rinadi: kim, qaysi ilova, oxirgi marta qachon, nechta so'rov.
 * "Bekor qilish" ulanishning barcha tokenlarini darhol o'chiradi. Pastda — har bir AI
 * so'rovining jurnali.
 */
export function AiConnectionsPage() {
  const [status, setStatus] = useState<McpStatus | null>(null)
  const [connections, setConnections] = useState<McpConnection[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const [audit, setAudit] = useState<McpAuditPage | null>(null)
  // Which filter the shown audit page belongs to — loading = it differs from the current one.
  const [auditKey, setAuditKey] = useState<string | null>(null)
  const [userFilter, setUserFilter] = useState<{ id: string; name: string } | null>(null)
  const [outcome, setOutcome] = useState<string>('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)

  const loadConnections = useCallback(() => {
    Promise.all([getMcpStatus(), getMcpConnections()])
      .then(([s, c]) => {
        setStatus(s)
        setConnections(c)
      })
      .catch((e: unknown) => setError(errorText(e, "Ma'lumotni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  useEffect(loadConnections, [loadConnections])

  const currentKey = JSON.stringify([userFilter?.id, outcome, page, pageSize])
  const auditLoading = auditKey !== currentKey

  useEffect(() => {
    getMcpAudit({ userId: userFilter?.id, outcome: outcome || undefined, page, pageSize })
      .then(setAudit)
      .catch(() => setAudit(null))
      .finally(() => setAuditKey(JSON.stringify([userFilter?.id, outcome, page, pageSize])))
  }, [userFilter, outcome, page, pageSize])

  const revoke = (c: McpConnection) => {
    if (!confirm(`«${c.clientName}» (${c.userName}) ulanishini bekor qilasizmi? AI ilovasi darhol uzib qo'yiladi.`)) return
    revokeMcpConnection(c.id)
      .then(loadConnections)
      .catch((e: unknown) => alert(errorText(e, "Bekor qilib bo'lmadi")))
  }

  const copyEndpoint = () => {
    if (!status) return
    void navigator.clipboard?.writeText(status.endpoint).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    })
  }

  const active = connections.filter((c) => c.active)
  const inactive = connections.filter((c) => !c.active)
  const lastPage = audit ? Math.max(1, Math.ceil(audit.total / audit.pageSize)) : 1

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">AI ulanishlar</h1>
        <p className="text-sm text-slate-400">
          ChatGPT, Claude va boshqa AI yordamchilar — faqat o'qish uchun. Ruxsat Boshqaruv → Rollar dagi «AI ulanish»
          belgisi bilan beriladi.
        </p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : error ? (
        <Card>
          <p className="py-12 text-center text-sm text-red-600">{error}</p>
        </Card>
      ) : (
        <>
          <Card className="flex flex-wrap items-center gap-4">
            <div
              className={cn(
                'flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl',
                status?.enabled ? 'bg-emerald-50 text-emerald-600' : 'bg-slate-100 text-slate-400',
              )}
            >
              {status?.enabled ? <ShieldCheck className="h-5 w-5" /> : <ShieldOff className="h-5 w-5" />}
            </div>
            <div className="min-w-0 flex-1">
              <p className="text-sm font-medium text-slate-800">
                {status?.enabled ? 'AI ulanish yoqilgan' : "AI ulanish serverda o'chiq"}
              </p>
              {status?.enabled ? (
                <p className="truncate text-xs text-slate-400">
                  AI ilovasiga qo'shiladigan manzil: <span className="font-mono text-slate-600">{status.endpoint}</span>
                </p>
              ) : (
                <p className="text-xs text-slate-400">
                  Yoqish uchun serverda o'qish-uchun baza ulanishi sozlanishi kerak (docs/MCP.md).
                </p>
              )}
            </div>
            {status?.enabled && (
              <button
                type="button"
                onClick={copyEndpoint}
                className="inline-flex items-center gap-1.5 rounded-xl bg-slate-100 px-3 py-2 text-xs font-medium text-slate-700 transition-colors hover:bg-slate-200"
              >
                {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
                {copied ? 'Nusxalandi' : 'Nusxalash'}
              </button>
            )}
          </Card>

          <Card className="p-0">
            <div className="flex items-center justify-between px-4 py-3">
              <h2 className="text-sm font-semibold text-slate-700">Ulangan ilovalar</h2>
              <span className="text-xs text-slate-400">{active.length} ta faol</span>
            </div>
            {connections.length === 0 ? (
              <div className="flex flex-col items-center gap-2 px-4 pb-10 pt-4 text-center">
                <Bot className="h-8 w-8 text-slate-300" />
                <p className="text-sm text-slate-400">Hali hech kim AI ilovasini ulamagan.</p>
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-4 py-3">Ilova</th>
                      <th className="px-4 py-3">Foydalanuvchi</th>
                      <th className="px-4 py-3">Ulangan</th>
                      <th className="px-4 py-3">Oxirgi foydalanish</th>
                      <th className="px-4 py-3 text-right">So'rovlar</th>
                      <th className="px-4 py-3">Holat</th>
                      <th className="px-4 py-3 text-right">Amallar</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {[...active, ...inactive].map((c) => (
                      <tr key={c.id} className={cn('hover:bg-slate-50/60', !c.active && 'text-slate-400')}>
                        <td className="px-4 py-3">
                          <span className="block font-medium text-slate-800">{c.clientName}</span>
                          <span className="block text-xs text-slate-500">→ {c.redirectHosts.join(', ') || '—'}</span>
                          <span className="block max-w-[220px] truncate font-mono text-[11px] text-slate-400" title={c.clientId}>
                            {c.clientId}
                          </span>
                        </td>
                        <td className="px-4 py-3">
                          <button
                            type="button"
                            onClick={() => {
                              setUserFilter({ id: c.userId, name: c.userName })
                              setPage(1)
                            }}
                            className="text-left hover:text-brand-600"
                            title="Shu foydalanuvchining so'rovlari"
                          >
                            <span className="block text-slate-700">{c.userName}</span>
                            <span className="text-xs text-slate-400">{ROLE_LABEL[c.userRole ?? ''] ?? c.userRole ?? '—'}</span>
                          </button>
                        </td>
                        <td className="whitespace-nowrap px-4 py-3 text-slate-500">{formatDateTime(c.createdAt)}</td>
                        <td className="whitespace-nowrap px-4 py-3 text-slate-500">{formatDateTime(c.lastUsedAt)}</td>
                        <td className="px-4 py-3 text-right tabular-nums text-slate-600">{c.calls}</td>
                        <td className="px-4 py-3">
                          <StatusBadge c={c} />
                        </td>
                        <td className="px-4 py-3 text-right">
                          {c.revokedAt === null && (
                            <button
                              type="button"
                              onClick={() => revoke(c)}
                              className="rounded-lg px-2.5 py-1 text-xs font-medium text-red-600 transition-colors hover:bg-red-50"
                            >
                              Bekor qilish
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        </>
      )}

      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
          <div className="flex min-w-0 items-center gap-2">
            <h2 className="text-sm font-semibold text-slate-700">So'rovlar jurnali</h2>
            {userFilter && (
              <button
                type="button"
                onClick={() => {
                  setUserFilter(null)
                  setPage(1)
                }}
                className="rounded-full bg-slate-100 px-2.5 py-0.5 text-xs text-slate-600 hover:bg-slate-200"
              >
                {userFilter.name} ✕
              </button>
            )}
          </div>
          <div className="inline-flex rounded-lg bg-slate-100 p-0.5">
            {[
              { v: '', l: 'Hammasi' },
              { v: 'ok', l: 'Bajarildi' },
              { v: 'denied', l: "Ruxsat yo'q" },
              { v: 'error', l: 'Xato' },
            ].map((o) => (
              <button
                key={o.v}
                type="button"
                onClick={() => {
                  setOutcome(o.v)
                  setPage(1)
                }}
                className={cn(
                  'rounded-md px-2.5 py-1 text-xs font-medium transition-colors',
                  outcome === o.v ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                )}
              >
                {o.l}
              </button>
            ))}
          </div>
        </div>
        {auditLoading ? (
          <Loader label="Yuklanmoqda..." />
        ) : !audit || audit.rows.length === 0 ? (
          <p className="px-4 pb-10 pt-4 text-center text-sm text-slate-400">Jurnal bo'sh.</p>
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-3">Vaqt</th>
                    <th className="px-4 py-3">Foydalanuvchi</th>
                    <th className="px-4 py-3">Ilova</th>
                    <th className="px-4 py-3">Vosita</th>
                    <th className="px-4 py-3">Parametrlar</th>
                    <th className="px-4 py-3 text-right">Qatorlar</th>
                    <th className="px-4 py-3">Natija</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {audit.rows.map((r) => (
                    <tr key={r.id} className="align-top hover:bg-slate-50/60">
                      <td className="whitespace-nowrap px-4 py-2.5 text-slate-500">{formatDateTime(r.at)}</td>
                      <td className="px-4 py-2.5 text-slate-700">{r.userName}</td>
                      <td className="px-4 py-2.5 text-slate-500">{r.clientName}</td>
                      <td className="whitespace-nowrap px-4 py-2.5 font-mono text-xs text-slate-700">{r.tool}</td>
                      <td className="max-w-[280px] truncate px-4 py-2.5 font-mono text-xs text-slate-400" title={r.arguments}>
                        {r.arguments || '—'}
                      </td>
                      <td className="px-4 py-2.5 text-right tabular-nums text-slate-600">{r.rowCount ?? '—'}</td>
                      <td className="px-4 py-2.5">
                        <span
                          className={cn(
                            'rounded-full px-2 py-0.5 text-xs font-medium',
                            OUTCOME[r.outcome]?.className ?? 'bg-slate-100 text-slate-600',
                          )}
                        >
                          {OUTCOME[r.outcome]?.label ?? r.outcome}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pager
              total={audit.total}
              page={page}
              lastPage={lastPage}
              pageSize={pageSize}
              onPage={setPage}
              onPageSize={(n) => {
                setPageSize(n)
                setPage(1)
              }}
            />
          </>
        )}
      </Card>
    </div>
  )
}

function StatusBadge({ c }: { c: McpConnection }) {
  const [label, className] = c.revokedAt
    ? ['Bekor qilingan', 'bg-slate-100 text-slate-500']
    : !c.userAllowed
      ? ["Ruxsati olingan", 'bg-amber-50 text-amber-700']
      : c.active
        ? ['Faol', 'bg-emerald-50 text-emerald-700']
        : ['Muddati tugagan', 'bg-slate-100 text-slate-500']
  return <span className={cn('whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-medium', className)}>{label}</span>
}
