import { useEffect, useMemo, useState } from 'react'
import { Award, Loader2, Send, TriangleAlert } from 'lucide-react'
import {
  listExamAwards,
  previewExamAwards,
  sendExamAwards,
  type ExamAwardNotice,
  type ExamAwardPreview,
  type ExamAwardRow,
  type ExamAwardSendResult,
} from '@/api/services/exams'
import { billingErrorMessage } from '@/api/services/billingError'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDate } from '@/lib/utils'

const clampCount = (raw: string) => Math.max(0, Math.min(50, Math.floor(Number(raw) || 0)))

/**
 * «Natija xabarlari» (mijoz, 2026-10-15: "Oylik testni eng top va bottom o'quvchilarining accountiga xabar
 * jo'natish (tashakkurnoma diplom / ogohlantirish) — nechta o'quvchiga jo'natish sonini qo'lda kiritishli
 * qilish"). Xodim nechta eng yaxshi va nechta eng past o'quvchi ekanini kiritadi, ro'yxatni ko'radi (kerakmasini
 * olib tashlaydi), matnni tahrirlaydi va yuboradi: Telegram'ga va o'quvchining ilovadagi e'lonlariga, eng
 * yaxshilarga ixtiyoriy «Tashakkurnoma» PDF. Bir o'quvchiga bir imtihon uchun bir tur — bir marta.
 */
