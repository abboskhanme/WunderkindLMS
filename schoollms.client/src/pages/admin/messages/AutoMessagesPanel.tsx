/**
 * Avtomatik xabarlar — ota-onalarga qoida bo'yicha Telegram xabari (docs/modules/auto-messages.md,
 * 2026-10-13): yo'qlik, yangi oylik hisob, qarz eslatmasi, tug'ilgan kun. Har qoida O'CHIQ holda
 * keladi — maktab matnni tasdiqlagach shu yerda yoqiladi. Pastda: nima ketgani (jurnal).
 */
import { useCallback, useEffect, useRef, useState } from 'react'
import { AlertTriangle, CalendarClock, Eye, RotateCcw, Save } from 'lucide-react'
import type { AxiosError } from 'axios'
import {
  getAutoMessageLog,
  getAutoMessageRules,
  previewAutoMessage,
  saveAutoMessageRule,
  type AutoMessageKey,
  type AutoMessageLogRow,
  type AutoMessagePreview,
  type AutoMessageRule,
} from '@/api/services/autoMessages'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Input, Select, Time24Input } from '@/components/ui/Input'
import { DatePicker } from '@/components/ui/DatePicker'
import { FormatToolbar } from '@/components/ui/FormatToolbar'
import { RichText } from '@/components/ui/RichText'
import { Switch } from '@/components/hr-tasks/ui'
import { cn } from '@/lib/utils'

const TITLES: Record<AutoMessageKey, { title: string; hint: string }> = {
  absence: {
    title: "Kelmaganlik xabari",
    hint: "Kun bo'yi kelmagan o'quvchining ota-onasiga — hamshira belgilagan davomat bo'yicha. Tugma bilan yuborilganlarga qayta ketmaydi.",
  },
  invoice: {
    title: 'Yangi oylik hisob',
    hint: "Oy hisob-fakturasi chiqqach — shu oy summasi va jami to'lanmagan. Hisob hali chiqmagan bo'lsa, keyingi 2 kun ichida qayta urinadi.",
  },
  debt: {
    title: "Qarz eslatmasi",
    hint: "Har oy belgilangan kuni — muddati o'tgan qarzi eng kam summadan ko'p bo'lganlarga (\"Qarzdorlar\" hisobotidagi raqam).",
  },
  birthday: {
    title: "Tug'ilgan kun tabrigi",
    hint: "Tug'ilgan kuni o'quvchining ota-onasiga. 29-fevralda tug'ilganlar boshqa yillari 28-fevralda tabriklanadi.",
  },
}

const STATUS: Record<AutoMessageLogRow['status'], { label: string; cls: string }> = {
  sent: { label: 'Yetdi', cls: 'bg-emerald-50 text-emerald-700' },
  failed: { label: 'Yetmadi', cls: 'bg-red-50 text-red-700' },
  no_telegram: { label: 'Telegram yo\'q', cls: 'bg-slate-100 text-slate-500' },
}

function errorText(err: unknown, fallback: string): string {
  return (err as AxiosError<{ message?: string }>)?.response?.data?.message ?? fallback
}

function fmtDateTime(iso: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('uz-UZ', {
    timeZone: 'Asia/Tashkent', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
  })
}

export function AutoMessagesPanel() {
  const [rules, setRules] = useState<AutoMessageRule[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [logReload, setLogReload] = useState(0)

  useEffect(() => {
    getAutoMessageRules()
      .then(setRules)
      .catch((e) => setError(errorText(e, "Qoidalarni yuklab bo'lmadi.")))
  }, [])

  if (error) return <p className="text-sm text-red-600">{error}</p>
  if (!rules) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <p className="flex items-start gap-2 rounded-xl bg-amber-50 px-4 py-3 text-sm text-amber-800">
        <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
        Yoqilgan qoida ota-onalarga o'zi xabar yuboradi. Avval matnni maktab bilan kelishib oling va "Namuna"
        tugmasi bilan kimga ketishini ko'ring.
      </p>
      <div className="grid gap-4 xl:grid-cols-2">
        {rules.map((r) => (
          <RuleCard
            key={r.key}
            rule={r}
            onSaved={(next) => {
              setRules((list) => list?.map((x) => (x.key === next.key ? { ...x, ...next } : x)) ?? null)
              setLogReload((n) => n + 1)
            }}
          />
        ))}
      </div>
      <LogCard reloadKey={logReload} />
    </div>
  )
}

