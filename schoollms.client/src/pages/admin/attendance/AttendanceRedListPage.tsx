/**
 * Qizil ro'yxat — ketma-ket va sababsiz dars qoldirayotgan o'quvchilar va ota-ona bilan aloqa navbati
 * (mijoz, 2026-10-15; docs/modules/attendance-red-list.md).
 *
 * Tizim har kuni tekshiradi va mezonga tushgan o'quvchiga holat ochadi ("Aloqa kerak"). Sinf rahbari
 * Mini App'da, rahbariyat shu yerda amal yozadi: Qayta qo'ng'iroq · Bog'lanib bo'lmadi · Hal bo'ldi.
 * Qizil bilan belgilangan qatorlar — rahbariyat e'tiboriga: bog'lanib bo'lmagan, qayta qo'ng'iroq kuni
 * o'tgan yoki bir necha kundan beri hech kim amal yozmagan.
 */
import { useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { AlertTriangle, ChevronRight, RefreshCw } from 'lucide-react'
import type { AxiosError } from 'axios'
import {
  CASE_STATUSES,
  caseReason,
  getRedList,
  getRedListSettings,
  overdueText,
  scanRedList,
  type AttendanceCaseList,
  type CaseTab,
  type RedListSettings,
} from '@/api/services/attendanceRedList'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Toast } from '@/components/ui/Toast'
import { cn, formatDate } from '@/lib/utils'
import { RedListCaseDrawer, StatusBadge } from './RedListCaseDrawer'
import { RedListSettingsCard } from './RedListSettingsCard'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

const TABS: { key: CaseTab; label: string }[] = [
  { key: 'open', label: 'Barcha ochiq' },
  ...CASE_STATUSES.map((s) => ({ key: s.key as CaseTab, label: s.label })),
]

function errorText(err: unknown, fallback: string): string {
  return (err as AxiosError<{ message?: string }>)?.response?.data?.message ?? fallback
}

function fmtDateTime(iso: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('uz-UZ', {
    timeZone: 'Asia/Tashkent', day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit',
  })
}

