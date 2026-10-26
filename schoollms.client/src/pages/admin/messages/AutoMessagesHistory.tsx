/**
 * Avtomatik xabarlar — tarix va statistika (2026-10-15, docs/modules/auto-messages.md).
 * Mijoz: "yuborilgan xabarlar holati (Rejalashtirilgan, Tasdiq kutmoqda, Yuborildi, Xatolik,
 * To'xtatilgan) va yetkazilish ro'yxati audit uchun alohida ko'rinib turishi kerak".
 *
 *  · yuqorida — davr bo'yicha holatlar soni (kartochkalar) va qoida × holat jadvali;
 *  · "Yuborishlar" — har qoidaning har kungi (oylik qoidada — har oylik) yuborishi: reja vaqti,
 *    holati, kim tasdiqladi yoki to'xtatdi; bosilsa — shu yuborishning har bir chat bo'yicha ro'yxati;
 *  · "Yetkazish jurnali" — barcha xabarlar (avtomatik va qo'lda: hamshira tugmasi, chekni qayta
 *    yuborish), sahifalab, filtr va Excel bilan.
 */
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { Ban, CheckCircle2, ChevronRight, Download, RefreshCw, Search, UserMinus } from 'lucide-react'
import {
  approveAutoMessageRun,
  cancelAutoMessageRun,
  downloadAutoMessageLog,
  excludeFromAutoMessageRun,
  getAutoMessageLogPage,
  getAutoMessageRuns,
  getAutoMessageRules,
  getAutoMessageStats,
  type AutoMessageKey,
  type AutoMessageLogFilters,
  type AutoMessageLogPage,
  type AutoMessageLogStatus,
  type AutoMessageRule,
  type AutoMessageRun,
  type AutoMessageStats,
} from '@/api/services/autoMessages'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Input, Select } from '@/components/ui/Input'
import { DatePicker } from '@/components/ui/DatePicker'
import { Modal } from '@/components/ui/Modal'
import { Pager } from '@/components/ui/Pager'
import { RichText } from '@/components/ui/RichText'
import { cn } from '@/lib/utils'
import { LOG_STATUS, RUN_STATUS, TITLES, errorText, fmtDateTime, fmtPeriod, isoDay, titleOfKey } from './autoMessageLabels'

const STAT_COLUMNS: AutoMessageLogStatus[] = ['scheduled', 'pending_approval', 'sent', 'failed', 'no_telegram', 'cancelled']

/**
 * Qoidalar ro'yxati (2026-10-26 dan xodim yaratgan shablonlar ham, o'chirilganlari bilan) — tarixdagi
 * har qatorga nom shundan olinadi.
 */
const RulesContext = createContext<AutoMessageRule[] | null>(null)

function useRuleTitle() {
  const rules = useContext(RulesContext)
  return useCallback((key: string) => titleOfKey(key, rules), [rules])
}

export function AutoMessagesHistory() {
  const [from, setFrom] = useState(() => isoDay(6))
  const [to, setTo] = useState(() => isoDay(0))
  const [rule, setRule] = useState('')
  const [reload, setReload] = useState(0)
  const [openRun, setOpenRun] = useState<AutoMessageRun | null>(null)
  const [rules, setRules] = useState<AutoMessageRule[] | null>(null)

  const refresh = useCallback(() => setReload((n) => n + 1), [])
  const titleOf = useCallback((key: string) => titleOfKey(key, rules), [rules])
  const ruleKeys: AutoMessageKey[] = rules?.map((r) => r.key) ?? (Object.keys(TITLES) as AutoMessageKey[])

  useEffect(() => {
    // Nomlarsiz ham ishlaydi — asosiy qoidalar nomi shu faylda.
    getAutoMessageRules().then(setRules).catch(() => setRules(null))
  }, [])

  return (
    <RulesContext.Provider value={rules}>
    <div className="space-y-6">
      <div className="flex flex-wrap items-end gap-2">
        <DatePicker value={from} onChange={setFrom} ariaLabel="Davr boshi" className="w-36" />
        <DatePicker value={to} onChange={setTo} ariaLabel="Davr oxiri" className="w-36" />
        <Select value={rule} onChange={(e) => setRule(e.target.value)} className="w-56">
          <option value="">Barcha xabar turlari</option>
          {ruleKeys.map((k) => (
            <option key={k} value={k}>
              {titleOf(k)}
            </option>
          ))}
        </Select>
        <Button variant="ghost" onClick={refresh} title="Yangilash">
          <RefreshCw className="h-4 w-4" />
        </Button>
      </div>

      <StatsBlock from={from} to={to} rule={rule} reloadKey={reload} />
      <RunsCard from={from} to={to} rule={rule} reloadKey={reload} onOpen={setOpenRun} onChanged={refresh} />
      <LogCard from={from} to={to} rule={rule} reloadKey={reload} />

      {openRun && (
        <RunModal
          run={openRun}
          onClose={() => setOpenRun(null)}
          onChanged={(next) => {
            setOpenRun(next)
            refresh()
          }}
        />
      )}
    </div>
    </RulesContext.Provider>
  )
}