export function ExamAwardsModal({
  examId,
  open,
  canSend,
  onClose,
}: {
  examId: string
  open: boolean
  /** To'liq «Imtihonlar» va «Xabarlar» ruxsati (server ham tekshiradi). */
  canSend: boolean
  onClose: () => void
}) {
  const [topRaw, setTopRaw] = useState('3')
  const [bottomRaw, setBottomRaw] = useState('3')
  const [perClass, setPerClass] = useState(false)
  const top = clampCount(topRaw)
  const bottom = clampCount(bottomRaw)

  const [preview, setPreview] = useState<ExamAwardPreview | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [topText, setTopText] = useState<string | null>(null)
  const [bottomText, setBottomText] = useState<string | null>(null)
  const [diploma, setDiploma] = useState(true)
  const [excluded, setExcluded] = useState<Set<string>>(new Set())
  const [confirming, setConfirming] = useState(false)
  const [sending, setSending] = useState(false)
  const [result, setResult] = useState<ExamAwardSendResult | null>(null)
  const [sent, setSent] = useState<ExamAwardNotice[]>([])
  const [version, setVersion] = useState(0)

  useEffect(() => {
    if (!open) return
    let active = true
    const timer = window.setTimeout(() => {
      setLoading(true)
      setError(null)
      Promise.all([previewExamAwards(examId, top, bottom, perClass), listExamAwards(examId)])
        .then(([p, s]) => {
          if (!active) return
          setPreview(p)
          setSent(s)
          setTopText((t) => t ?? p.topText)
          setBottomText((t) => t ?? p.bottomText)
        })
        .catch((e: unknown) => active && setError(billingErrorMessage(e, "Ro'yxatni yuklab bo'lmadi")))
        .finally(() => active && setLoading(false))
    }, 250)
    return () => {
      active = false
      window.clearTimeout(timer)
    }
  }, [open, examId, top, bottom, perClass, version])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- soni yoki qamrov o'zgarsa tasdiq va natija eskiradi (maqsadli)
    setConfirming(false)
    setResult(null)
  }, [top, bottom, perClass])

  const rows = useMemo(() => {
    const all: { row: ExamAwardRow; kind: 'top' | 'bottom' }[] = []
    for (const g of preview?.groups ?? []) {
      for (const row of g.top) all.push({ row, kind: 'top' })
      for (const row of g.bottom) all.push({ row, kind: 'bottom' })
    }
    return all
  }, [preview])
  const toSend = rows.filter((r) => !r.row.alreadySent && !excluded.has(r.row.studentId))

  const toggle = (id: string) =>
    setExcluded((cur) => {
      const next = new Set(cur)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const send = () => {
    if (!preview || sending) return
    setSending(true)
    setError(null)
    sendExamAwards(examId, {
      top,
      bottom,
      perClass,
      topText: top > 0 ? topText : null,
      bottomText: bottom > 0 ? bottomText : null,
      diploma,
      excludeStudentIds: [...excluded],
    })
      .then((r) => {
        setResult(r)
        setConfirming(false)
        setVersion((v) => v + 1)
      })
      .catch((e: unknown) => setError(billingErrorMessage(e, "Yuborib bo'lmadi")))
      .finally(() => setSending(false))
  }

  const footer = (
    <div className="flex w-full flex-wrap items-center justify-end gap-2">
      <Button variant="ghost" onClick={onClose}>
        Yopish
      </Button>
      {canSend &&
        (confirming ? (
          <>
            <span className="text-sm text-slate-500">{toSend.length} ta o'quvchiga yuborilsinmi?</span>
            <Button variant="secondary" onClick={() => setConfirming(false)} disabled={sending}>
              Yo'q
            </Button>
            <Button onClick={send} disabled={sending}>
              {sending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
              Ha, yuborish
            </Button>
          </>
        ) : (
          <Button
            onClick={() => setConfirming(true)}
            disabled={!preview || toSend.length === 0 || !preview.telegramConfigured || loading}
          >
            <Send className="h-4 w-4" /> Yuborish ({toSend.length})
          </Button>
        ))}
    </div>
  )

  return (
    <Modal open={open} onClose={onClose} title="Natija xabarlari" size="xl" footer={footer}>
      <div className="space-y-5">
        <div className="grid gap-3 sm:grid-cols-3">
          <CountField label="Eng yaxshi (nechta o'quvchi)" value={topRaw} onChange={setTopRaw} />
          <CountField label="Eng past (nechta o'quvchi)" value={bottomRaw} onChange={setBottomRaw} />
          <label className="flex items-end gap-2 pb-2 text-sm text-slate-600">
            <input type="checkbox" checked={perClass} onChange={(e) => setPerClass(e.target.checked)} className="h-4 w-4 rounded" />
            Har sinf bo'yicha alohida
          </label>
        </div>

        {preview && !preview.telegramConfigured && (
          <p className="flex items-center gap-2 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-700">
            <TriangleAlert className="h-4 w-4 shrink-0" /> Telegram bot sozlanmagan — xabar yuborib bo'lmaydi.
          </p>
        )}
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
        {result && (
          <p className="rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-700">
            Yuborildi: {result.topSent} ta tashakkurnoma, {result.bottomSent} ta ogohlantirish · {result.delivered} ta chatga yetdi
            {result.noTelegram > 0 && ` · ${result.noTelegram} tasida Telegram yo'q (ilovadagi e'lonlarda turadi)`}
            {result.failed > 0 && ` · ${result.failed} tasiga yetmadi`}
            {result.skipped > 0 && ` · ${result.skipped} tasiga avval yuborilgan`}
          </p>
        )}

        {loading && !preview ? (
          <Loader label="Yuklanmoqda..." />
        ) : preview && preview.scored === 0 ? (
          <p className="py-8 text-center text-sm text-slate-400">Bu imtihonda hali baholangan o'quvchi yo'q.</p>
        ) : (
          preview && (
            <div className={cn('space-y-4 transition-opacity', loading && 'opacity-60')}>
              {preview.groups.map((g) => (
                <div key={g.className ?? 'all'} className="space-y-2">
                  {g.className && (
                    <p className="text-sm font-semibold text-slate-700">
                      {g.className} <span className="font-normal text-slate-400">· {g.scored} ta baholangan</span>
                    </p>
                  )}
                  <div className="grid gap-3 md:grid-cols-2">
                    <RowList title="Tashakkurnoma" tone="top" rows={g.top} excluded={excluded} onToggle={toggle} canSend={canSend} />
                    <RowList title="Ogohlantirish" tone="bottom" rows={g.bottom} excluded={excluded} onToggle={toggle} canSend={canSend} />
                  </div>
                </div>
              ))}
              <p className="text-xs text-slate-400">
                Teng natijali o'quvchilar bitta o'rinni bo'lishadi va chegarada hammasi kiradi. Bitta o'quvchi ikkala
                ro'yxatga tushmaydi. Avval yuborilganlar qayta yuborilmaydi.
              </p>
            </div>
          )
        )}

        {canSend && preview && (
          <div className="grid gap-3 md:grid-cols-2">
            {top > 0 && (
              <TextBlock label="Tashakkurnoma matni" value={topText ?? ''} onChange={setTopText} tokens={preview.tokens} />
            )}
            {bottom > 0 && (
              <TextBlock label="Ogohlantirish matni" value={bottomText ?? ''} onChange={setBottomText} tokens={preview.tokens} />
            )}
          </div>
        )}
        {canSend && top > 0 && (
          <label className="flex items-center gap-2 text-sm text-slate-600">
            <input type="checkbox" checked={diploma} onChange={(e) => setDiploma(e.target.checked)} className="h-4 w-4 rounded" />
            Eng yaxshilarga «Tashakkurnoma» (PDF) ham yuborilsin
          </label>
        )}

        {sent.length > 0 && (
          <div>
            <p className="mb-2 text-sm font-medium text-slate-600">Yuborilganlar ({sent.length})</p>
            <div className="max-h-56 space-y-1 overflow-y-auto">
              {sent.map((n) => (
                <div key={n.id} className="flex flex-wrap items-center gap-2 rounded-lg border border-slate-100 px-3 py-1.5 text-sm">
                  <span className={cn('rounded px-1.5 py-0.5 text-xs font-medium', n.kind === 'top' ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700')}>
                    {n.kind === 'top' ? 'Tashakkurnoma' : 'Ogohlantirish'}
                  </span>
                  <span className="text-slate-700">{n.fullName}</span>
                  <span className="text-slate-400">{n.className}</span>
                  {n.withDiploma && <Award className="h-3.5 w-3.5 text-amber-500" />}
                  <span className="ml-auto text-xs text-slate-400">
                    {n.delivered > 0 ? `${n.delivered} ta chat` : 'Telegramga yetmadi'} · {formatDate(n.sentAt.slice(0, 10))}
                    {n.sentByName && ` · ${n.sentByName}`}
                  </span>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </Modal>
  )
}

function CountField({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <label className="block text-sm">
      <span className="mb-1 block font-medium text-slate-600">{label}</span>
      <input
        type="number"
        min={0}
        max={50}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400"
      />
    </label>
  )
}

function RowList({
  title,
  tone,
  rows,
  excluded,
  onToggle,
  canSend,
}: {
  title: string
  tone: 'top' | 'bottom'
  rows: ExamAwardRow[]
  excluded: Set<string>
  onToggle: (id: string) => void
  canSend: boolean
}) {
  return (
    <div className="rounded-xl border border-slate-100">
      <p
        className={cn(
          'rounded-t-xl px-3 py-2 text-xs font-semibold uppercase tracking-wide',
          tone === 'top' ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700',
        )}
      >
        {title} · {rows.length}
      </p>
      {rows.length === 0 ? (
        <p className="px-3 py-3 text-sm text-slate-400">—</p>
      ) : (
        <ul className="divide-y divide-slate-50">
          {rows.map((r) => (
            <li key={r.studentId} className="flex items-center gap-2 px-3 py-2 text-sm">
              {canSend && (
                <input
                  type="checkbox"
                  disabled={r.alreadySent}
                  checked={!r.alreadySent && !excluded.has(r.studentId)}
                  onChange={() => onToggle(r.studentId)}
                  className="h-4 w-4 rounded"
                />
              )}
              <span className="w-6 text-right font-semibold text-slate-400">{r.rank}</span>
              <span className="min-w-0 flex-1 truncate text-slate-700">
                {r.fullName} <span className="text-slate-400">{r.className}</span>
              </span>
              <span className="font-medium text-slate-700">{r.percent ?? '—'}%</span>
              {r.alreadySent ? (
                <span className="rounded bg-slate-100 px-1.5 py-0.5 text-xs text-slate-500">yuborilgan</span>
              ) : (
                r.chats === 0 && <span className="rounded bg-slate-100 px-1.5 py-0.5 text-xs text-slate-500">Telegram yo'q</span>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function TextBlock({
  label,
  value,
  onChange,
  tokens,
}: {
  label: string
  value: string
  onChange: (v: string) => void
  tokens: string[]
}) {
  return (
    <label className="block text-sm">
      <span className="mb-1 block font-medium text-slate-600">{label}</span>
      <textarea
        rows={6}
        maxLength={2000}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full resize-y rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400"
      />
      <span className="mt-1 block text-xs text-slate-400">
        O'rinbosarlar: {tokens.join(' ')} · **qalin**, __kursiv__
      </span>
    </label>
  )
}