export function AttendanceRedListPage() {
  const [params, setParams] = useSearchParams()
  const [tab, setTab] = useState<CaseTab>('open')
  const [className, setClassName] = useState('')
  const [teacherId, setTeacherId] = useState('')
  const [overdueOnly, setOverdueOnly] = useState(false)
  const [data, setData] = useState<AttendanceCaseList | null>(null)
  const [settings, setSettings] = useState<RedListSettings | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [reload, setReload] = useState(0)
  const [scanning, setScanning] = useState(false)
  const [toast, setToast] = useState<{ text: string; tone: 'success' | 'error' } | null>(null)
  const openCase = params.get('case')

  useEffect(() => {
    getRedListSettings().then(setSettings).catch(() => setSettings(null))
  }, [])

  useEffect(() => {
    let active = true
    getRedList({ status: tab, className: className || undefined, teacherId: teacherId || undefined, overdue: overdueOnly })
      .then((d) => {
        if (!active) return
        setData(d)
        setError(null)
      })
      .catch((e) => active && setError(errorText(e, "Ro'yxatni yuklab bo'lmadi.")))
    return () => {
      active = false
    }
  }, [tab, className, teacherId, overdueOnly, reload])

  const refresh = useCallback(() => setReload((n) => n + 1), [])
  const closeToast = useCallback(() => setToast(null), [])
  const pick = (id: string | null) => {
    const next = new URLSearchParams(params)
    if (id) next.set('case', id)
    else next.delete('case')
    setParams(next, { replace: true })
  }

  const scan = async () => {
    setScanning(true)
    try {
      const r = await scanRedList()
      setToast({
        text: r.opened > 0 ? `${r.opened} ta yangi holat ochildi.` : "Yangi holat yo'q — ro'yxat yangilandi.",
        tone: 'success',
      })
      refresh()
      getRedListSettings().then(setSettings).catch(() => undefined)
    } catch (e) {
      setToast({ text: errorText(e, "Tekshirib bo'lmadi."), tone: 'error' })
    } finally {
      setScanning(false)
    }
  }

  const windowDays = settings?.windowDays ?? 14
  const counts = data?.counts ?? {}

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Qizil ro'yxat</h1>
          <p className="text-sm text-slate-400">
            Ketma-ket va sababsiz dars qoldirayotgan o'quvchilar — ota-ona bilan bog'lanish navbati
          </p>
        </div>
        <div className="flex flex-col items-end gap-1">
          <Button variant="secondary" onClick={scan} disabled={scanning}>
            <RefreshCw className={cn('h-4 w-4', scanning && 'animate-spin')} />
            {scanning ? 'Tekshirilmoqda...' : 'Hozir tekshirish'}
          </Button>
          {settings?.lastScanAt && (
            <span className="text-xs text-slate-400">Oxirgi tekshiruv: {fmtDateTime(settings.lastScanAt)}</span>
          )}
        </div>
      </div>

      <Card className="p-0">
        <div className="flex flex-wrap items-center gap-3 px-5 pt-5">
          <div className="flex flex-wrap gap-1 rounded-xl bg-slate-100 p-1">
            {TABS.map((t) => {
              const count = t.key === 'open' ? counts.open : counts[t.key]
              return (
                <button
                  key={t.key}
                  type="button"
                  onClick={() => setTab(t.key)}
                  className={cn(
                    'rounded-lg px-3 py-1.5 text-xs font-medium transition-colors',
                    tab === t.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500',
                  )}
                >
                  {t.label}
                  {t.key === 'hal_boldi' && data ? ` (${data.closedDays} kun)` : ''}
                  <span className="ml-1 text-slate-400">{count ?? 0}</span>
                </button>
              )
            })}
          </div>
          <div className="ml-auto flex flex-wrap items-center gap-2">
            <select aria-label="Sinf" value={className} onChange={(e) => setClassName(e.target.value)} className={control}>
              <option value="">Barcha sinflar</option>
              {(data?.classes ?? []).map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
            <select aria-label="Sinf rahbari" value={teacherId} onChange={(e) => setTeacherId(e.target.value)} className={control}>
              <option value="">Barcha sinf rahbarlari</option>
              {(data?.teachers ?? []).map((t) => (
                <option key={t.id} value={t.id}>
                  {t.fullName}
                </option>
              ))}
            </select>
            <button
              type="button"
              onClick={() => setOverdueOnly((v) => !v)}
              className={cn(
                'inline-flex items-center gap-1.5 rounded-lg border px-3 py-2 text-sm font-medium transition-colors',
                overdueOnly ? 'border-red-200 bg-red-50 text-red-700' : 'border-slate-200 bg-white text-slate-600',
              )}
            >
              <AlertTriangle className="h-4 w-4" />
              Muddati o'tgan
              <span className={overdueOnly ? 'text-red-500' : 'text-slate-400'}>{counts.overdue ?? 0}</span>
            </button>
          </div>
        </div>

        {error ? (
          <p className="px-5 py-8 text-center text-sm text-red-600">{error}</p>
        ) : !data ? (
          <Loader label="Yuklanmoqda..." />
        ) : data.rows.length === 0 ? (
          <p className="px-5 py-10 text-center text-sm text-slate-400">
            {tab === 'hal_boldi' ? "Bu davrda hal bo'lgan holat yo'q." : "Ro'yxat bo'sh — mezonga tushgan o'quvchi yo'q."}
          </p>
        ) : (
          <div className="mt-3 overflow-x-auto">
            <table className="w-full min-w-[880px] table-fixed text-sm">
              <colgroup>
                <col className="w-[24%]" />
                <col className="w-[15%]" />
                <col className="w-[13%]" />
                <col className="w-[14%]" />
                <col className="w-[26%]" />
                <col className="w-[5%]" />
              </colgroup>
              <thead className="whitespace-nowrap bg-slate-50 text-left text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-5 py-2.5 font-medium">O'quvchi</th>
                  <th className="px-3 py-2.5 font-medium">Sabab</th>
                  <th className="px-3 py-2.5 font-medium">Oxirgi kelmagan</th>
                  <th className="px-3 py-2.5 font-medium">Holat</th>
                  <th className="px-3 py-2.5 font-medium">Oxirgi amal</th>
                  <th />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.rows.map((r) => {
                  const late = overdueText(r)
                  return (
                    <tr
                      key={r.id}
                      onClick={() => pick(r.id)}
                      className={cn('group cursor-pointer', r.overdue ? 'bg-red-50/50 hover:bg-red-50' : 'hover:bg-brand-50/40')}
                    >
                      <td className="px-5 py-2.5">
                        <span className="block truncate font-medium text-slate-800" title={r.fullName}>
                          {r.fullName}
                        </span>
                        <span className="block truncate text-xs text-slate-400">
                          {r.className || '—'}
                          {r.teachers.length > 0 && ` · ${r.teachers.map((t) => t.fullName).join(', ')}`}
                        </span>
                      </td>
                      <td className="px-3 py-2.5 font-medium text-red-600">{caseReason(r, windowDays)}</td>
                      <td className="px-3 py-2.5 tabular-nums text-slate-600">
                        {r.lastAbsentOn ? formatDate(r.lastAbsentOn) : '—'}
                        {r.returned && r.lastPresentOn && (
                          <span className="mt-0.5 block text-xs font-medium text-emerald-600">
                            Qaytdi {formatDate(r.lastPresentOn).slice(0, 5)}
                          </span>
                        )}
                      </td>
                      <td className="px-3 py-2.5">
                        <StatusBadge status={r.status} />
                        {r.status === 'qayta_qongiroq' && r.callbackOn && (
                          <span className="mt-0.5 block text-xs text-slate-500">{formatDate(r.callbackOn)}</span>
                        )}
                      </td>
                      <td className="px-3 py-2.5">
                        {late && (
                          <span className="mb-0.5 flex items-center gap-1 text-xs font-medium text-red-600">
                            <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
                            {late}
                          </span>
                        )}
                        {r.lastComment ? (
                          <>
                            <span className="block truncate text-slate-700" title={r.lastComment}>
                              {r.lastComment}
                            </span>
                            <span className="block truncate text-xs text-slate-400">
                              {r.lastActorName} · {fmtDateTime(r.lastActionAt)}
                            </span>
                          </>
                        ) : (
                          !late && <span className="text-xs text-slate-400">Hali amal yo'q</span>
                        )}
                      </td>
                      <td className="pr-4 text-slate-300 group-hover:text-brand-500">
                        <ChevronRight className="h-4 w-4" />
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
        <div className="h-3" />
      </Card>

      {settings && (
        <RedListSettingsCard
          key={settings.updatedAt}
          settings={settings}
          onSaved={(next) => {
            setSettings(next)
            refresh()
          }}
        />
      )}

      <RedListCaseDrawer
        caseId={openCase}
        windowDays={windowDays}
        countExcused={settings?.countExcused ?? false}
        onClose={() => pick(null)}
        onChanged={refresh}
      />
      <Toast message={toast?.text ?? null} tone={toast?.tone} onClose={closeToast} />
    </div>
  )
}
