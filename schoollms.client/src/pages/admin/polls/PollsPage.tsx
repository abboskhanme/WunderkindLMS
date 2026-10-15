import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { BarChart3, Copy, Link2, Lock, Megaphone, Pencil, Play, Plus, Square, Trash2, Users } from 'lucide-react'
import {
  closePoll,
  deletePoll,
  duplicatePoll,
  getPollRecipients,
  getPolls,
  notifyPoll,
  openPoll,
  pollLink,
  POLL_AUDIENCE,
  POLL_STATUS,
  type Poll,
} from '@/api/services/polls'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Toast } from '@/components/ui/Toast'
import { fmtDate } from '@/components/lesson-analysis/format'
import { PollEditorModal } from './PollEditorModal'

const STATUS_TONE: Record<Poll['status'], string> = {
  draft: 'bg-slate-100 text-slate-600',
  open: 'bg-emerald-50 text-emerald-700',
  closed: 'bg-slate-100 text-slate-400',
}

/**
 * So'rovnomalar va NPS (client, 2026-10-07): polls for parents or teachers, answered in the
 * Telegram Mini App; the results page shows NPS by class and by person. Since 2026-10-15 also
 * Google-Forms-like polls for anyone with the link (`/sorovnoma/{token}`), "Nusxa olish" and
 * the link copy button (docs/modules/surveys-forms.md).
 */