// ---------------------------------------------------------------------------
//  Statistics
// ---------------------------------------------------------------------------

function StatsBlock({ from, to, rule, reloadKey }: { from: string; to: string; rule: string; reloadKey: number }) {
  const titleOf = useRuleTitle()
  const [stats, setStats] = useState<AutoMessageStats | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setError(null)
    getAutoMessageStats(from, to)
      .then(setStats)
      .catch((e) => setError(errorText(e, "Statistikani yuklab bo'lmadi.")))
  }, [from, to, reloadKey])

  const view = useMemo(() => {
    if (!stats) return null
    const cells = rule ? stats.byRule.filter((c) => c.ruleKey === rule) : stats.byRule
    const total = (s: AutoMessageLogStatus) => cells.filter((c) => c.status === s).reduce((n, c) => n + c.count, 0)
    // Server tartibida: avval asosiy qoidalar, keyin shablonlar.
    const keys = [...new Set(stats.byRule.map((c) => c.ruleKey))]
    const byRule = keys.filter((k) => !rule || k === rule)
      .map((k) => ({
        key: k,
        counts: Object.fromEntries(
          STAT_COLUMNS.map((s) => [s, stats.byRule.find((c) => c.ruleKey === k && c.status === s)?.count ?? 0]),
        ) as Record<AutoMessageLogStatus, number>,
      }))
      .filter((r) => STAT_COLUMNS.some((s) => r.counts[s] > 0))
    return { total, byRule }
  }, [stats, rule])

  if (error) return <p className="text-sm text-red-600">{error}</p>
  if (!view) return <Loader label="Yuklanmoqda..." />

  const failed = view.total('failed')
  const noTelegram = view.total('no_telegram')
  const tiles: { label: string; value: number; hint?: string; cls: string }[] = [
    { label: 'Rejalashtirilgan', value: view.total('scheduled'), cls: 'text-sky-700' },
    { label: 'Tasdiq kutmoqda', value: view.total('pending_approval'), cls: 'text-amber-700' },
    { label: 'Yuborildi', value: view.total('sent'), cls: 'text-emerald-700' },
    { label: 'Xatolik', value: failed + noTelegram, hint: `Telegram yo'q: ${noTelegram}`, cls: 'text-red-700' },
    { label: "To'xtatilgan", value: view.total('cancelled'), cls: 'text-slate-600' },
  ]

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        {tiles.map((t) => (
          <Card key={t.label} className="px-4 py-3">
            <p className="text-xs text-slate-400">{t.label}</p>
            <p className={cn('mt-0.5 text-xl font-semibold tabular-nums', t.cls)}>{t.value}</p>
            {t.hint && <p className="text-[11px] text-slate-400">{t.hint}</p>}
          </Card>
        ))}
      </div>
      {view.byRule.length > 0 && (
        <Card className="overflow-x-auto p-0">
          <table className="w-full text-sm">
            <thead className="bg-slate-50 text-left text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-5 py-2 font-medium">Xabar turi</th>
                {STAT_COLUMNS.map((s) => (
                  <th key={s} className="px-3 py-2 text-right font-medium">
                    {LOG_STATUS[s].label}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {view.byRule.map((r) => (
                <tr key={r.key} className="border-t border-slate-100">
                  <td className="px-5 py-2 text-slate-700">{titleOf(r.key)}</td>
                  {STAT_COLUMNS.map((s) => (
                    <td key={s} className={cn('px-3 py-2 text-right tabular-nums', r.counts[s] ? 'text-slate-700' : 'text-slate-300')}>
                      {r.counts[s]}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}
    </div>
  )
}

// ---------------------------------------------------------------------------
//  Runs
// ---------------------------------------------------------------------------

function useRunActions(onDone: (run: AutoMessageRun) => void) {
  const titleOf = useRuleTitle()
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const act = async (run: AutoMessageRun, kind: 'approve' | 'cancel') => {
    const question =
      kind === 'approve'
        ? `"${titleOf(run.ruleKey)}" (${fmtPeriod(run.period)}) — ${run.pending} ta xabar hozir ota-onalarga yuboriladi. Tasdiqlaysizmi?`
        : `"${titleOf(run.ruleKey)}" (${fmtPeriod(run.period)}) to'xtatilsinmi? Hali ketmagan xabarlar yuborilmaydi.`
    if (!window.confirm(question)) return
    setBusy(run.id)
    setError(null)
    try {
      onDone(kind === 'approve' ? await approveAutoMessageRun(run.id) : await cancelAutoMessageRun(run.id))
    } catch (e) {
      setError(errorText(e, kind === 'approve' ? "Tasdiqlab bo'lmadi." : "To'xtatib bo'lmadi."))
    } finally {
      setBusy(null)
    }
  }
  return { busy, error, act }
}

function RunActions({ run, busy, act }: { run: AutoMessageRun; busy: boolean; act: (r: AutoMessageRun, k: 'approve' | 'cancel') => void }) {
  const open = run.status === 'scheduled' || run.status === 'pending_approval' || run.status === 'sending'
  if (!open) return null
  return (
    <div className="flex justify-end gap-1.5" onClick={(e) => e.stopPropagation()}>
      {run.status === 'pending_approval' && (
        <Button className="px-2.5 py-1 text-xs" disabled={busy} onClick={() => act(run, 'approve')}>
          <CheckCircle2 className="h-3.5 w-3.5" /> Tasdiqlash
        </Button>
      )}
      <Button variant="secondary" className="px-2.5 py-1 text-xs" disabled={busy} onClick={() => act(run, 'cancel')}>
        <Ban className="h-3.5 w-3.5" /> To'xtatish
      </Button>
    </div>
  )
}

function RunsCard({
  from,
  to,
  rule,
  reloadKey,
  onOpen,
  onChanged,
}: {
  from: string
  to: string
  rule: string
  reloadKey: number
  onOpen: (run: AutoMessageRun) => void
  onChanged: () => void
}) {
  const titleOf = useRuleTitle()
  const [runs, setRuns] = useState<AutoMessageRun[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const { busy, error: actionError, act } = useRunActions(() => onChanged())

  useEffect(() => {
    setError(null)
    getAutoMessageRuns({ from, to, rule })
      .then(setRuns)
      .catch((e) => setError(errorText(e, "Yuborishlarni yuklab bo'lmadi.")))
  }, [from, to, rule, reloadKey])

  return (
    <Card className="p-0">
      <div className="border-b border-slate-100 px-5 py-4">
        <h3 className="font-semibold text-slate-800">Yuborishlar</h3>
        <p className="text-xs text-slate-400">
          Har qoidaning har kungi (oylik qoidada — oylik) yuborishi. Bosing — kimga yetgani ko'rinadi.
        </p>
      </div>
      {actionError && <p className="px-5 pt-3 text-sm text-red-600">{actionError}</p>}
      {error ? (
        <p className="px-5 py-6 text-sm text-red-600">{error}</p>
      ) : !runs ? (
        <Loader label="Yuklanmoqda..." />
      ) : runs.length === 0 ? (
        <p className="px-5 py-10 text-center text-sm text-slate-400">
          Bu davrda yuborish yo'q. To'lov xabarlari va qo'lda yuborilganlar pastdagi jurnalda.
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-slate-50 text-left text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-5 py-2 font-medium">Xabar turi</th>
                <th className="px-3 py-2 font-medium">Davr</th>
                <th className="px-3 py-2 font-medium">Holat</th>
                <th className="px-3 py-2 font-medium">Reja / yuborilgan</th>
                <th className="px-3 py-2 text-right font-medium">Yetdi</th>
                <th className="px-3 py-2 text-right font-medium">Xatolik</th>
                <th className="px-3 py-2 text-right font-medium">Kutmoqda</th>
                <th className="px-3 py-2 text-right font-medium">To'xt.</th>
                <th className="px-3 py-2 font-medium">Kim</th>
                <th className="px-5 py-2" />
              </tr>
            </thead>
            <tbody>
              {runs.map((r) => (
                <tr
                  key={r.id}
                  onClick={() => onOpen(r)}
                  className="cursor-pointer border-t border-slate-100 align-top hover:bg-slate-50/60"
                >
                  <td className="px-5 py-2">
                    <span className="font-medium text-slate-800">{titleOf(r.ruleKey)}</span>
                    {r.note && <p className="mt-0.5 max-w-xs text-xs text-amber-700">{r.note}</p>}
                  </td>
                  <td className="whitespace-nowrap px-3 py-2 text-slate-600">{fmtPeriod(r.period)}</td>
                  <td className="px-3 py-2">
                    <span className={cn('whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-medium', RUN_STATUS[r.status].cls)}>
                      {RUN_STATUS[r.status].label}
                    </span>
                  </td>
                  <td className="whitespace-nowrap px-3 py-2 text-xs text-slate-500">
                    {fmtDateTime(r.plannedAt)}
                    {r.finishedAt && <span className="block text-slate-400">→ {fmtDateTime(r.finishedAt)}</span>}
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums text-emerald-700">{r.sent}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-red-700">
                    {r.failed + r.noTelegram}
                    {r.noTelegram > 0 && <span className="block text-[11px] text-slate-400">TG yo'q {r.noTelegram}</span>}
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums text-amber-700">{r.pending}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-slate-500">{r.cancelled}</td>
                  <td className="px-3 py-2 text-xs text-slate-500">
                    {r.approvedByName && <span className="block">Tasdiqladi: {r.approvedByName}</span>}
                    {r.cancelledByName && <span className="block">To'xtatdi: {r.cancelledByName}</span>}
                  </td>
                  <td className="whitespace-nowrap px-5 py-2">
                    <div className="flex items-center justify-end gap-2">
                      <RunActions run={r} busy={busy === r.id} act={(x, k) => void act(x, k)} />
                      <ChevronRight className="h-4 w-4 text-slate-300" />
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  )
}

function RunModal({
  run,
  onClose,
  onChanged,
}: {
  run: AutoMessageRun
  onClose: () => void
  onChanged: (run: AutoMessageRun) => void
}) {
  const titleOf = useRuleTitle()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [data, setData] = useState<AutoMessageLogPage | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [excluding, setExcluding] = useState<string | null>(null)
  const { busy, error: actionError, act } = useRunActions(onChanged)

  useEffect(() => {
    setError(null)
    getAutoMessageLogPage({ runId: run.id }, page, pageSize)
      .then(setData)
      .catch((e) => setError(errorText(e, "Ro'yxatni yuklab bo'lmadi.")))
  }, [run, page, pageSize])

  const exclude = async (studentId: string, name: string) => {
    if (!window.confirm(`${name} bu yuborishdan chiqarilsinmi? Unga xabar ketmaydi.`)) return
    setExcluding(studentId)
    try {
      onChanged(await excludeFromAutoMessageRun(run.id, studentId))
    } catch (e) {
      setError(errorText(e, "Chiqarib bo'lmadi."))
    } finally {
      setExcluding(null)
    }
  }

  return (
    <Modal open onClose={onClose} size="xl" title={`${titleOf(run.ruleKey)} — ${fmtPeriod(run.period)}`}>
      <div className="space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2 text-sm text-slate-600">
            <span className={cn('rounded-full px-2 py-0.5 text-xs font-medium', RUN_STATUS[run.status].cls)}>
              {RUN_STATUS[run.status].label}
            </span>
            <span>Reja: {fmtDateTime(run.plannedAt)}</span>
            {run.approvedByName && <span>· Tasdiqladi: {run.approvedByName} ({fmtDateTime(run.approvedAt)})</span>}
            {run.cancelledByName && <span>· To'xtatdi: {run.cancelledByName} ({fmtDateTime(run.cancelledAt)})</span>}
          </div>
          <RunActions run={run} busy={busy === run.id} act={(x, k) => void act(x, k)} />
        </div>
        {run.note && <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">{run.note}</p>}
        {(run.imageUrls?.length ?? 0) > 0 && (
          <div className="flex flex-wrap items-center gap-1.5">
            <span className="text-xs text-slate-500">Rasmlar:</span>
            {run.imageUrls!.map((url, i) => (
              <a key={`${url}-${i}`} href={url} target="_blank" rel="noreferrer">
                <img src={url} alt="" className="h-12 w-12 rounded-md object-cover" />
              </a>
            ))}
          </div>
        )}
        {(actionError || error) && <p className="text-sm text-red-600">{actionError ?? error}</p>}
        {!data ? (
          <Loader label="Yuklanmoqda..." />
        ) : data.rows.length === 0 ? (
          <p className="py-8 text-center text-sm text-slate-400">Bu yuborishda hali xabar tayyorlanmagan.</p>
        ) : (
          <div className="overflow-hidden rounded-xl border border-slate-100">
            <DeliveryTable
              rows={data.rows}
              action={
                run.status === 'pending_approval'
                  ? (r) =>
                      r.status === 'pending_approval' && (
                        <button
                          type="button"
                          disabled={excluding === r.studentId}
                          onClick={(e) => {
                            e.stopPropagation()
                            void exclude(r.studentId, r.fullName)
                          }}
                          className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs text-slate-500 hover:bg-red-50 hover:text-red-700"
                        >
                          <UserMinus className="h-3.5 w-3.5" /> Chiqarish
                        </button>
                      )
                  : undefined
              }
            />
            <Pager
              total={data.total}
              page={page}
              lastPage={Math.max(1, Math.ceil(data.total / pageSize))}
              pageSize={pageSize}
              onPage={setPage}
              onPageSize={(n) => {
                setPageSize(n)
                setPage(1)
              }}
            />
          </div>
        )}
      </div>
    </Modal>
  )
}

// ---------------------------------------------------------------------------
//  Delivery log
// ---------------------------------------------------------------------------

function DeliveryTable({
  rows,
  action,
}: {
  rows: AutoMessageLogPage['rows']
  action?: (row: AutoMessageLogPage['rows'][number]) => ReactNode
}) {
  const titleOf = useRuleTitle()
  const [open, setOpen] = useState<string | null>(null)
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="bg-slate-50 text-left text-xs uppercase tracking-wide text-slate-400">
          <tr>
            <th className="px-5 py-2 font-medium">Vaqt</th>
            <th className="px-3 py-2 font-medium">Xabar turi</th>
            <th className="px-3 py-2 font-medium">O'quvchi</th>
            <th className="px-3 py-2 font-medium">Ota-ona</th>
            <th className="px-3 py-2 font-medium">Holat</th>
            <th className="px-3 py-2 font-medium">Kim</th>
            {action && <th className="px-3 py-2" />}
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr
              key={r.id}
              onClick={() => setOpen(open === r.id ? null : r.id)}
              className="cursor-pointer border-t border-slate-100 align-top hover:bg-slate-50/60"
            >
              <td className="whitespace-nowrap px-5 py-2 text-slate-500">{fmtDateTime(r.at)}</td>
              <td className="px-3 py-2 text-slate-600">
                {titleOf(r.ruleKey)}
                {r.refId && <span className="block text-xs text-slate-400">Chek № {r.refId}</span>}
              </td>
              <td className="px-3 py-2">
                <span className="font-medium text-slate-800">{r.fullName}</span>
                <span className="text-slate-400"> · {r.className}</span>
                {open === r.id && r.text && (
                  <RichText html={r.text} className="mt-2 rounded-lg bg-slate-50 px-3 py-2 text-slate-700" />
                )}
              </td>
              <td className="px-3 py-2 text-slate-600">{r.parentName ?? (r.chatId ? 'Ota-ona' : '—')}</td>
              <td className="px-3 py-2">
                <span className={cn('whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-medium', LOG_STATUS[r.status].cls)}>
                  {LOG_STATUS[r.status].label}
                </span>
                {r.attempts > 1 && <span className="block text-[11px] text-slate-400">{r.attempts} urinish</span>}
                {r.error && r.status !== 'sent' && <span className="block max-w-[14rem] text-[11px] text-slate-400">{r.error}</span>}
              </td>
              <td className="px-3 py-2 text-xs text-slate-500">
                {r.source === 'manual' ? `Qo'lda${r.sentByName ? ` · ${r.sentByName}` : ''}` : 'Avtomatik'}
              </td>
              {action && <td className="px-3 py-2 text-right">{action(r)}</td>}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function LogCard({ from, to, rule, reloadKey }: { from: string; to: string; rule: string; reloadKey: number }) {
  const [status, setStatus] = useState('')
  const [source, setSource] = useState('')
  const [search, setSearch] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [data, setData] = useState<AutoMessageLogPage | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [exporting, setExporting] = useState(false)

  // The pupil search waits for a pause in typing.
  useEffect(() => {
    const t = setTimeout(() => setQ(search), 350)
    return () => clearTimeout(t)
  }, [search])

  const filters: AutoMessageLogFilters = useMemo(
    () => ({ rule, from, to, status, source, q }),
    [rule, from, to, status, source, q],
  )

  useEffect(() => setPage(1), [filters])

  useEffect(() => {
    setError(null)
    getAutoMessageLogPage(filters, page, pageSize)
      .then(setData)
      .catch((e) => setError(errorText(e, "Jurnalni yuklab bo'lmadi.")))
  }, [filters, page, pageSize, reloadKey])

  const exportXlsx = async () => {
    setExporting(true)
    try {
      await downloadAutoMessageLog(filters)
    } catch (e) {
      setError(errorText(e, "Excel faylni olib bo'lmadi."))
    } finally {
      setExporting(false)
    }
  }

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-end justify-between gap-3 border-b border-slate-100 px-5 py-4">
        <div>
          <h3 className="font-semibold text-slate-800">Yetkazish jurnali</h3>
          <p className="text-xs text-slate-400">
            Har ota-ona chati alohida qator — avtomatik va qo'lda yuborilganlar (hamshira tugmasi, chekni qayta yuborish).
          </p>
        </div>
        <div className="flex flex-wrap items-end gap-2">
          <div className="relative">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <Input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="O'quvchi"
              className="w-44 pl-8"
            />
          </div>
          <Select value={status} onChange={(e) => setStatus(e.target.value)} className="w-44">
            <option value="">Barcha holat</option>
            {STAT_COLUMNS.map((k) => (
              <option key={k} value={k}>
                {LOG_STATUS[k].label}
              </option>
            ))}
          </Select>
          <Select value={source} onChange={(e) => setSource(e.target.value)} className="w-36">
            <option value="">Hammasi</option>
            <option value="auto">Avtomatik</option>
            <option value="manual">Qo'lda</option>
          </Select>
          <Button variant="secondary" onClick={() => void exportXlsx()} disabled={exporting}>
            <Download className="h-4 w-4" /> {exporting ? 'Tayyorlanmoqda…' : 'Excel'}
          </Button>
        </div>
      </div>
      {error ? (
        <p className="px-5 py-6 text-sm text-red-600">{error}</p>
      ) : !data ? (
        <Loader label="Yuklanmoqda..." />
      ) : data.rows.length === 0 ? (
        <p className="px-5 py-10 text-center text-sm text-slate-400">Bu davrda xabar yo'q.</p>
      ) : (
        <>
          <DeliveryTable rows={data.rows} />
          <Pager
            total={data.total}
            page={page}
            lastPage={Math.max(1, Math.ceil(data.total / pageSize))}
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
  )
}