function RuleCard({ rule, onSaved }: { rule: AutoMessageRule; onSaved: (r: AutoMessageRule) => void }) {
  const [enabled, setEnabled] = useState(rule.enabled)
  const [sendTime, setSendTime] = useState(rule.sendTime)
  const [day, setDay] = useState(rule.dayOfMonth)
  const [minAmount, setMinAmount] = useState(String(rule.minAmount))
  const [text, setText] = useState(rule.text)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null)
  const [preview, setPreview] = useState<AutoMessagePreview | null>(null)
  const [previewBusy, setPreviewBusy] = useState(false)
  const textRef = useRef<HTMLTextAreaElement>(null)
  const meta = TITLES[rule.key]

  const dirty =
    enabled !== rule.enabled ||
    sendTime !== rule.sendTime ||
    (rule.monthly && day !== rule.dayOfMonth) ||
    (rule.key === 'debt' && Number(minAmount || 0) !== rule.minAmount) ||
    text !== rule.text

  const save = async (nextEnabled = enabled) => {
    setBusy(true)
    setMsg(null)
    try {
      const saved = await saveAutoMessageRule(rule.key, {
        enabled: nextEnabled,
        sendTime,
        dayOfMonth: day,
        minAmount: Number(minAmount || 0),
        text,
      })
      setEnabled(saved.enabled)
      onSaved(saved)
      setMsg({ ok: true, text: saved.enabled ? 'Saqlandi — qoida yoqilgan.' : "Saqlandi — qoida o'chiq." })
    } catch (e) {
      setEnabled(rule.enabled)
      setMsg({ ok: false, text: errorText(e, "Saqlab bo'lmadi.") })
    } finally {
      setBusy(false)
    }
  }

  const runPreview = async () => {
    setPreviewBusy(true)
    try {
      setPreview(await previewAutoMessage(rule.key, text))
    } catch (e) {
      setMsg({ ok: false, text: errorText(e, "Namunani olib bo'lmadi.") })
    } finally {
      setPreviewBusy(false)
    }
  }

  const insertToken = (token: string) => {
    const el = textRef.current
    if (!el) {
      setText((t) => t + token)
      return
    }
    const start = el.selectionStart ?? text.length
    const end = el.selectionEnd ?? text.length
    setText(text.slice(0, start) + token + text.slice(end))
    requestAnimationFrame(() => {
      el.focus()
      el.setSelectionRange(start + token.length, start + token.length)
    })
  }

  return (
    <Card className="flex flex-col gap-4 p-5">
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0">
          <h3 className="font-semibold text-slate-800">{meta.title}</h3>
          <p className="mt-0.5 text-xs text-slate-400">{meta.hint}</p>
        </div>
        <Switch
          checked={enabled}
          disabled={busy}
          ariaLabel={`${meta.title} — yoqish/o'chirish`}
          onChange={(next) => {
            setEnabled(next)
            void save(next)
          }}
        />
      </div>

      <div className="flex flex-wrap items-end gap-4">
        <div>
          <span className="mb-1 block text-sm font-medium text-slate-600">Vaqti</span>
          <Time24Input value={sendTime} onChange={setSendTime} />
        </div>
        {rule.monthly && (
          <Select
            label="Oyning kuni"
            value={String(day)}
            onChange={(e) => setDay(Number(e.target.value))}
            className="w-24"
          >
            {Array.from({ length: 28 }, (_, i) => i + 1).map((d) => (
              <option key={d} value={d}>
                {d}
              </option>
            ))}
          </Select>
        )}
        {rule.key === 'debt' && (
          <Input
            label="Eng kam qarz, so'm"
            inputMode="numeric"
            value={minAmount}
            onChange={(e) => setMinAmount(e.target.value.replace(/\D/g, ''))}
            className="w-40"
          />
        )}
      </div>

      <div>
        <FormatToolbar textareaRef={textRef} value={text} onChange={setText} className="mb-1.5" />
        <textarea
          ref={textRef}
          value={text}
          onChange={(e) => setText(e.target.value)}
          className="h-32 w-full resize-y rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
        />
        <div className="mt-1.5 flex flex-wrap gap-1.5">
          {rule.tokens.map((t) => (
            <button
              key={t.token}
              type="button"
              title={t.meaning}
              onClick={() => insertToken(t.token)}
              className="rounded-md bg-slate-100 px-2 py-1 text-xs font-medium text-slate-600 transition-colors hover:bg-brand-50 hover:text-brand-700"
            >
              {t.token}
            </button>
          ))}
          {text !== rule.defaultText && (
            <button
              type="button"
              onClick={() => setText(rule.defaultText)}
              className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs text-slate-400 hover:text-slate-600"
            >
              <RotateCcw className="h-3 w-3" /> Asl matn
            </button>
          )}
        </div>
      </div>

      {preview && (
        <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm">
          <p className="mb-1 text-xs text-slate-500">
            Hozir ishga tushsa: <b className="text-slate-700">{preview.pupils}</b> o'quvchi, ota-onasi Telegramda —{' '}
            <b className="text-slate-700">{preview.withTelegram}</b>
            {preview.alreadySent > 0 && <>, allaqachon yuborilgan — {preview.alreadySent}</>}.
          </p>
          {preview.sample ? (
            <>
              <p className="mb-1 text-xs font-medium text-slate-400">Namuna ({preview.sampleFor}):</p>
              <RichText html={preview.sample} className="text-slate-700" />
            </>
          ) : (
            <p className="text-xs text-slate-400">Hozir bu qoidaga mos o'quvchi yo'q.</p>
          )}
        </div>
      )}

      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 pt-3">
        <p className="flex items-center gap-1.5 text-xs text-slate-400">
          <CalendarClock className="h-3.5 w-3.5" />
          {rule.lastRunAt ? (
            <>
              Oxirgi: {fmtDateTime(rule.lastRunAt)} — yetdi {rule.lastSent}
              {rule.lastFailed > 0 && `, yetmadi ${rule.lastFailed}`}
              {rule.lastNoTelegram > 0 && `, Telegram yo'q ${rule.lastNoTelegram}`}
            </>
          ) : (
            'Hali ishga tushmagan'
          )}
        </p>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={() => void runPreview()} disabled={previewBusy}>
            <Eye className="h-4 w-4" /> {previewBusy ? 'Tekshirilmoqda…' : 'Namuna'}
          </Button>
          <Button onClick={() => void save()} disabled={busy || !dirty}>
            <Save className="h-4 w-4" /> Saqlash
          </Button>
        </div>
      </div>
      {msg && <p className={cn('text-sm', msg.ok ? 'text-emerald-700' : 'text-red-600')}>{msg.text}</p>}
    </Card>
  )
}