export function PollsPage() {
  const [polls, setPolls] = useState<Poll[] | null>(null)
  const [editing, setEditing] = useState<Poll | 'new' | null>(null)
  const [announce, setAnnounce] = useState<{ poll: Poll; count: number; botConfigured: boolean } | null>(null)
  const [busy, setBusy] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    getPolls()
      .then(setPolls)
      .catch((e) => {
        setPolls([])
        setError(hrErrorMessage(e, "Yuklab bo'lmadi"))
      })
  }, [])
  useEffect(load, [load])

  const run = async <T,>(action: () => Promise<T>, done: string | ((result: T) => string)) => {
    setBusy(true)
    setError('')
    try {
      const result = await action()
      setToast(typeof done === 'string' ? done : done(result))
      load()
    } catch (e) {
      setError(hrErrorMessage(e, 'Bajarib bo\'lmadi'))
    } finally {
      setBusy(false)
    }
  }

  /** The clipboard needs a secure context; elsewhere the link is shown to copy by hand. */
  const copyLink = async (token: string) => {
    const url = pollLink(token)
    try {
      await navigator.clipboard.writeText(url)
      setToast('Havola nusxalandi')
    } catch {
      window.prompt("Havolani nusxalang", url)
    }
  }

  const askAnnounce = async (poll: Poll) => {
    try {
      const r = await getPollRecipients(poll.id)
      setAnnounce({ poll, ...r })
    } catch (e) {
      setError(hrErrorMessage(e, "Qabul qiluvchilarni aniqlab bo'lmadi"))
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">So'rovnomalar va NPS</h1>
          <p className="text-sm text-slate-400">
            Ota-onalar va o'qituvchilar Telegram ilovasida, boshqalar havola orqali javob beradi — natijada maktab, ustozlar
            va rahbariyat NPS'i
          </p>
        </div>
        <Button onClick={() => setEditing('new')}>
          <Plus className="h-4 w-4" /> Yangi so'rovnoma
        </Button>
      </div>

      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      {polls === null ? (
        <Loader label="Yuklanmoqda..." />
      ) : polls.length === 0 ? (
        <Card>
          <p className="py-10 text-center text-slate-400">Hali so'rovnoma yo'q — "NPS shabloni" bilan boshlash qulay</p>
        </Card>
      ) : (
        <div className="space-y-3">
          {polls.map((p) => (
            <Card key={p.id} className="flex flex-col gap-3">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <h2 className="text-base font-semibold text-slate-800">{p.title}</h2>
                    <span className={'rounded-md px-2 py-0.5 text-xs font-medium ' + STATUS_TONE[p.status]}>
                      {POLL_STATUS[p.status]}
                    </span>
                    <span className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">
                      {POLL_AUDIENCE[p.audience]}
                    </span>
                    {p.anonymous && (
                      <span className="inline-flex items-center gap-1 rounded-md bg-slate-100 px-2 py-0.5 text-xs text-slate-500">
                        <Lock className="h-3 w-3" /> Anonim
                      </span>
                    )}
                    {p.publicToken && p.audience !== 'public' && (
                      <span className="inline-flex items-center gap-1 rounded-md bg-slate-100 px-2 py-0.5 text-xs text-slate-500">
                        <Link2 className="h-3 w-3" /> Havola orqali ham
                      </span>
                    )}
                    {p.publicToken && p.collectContact && (
                      <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs text-slate-500">Ism va telefon so'raladi</span>
                    )}
                  </div>
                  {p.description && <p className="mt-1 text-sm text-slate-500">{p.description}</p>}
                  <p className="mt-1 text-xs text-slate-400">
                    {p.questions.filter((q) => q.kind !== 'section').length} ta savol · yaratilgan {fmtDate(p.createdAt)}
                    {p.closesOn && ` · ${fmtDate(p.closesOn)} gacha`}
                    {p.notifiedAt && ` · botdan e'lon qilingan ${fmtDate(p.notifiedAt)}`}
                  </p>
                </div>
                <div className="flex items-center gap-1.5 text-slate-600">
                  <Users className="h-4 w-4 text-slate-400" />
                  <span className="text-lg font-semibold text-slate-800">{p.responses}</span>
                  <span className="text-sm text-slate-400">javob</span>
                </div>
              </div>

              <div className="flex flex-wrap gap-2">
                <Link
                  to={`/admin/boshqaruv/polls/${p.id}`}
                  className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
                >
                  <BarChart3 className="h-4 w-4" /> Natijalar
                </Link>
                <Button variant="secondary" onClick={() => setEditing(p)} disabled={busy}>
                  <Pencil className="h-4 w-4" /> Tahrirlash
                </Button>
                {p.status !== 'open' ? (
                  <Button
                    variant="secondary"
                    disabled={busy}
                    onClick={() =>
                      run(() => openPoll(p.id), p.audience === 'public' ? "So'rovnoma ochildi — havola ishlaydi" : "So'rovnoma ochildi — ilovada ko'rinadi")
                    }
                  >
                    <Play className="h-4 w-4" /> Ochish
                  </Button>
                ) : (
                  <>
                    <Button variant="secondary" disabled={busy} onClick={() => run(() => closePoll(p.id), "So'rovnoma yopildi")}>
                      <Square className="h-4 w-4" /> Yopish
                    </Button>
                    {!p.notifiedAt && p.audience !== 'public' && (
                      <Button variant="secondary" disabled={busy} onClick={() => askAnnounce(p)}>
                        <Megaphone className="h-4 w-4" /> Botdan e'lon qilish
                      </Button>
                    )}
                  </>
                )}
                {p.publicToken && (
                  <Button variant="secondary" onClick={() => copyLink(p.publicToken!)}>
                    <Link2 className="h-4 w-4" /> Havolani nusxalash
                  </Button>
                )}
                <Button variant="ghost" disabled={busy} onClick={() => run(() => duplicatePoll(p.id), "Nusxa yaratildi — qoralama")}>
                  <Copy className="h-4 w-4" /> Nusxa olish
                </Button>
                {p.responses === 0 && (
                  <Button variant="ghost" disabled={busy} onClick={() => run(() => deletePoll(p.id), "So'rovnoma o'chirildi")}>
                    <Trash2 className="h-4 w-4" /> O'chirish
                  </Button>
                )}
              </div>
            </Card>
          ))}
        </div>
      )}

      {editing && (
        <PollEditorModal
          poll={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            setToast('Saqlandi')
            load()
          }}
        />
      )}

      {announce && (
        <Modal
          open
          size="sm"
          onClose={() => setAnnounce(null)}
          title="Botdan e'lon qilish"
          footer={
            <>
              <Button variant="secondary" onClick={() => setAnnounce(null)}>
                Bekor qilish
              </Button>
              <Button
                disabled={busy || !announce.botConfigured || announce.count === 0}
                onClick={() => {
                  const id = announce.poll.id
                  setAnnounce(null)
                  run(() => notifyPoll(id), (r) => `E'lon ${r.sent} ta chatga yuborildi`)
                }}
              >
                Yuborish
              </Button>
            </>
          }
        >
          {announce.botConfigured ? (
            <p className="text-sm text-slate-600">
              «{announce.poll.title}» haqida <b>{announce.count}</b> ta {POLL_AUDIENCE[announce.poll.audience].toLowerCase()}ga
              Telegram xabari boradi. E'lon faqat bir marta yuboriladi.
            </p>
          ) : (
            <p className="text-sm text-amber-700">Telegram bot sozlanmagan — e'lon yuborib bo'lmaydi.</p>
          )}
        </Modal>
      )}

      <Toast message={toast} onClose={() => setToast(null)} />
    </div>
  )
}
