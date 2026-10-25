/**
 * Avtomatik xabarlar — ota-onalarga qoida bo'yicha Telegram xabari (docs/modules/auto-messages.md,
 * 2026-10-13): yo'qlik, yangi oylik hisob, qarz eslatmasi, tug'ilgan kun, to'lov qabul qilinganda.
 * Har qoida O'CHIQ holda keladi — maktab matnni tasdiqlagach shu yerda yoqiladi.
 * 2026-10-15: ikkinchi bo'lim "Tarix va statistika" (AutoMessagesHistory) — yuborishlar, tasdiqlash,
 * yetkazish jurnali va statistika.
 * 2026-10-25: qoidaga rasm qo'shiladi (e'londagi kabi, 10 tagacha) — to'lov qoidasidan tashqari,
 * u chek PDF'i bilan ketadi.
 */
import { useEffect, useRef, useState } from 'react'
import { AlertTriangle, CalendarClock, Eye, RotateCcw, Save } from 'lucide-react'
import {
  getAutoMessageRules,
  previewAutoMessage,
  saveAutoMessageRule,
  type AutoMessagePreview,
  type AutoMessageRule,
} from '@/api/services/autoMessages'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Input, Select, Time24Input } from '@/components/ui/Input'
import { FormatToolbar } from '@/components/ui/FormatToolbar'
import { RichText } from '@/components/ui/RichText'
import { Switch } from '@/components/hr-tasks/ui'
import { cn } from '@/lib/utils'
import { AutoMessagesHistory } from './AutoMessagesHistory'
import { BroadcastImagePicker } from './BroadcastImagePicker'
import { TITLES, errorText, fmtDateTime } from './autoMessageLabels'

const VIEWS = [
  { key: 'rules', label: 'Qoidalar va shablonlar' },
  { key: 'history', label: 'Tarix va statistika' },
] as const

export function AutoMessagesPanel() {
  const [view, setView] = useState<(typeof VIEWS)[number]['key']>('rules')
  const [rules, setRules] = useState<AutoMessageRule[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getAutoMessageRules()
      .then(setRules)
      .catch((e) => setError(errorText(e, "Qoidalarni yuklab bo'lmadi.")))
  }, [])

  return (
    <div className="space-y-6">
      <div className="flex gap-1 border-b border-slate-200 text-sm font-medium">
        {VIEWS.map((v) => (
          <button
            key={v.key}
            type="button"
            onClick={() => setView(v.key)}
            className={cn(
              '-mb-px border-b-2 px-3 py-2 transition-colors',
              view === v.key ? 'border-brand-500 text-slate-800' : 'border-transparent text-slate-500 hover:text-slate-700',
            )}
          >
            {v.label}
          </button>
        ))}
      </div>

      {view === 'history' ? (
        <AutoMessagesHistory />
      ) : error ? (
        <p className="text-sm text-red-600">{error}</p>
      ) : !rules ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          <p className="flex items-start gap-2 rounded-xl bg-amber-50 px-4 py-3 text-sm text-amber-800">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            Yoqilgan qoida ota-onalarga o'zi xabar yuboradi. Avval matnni maktab bilan kelishib oling va "Namuna"
            tugmasi bilan kimga ketishini ko'ring. "Tasdiq bilan" yoqilsa, xabarlar tayyorlanadi va "Tarix va
            statistika" bo'limida tasdiqlangandan keyingina ketadi.
          </p>
          <div className="grid gap-4 xl:grid-cols-2">
            {rules.map((r) => (
              <RuleCard
                key={r.key}
                rule={r}
                onSaved={(next) =>
                  setRules((list) => list?.map((x) => (x.key === next.key ? { ...x, ...next } : x)) ?? null)
                }
              />
            ))}
          </div>
        </>
      )}
    </div>
  )
}