function today(): string {
  return new Date().toLocaleDateString('en-CA', { timeZone: 'Asia/Tashkent' })
}

function LogCard({ reloadKey }: { reloadKey: number }) {
  const [rule, setRule] = useState('')
  const [status, setStatus] = useState('')
  const [from, setFrom] = useState(() => {
    const d = new Date(Date.now() - 6 * 86_400_000)
    return d.toLocaleDateString('en-CA', { timeZone: 'Asia/Tashkent' })
  })
  const [to, setTo] = useState(today())
  const [rows, setRows] = useState<AutoMessageLogRow[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [open, setOpen] = useState<string | null>(null)

  const load = useCallback(() => {
    setError(null)
    getAutoMessageLog({ rule: rule || undefined, status: status || undefined, from, to })
      .then(setRows)
      .catch((e) => setError(errorText(e, "Jurnalni yuklab bo'lmadi.")))
  }, [rule, status, from, to])

  useEffect(() => {
    load()
  }, [load, reloadKey])

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-end justify-between gap-3 border-b border-slate-100 px-5 py-4">
        <div>
          <h3 className="font-semibold text-slate-800">Jurnal</h3>
          <p className="text-xs text-slate-400">Avtomatik yuborilgan xabarlar — har ota-ona chati alohida qator.</p>
        </div>
        <div className="flex flex-wrap items-end gap-2">
          <Select value={rule} onChange={(e) => setRule(e.target.value)} className="w-48">
            <option value="">Barcha qoidalar</option>
            {(Object.keys(TITLES) as AutoMessageKey[]).map((k) => (
              <option key={k} value={k}>
                {TITLES[k].title}
              </option>
            ))}
          </Select>
          <Select value={status} onChange={(e) => setStatus(e.target.value)} className="w-40">
            <option value="">Barcha holat</option>
            {Object.entries(STATUS).map(([k, v]) => (
              <option key={k} value={k}>
                {v.label}
              </option>
            ))}
          </Select>
          <DatePicker value={from} onChange={setFrom} ariaLabel="Davr boshi" className="w-36" />
          <DatePicker value={to} onChange={setTo} ariaLabel="Davr oxiri" className="w-36" />
        </div>
      </div>
      {error ? (
        <p className="px-5 py-6 text-sm text-red-600">{error}</p>
      ) : !rows ? (
        <Loader label="Yuklanmoqda..." />
      ) : rows.length === 0 ? (
        <p className="px-5 py-10 text-center text-sm text-slate-400">Bu davrda avtomatik xabar yo'q.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-slate-50 text-left text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-5 py-2 font-medium">Vaqt</th>
                <th className="px-3 py-2 font-medium">Qoida</th>
                <th className="px-3 py-2 font-medium">O'quvchi</th>
                <th className="px-3 py-2 font-medium">Ota-ona</th>
                <th className="px-3 py-2 font-medium">Holat</th>
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
                  <td className="px-3 py-2 text-slate-600">{TITLES[r.ruleKey]?.title ?? r.ruleKey}</td>
                  <td className="px-3 py-2">
                    <span className="font-medium text-slate-800">{r.fullName}</span>
                    <span className="text-slate-400"> · {r.className}</span>
                    {open === r.id && r.text && (
                      <RichText html={r.text} className="mt-2 rounded-lg bg-slate-50 px-3 py-2 text-slate-700" />
                    )}
                  </td>
                  <td className="px-3 py-2 text-slate-600">{r.parentName ?? '—'}</td>
                  <td className="px-3 py-2">
                    <span className={cn('rounded-full px-2 py-0.5 text-xs font-medium', STATUS[r.status].cls)}>
                      {STATUS[r.status].label}
                    </span>
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
