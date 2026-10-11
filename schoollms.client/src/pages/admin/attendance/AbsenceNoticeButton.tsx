/**
 * "Kelmaganlarga xabar" — hamshira kunni belgilab bo'lgach bitta tugma (mijoz, 2026-10-11: "bir
 * tugmani bosish orqali kelmagan o'quvchini ota onasiga ... avtomatik habar yuborish").
 *
 * Oyna avval ro'yxatni ko'rsatadi: kim kun bo'yi kelmagan, sababi, ota-onasi Telegramga ulanganmi
 * va xabar allaqachon ketganmi. Tasdiqdan keyin faqat hali xabar olmaganlarga yuboriladi — tugmani
 * qayta bosish takror xabar bermaydi (server har o'quvchiga kuniga bitta xabar yozadi).
 */
import { useEffect, useState } from 'react'
import { CheckCircle2, Send } from 'lucide-react'
import {
  getAbsenceNotices,
  sendAbsenceNotices,
  type AbsenceNoticePreview,
} from '@/api/services/attendanceMarking'
import { billingErrorMessage } from '@/api/services/billingError'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'

export function AbsenceNoticeButton({
  date,
  absentTotal,
  onDone,
}: {
  date: string
  /** Kun bo'yi kelmaganlar soni (ekrandagi "N o'quvchi kelmadi") — tugmada ko'rinadi. */
  absentTotal: number
  onDone: (message: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [preview, setPreview] = useState<AbsenceNoticePreview | null>(null)
  const [loading, setLoading] = useState(false)
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda ro'yxatni yangidan o'qiymiz (maqsadli)
    setLoading(true)
    setError(null)
    getAbsenceNotices(date)
      .then((p) => active && setPreview(p))
      .catch((e: unknown) => active && setError(billingErrorMessage(e, "Ro'yxatni yuklab bo'lmadi")))
      .finally(() => active && setLoading(false))
    return () => {
      active = false
    }
  }, [open, date])

  const send = async () => {
    setSending(true)
    setError(null)
    try {
      const r = await sendAbsenceNotices(date)
      setOpen(false)
      const parts = [`${r.sent} ta ota-onaga xabar yuborildi`]
      if (r.alreadySent > 0) parts.push(`${r.alreadySent} tasiga oldin yuborilgan`)
      if (r.noTelegram > 0) parts.push(`${r.noTelegram} tasi Telegramga ulanmagan`)
      if (r.failed > 0) parts.push(`${r.failed} tasiga yetmadi`)
      onDone(parts.join(' · '))
    } catch (e) {
      setError(billingErrorMessage(e, "Xabar yuborib bo'lmadi"))
    } finally {
      setSending(false)
    }
  }

  return (
    <>
      <Button variant="secondary" onClick={() => setOpen(true)} title="Kun bo'yi kelmaganlarning ota-onalariga Telegram xabari">
        <Send className="h-4 w-4" /> Kelmaganlarga xabar
        {absentTotal > 0 && (
          <span className="ml-1 rounded-full bg-red-500 px-1.5 text-[11px] font-bold text-white">{absentTotal}</span>
        )}
      </Button>

      <Modal
        open={open}
        onClose={() => !sending && setOpen(false)}
        title="Kelmaganlarning ota-onalariga xabar"
        size="lg"
        footer={
          <>
            <Button variant="secondary" onClick={() => setOpen(false)} disabled={sending}>
              Bekor qilish
            </Button>
            <Button onClick={send} disabled={sending || loading || !preview || preview.ready === 0 || !preview.botConfigured}>
              <Send className="h-4 w-4" />
              {sending ? 'Yuborilmoqda…' : `Yuborish${preview && preview.ready > 0 ? ` (${preview.ready} ta)` : ''}`}
            </Button>
          </>
        }
      >
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : error && !preview ? (
          <p className="py-6 text-center text-sm text-red-600">{error}</p>
        ) : preview ? (
          <div className="space-y-3">
            <p className="text-sm text-slate-600">
              Xabar matni: <span className="italic text-slate-500">"Hurmatli ota-ona, farzandingiz … kuni maktabga kelmadi. Sabab: …"</span>
              <br />
              Faqat <b>kun bo'yi</b> kelmaganlar — ayrim darslarga kelmagan yoki kech kelganlarga yuborilmaydi.
            </p>
            {!preview.botConfigured && (
              <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-700">
                Telegram bot sozlanmagan — Sozlamalar → Integratsiyalar → Telegram bot.
              </p>
            )}
            {preview.rows.length === 0 ? (
              <p className="py-6 text-center text-sm text-slate-400">Bu kuni kun bo'yi kelmagan o'quvchi yo'q.</p>
            ) : (
              <>
                <div className="flex flex-wrap gap-2 text-xs">
                  <span className="rounded-md bg-brand-50 px-2 py-1 font-medium text-brand-700">Yuboriladi: {preview.ready}</span>
                  <span className="rounded-md bg-emerald-50 px-2 py-1 font-medium text-emerald-700">Oldin yuborilgan: {preview.alreadySent}</span>
                  <span className="rounded-md bg-amber-50 px-2 py-1 font-medium text-amber-700">Telegram yo'q: {preview.noTelegram}</span>
                </div>
                <div className="max-h-[50vh] overflow-y-auto rounded-xl border border-slate-100">
                  <table className="w-full text-sm">
                    <thead className="sticky top-0 bg-slate-50 text-left text-xs text-slate-400">
                      <tr>
                        <th className="px-3 py-2 font-medium">O'quvchi</th>
                        <th className="px-3 py-2 font-medium">Sinf</th>
                        <th className="px-3 py-2 font-medium">Sabab</th>
                        <th className="px-3 py-2 font-medium">Holat</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {preview.rows.map((r) => (
                        <tr key={r.studentId}>
                          <td className="px-3 py-2 font-medium text-slate-800">{r.fullName}</td>
                          <td className="px-3 py-2 text-slate-500">{r.rowName}</td>
                          <td className={cn('px-3 py-2', r.unexcused ? 'text-red-600' : 'text-slate-600')}>
                            {r.reasonName ?? '—'}
                          </td>
                          <td className="px-3 py-2">
                            {r.sentAt ? (
                              <span className="inline-flex items-center gap-1 text-xs text-emerald-700">
                                <CheckCircle2 className="h-3.5 w-3.5" /> Yuborilgan
                              </span>
                            ) : r.telegram ? (
                              <span className="text-xs font-medium text-brand-700">Yuboriladi</span>
                            ) : (
                              <span className="text-xs text-amber-700">Telegramga ulanmagan</span>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </>
            )}
            {error && <p className="text-sm text-red-600">{error}</p>}
          </div>
        ) : null}
      </Modal>
    </>
  )
}