function RuleCard({ rule, onSaved }: { rule: AutoMessageRule; onSaved: (r: AutoMessageRule) => void }) {
  const [enabled, setEnabled] = useState(rule.enabled)
  const [sendTime, setSendTime] = useState(rule.sendTime)
  const [day, setDay] = useState(rule.dayOfMonth)
  const [minAmount, setMinAmount] = useState(String(rule.minAmount))
  const [text, setText] = useState(rule.text)
  const [approval, setApproval] = useState(rule.requiresApproval)
  const [cutoff, setCutoff] = useState(rule.cutoffTime ?? '')
  const [images, setImages] = useState<string[]>(rule.imageUrls ?? [])
  const [uploading, setUploading] = useState(false)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null)
  const [preview, setPreview] = useState<AutoMessagePreview | null>(null)
  const [previewBusy, setPreviewBusy] = useState(false)
  const textRef = useRef<HTMLTextAreaElement>(null)
  const meta = TITLES[rule.key]
  const isAbsence = rule.key === 'absence'

  const dirty =
    enabled !== rule.enabled ||
    sendTime !== rule.sendTime ||
    (rule.monthly && day !== rule.dayOfMonth) ||
    (rule.key === 'debt' && Number(minAmount || 0) !== rule.minAmount) ||
    text !== rule.text ||
    (!rule.eventDriven && approval !== rule.requiresApproval) ||
    (isAbsence && cutoff !== (rule.cutoffTime ?? '')) ||
    (!rule.eventDriven && images.join('\n') !== (rule.imageUrls ?? []).join('\n'))

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
        requiresApproval: rule.eventDriven ? undefined : approval,
        cutoffTime: isAbsence ? cutoff || null : undefined,
        imageUrls: rule.eventDriven ? undefined : images,
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

      {!rule.eventDriven && (
        <div className="flex flex-wrap items-end gap-4">
          <div>
            <span className="mb-1 block text-sm font-medium text-slate-600">Vaqti</span>
            <Time24Input value={sendTime} onChange={setSendTime} />
          </div>
          {isAbsence && (
            <div>
              <span className="mb-1 block text-sm font-medium text-slate-600">Qaysi darsgacha</span>
              <div className="flex items-center gap-2">
                <Time24Input value={cutoff || sendTime} onChange={setCutoff} />
                {cutoff && cutoff !== sendTime && (
                  <button
                    type="button"
                    onClick={() => setCutoff('')}
                    className="text-xs text-slate-400 hover:text-slate-600"
                    title="Yuborish vaqti bilan bir xil"
                  >
                    <RotateCcw className="h-3.5 w-3.5" />
                  </button>
                )}
              </div>
            </div>
          )}
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
          <label className="flex items-center gap-2 pb-2 text-sm text-slate-600">
            <Switch checked={approval} disabled={busy} ariaLabel="Tasdiq bilan yuborish" onChange={setApproval} />
            Tasdiq bilan
          </label>
        </div>
      )}
      {isAbsence && (
        <p className="-mt-2 text-xs text-slate-400">
          {cutoff || sendTime} gacha boshlangan darslar hisoblanadi; shu darslarning davomati to'liq olinmagan sinfga
          xabar ketmaydi.
        </p>
      )}

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
              {t.label ? (
                <>
                  {t.label} <span className="font-normal text-slate-400">{t.token}</span>
                </>
              ) : (
                t.token
              )}
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

      {!rule.eventDriven && (
        <BroadcastImagePicker value={images} onChange={setImages} onBusyChange={setUploading} />
      )}

      {preview && (
        <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm">
          {rule.eventDriven ? (
            <p className="mb-1 text-xs text-slate-500">Namuna — to'qima qiymatlar bilan, chek ostida shunday chiqadi:</p>
          ) : (
            <p className="mb-1 text-xs text-slate-500">
              Hozir ishga tushsa: <b className="text-slate-700">{preview.pupils}</b> o'quvchi, ota-onasi Telegramda —{' '}
              <b className="text-slate-700">{preview.withTelegram}</b>
              {preview.alreadySent > 0 && <>, allaqachon yuborilgan — {preview.alreadySent}</>}.
            </p>
          )}
          {preview.sample ? (
            <>
              {!rule.eventDriven && (
                <p className="mb-1 text-xs font-medium text-slate-400">Namuna ({preview.sampleFor}):</p>
              )}
              {!rule.eventDriven && images.length > 0 && (
                <div className="mb-2 flex flex-wrap gap-1.5">
                  {images.map((url, i) => (
                    <img key={`${url}-${i}`} src={url} alt="" className="h-16 w-16 rounded-md object-cover" />
                  ))}
                </div>
              )}
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
          {rule.eventDriven ? (
            "Har to'lovda — natijasi \"Tarix va statistika\" bo'limida"
          ) : rule.lastRunAt ? (
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
          <Button onClick={() => void save()} disabled={busy || uploading || !dirty}>
            <Save className="h-4 w-4" /> Saqlash
          </Button>
        </div>
      </div>
      {msg && <p className={cn('text-sm', msg.ok ? 'text-emerald-700' : 'text-red-600')}>{msg.text}</p>}
    </Card>
  )
}
