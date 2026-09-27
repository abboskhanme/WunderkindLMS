/**
 * Rahbar biriktirgan OVOZLI IZOH — kartochka ichidagi ijrochi (eski `components/VoiceNote.tsx`).
 *
 * Boshida faqat "🎧 Ovozli izoh · 1:23" yorlig'i; `<audio>` BOSILGANDA o'rnatiladi —
 * telefonda joy kam va `preload="metadata"` har kartochkada ortiqcha so'rov bo'lardi.
 *
 * Zaxira yo'l (MAJBURIY): iOS `webm` ni o'qimaydi — ijrochi yiqilsa, "Ovozni yuklab
 * olish" havolasiga almashadi: ovoz yo'qolmaydi, kartochka buzilmaydi.
 */
import { useState } from 'react'
import { formatDuration } from '../../lib/hrFormat'
import { haptic } from '../../lib/telegram'
import { t } from './texts'

export function VoiceNote({ url, durationSeconds }) {
  const [open, setOpen] = useState(false)
  const [failed, setFailed] = useState(false)

  const duration = formatDuration(durationSeconds)
  const label = duration === null ? t('tasks.voice_open') : t('tasks.voice_open_with', { duration })

  return (
    <div className="mt-2.5">
      <button
        type="button"
        aria-expanded={open}
        onClick={() => {
          haptic('light')
          setOpen((v) => !v)
        }}
        className="rounded-full bg-brand/25 px-3 py-1.5 text-[13px] font-semibold text-brand-ink"
      >
        {open ? t('tasks.voice_hide') : label}
      </button>

      {open && !failed && (
        <audio
          src={url}
          controls
          preload="metadata"
          aria-label={t('tasks.voice')}
          onError={() => setFailed(true)}
          className="mt-2 block w-full"
        />
      )}

      {open && failed && (
        <p className="mt-2 text-[13px] text-slate-500">
          <span>{t('tasks.voice_failed')}</span>{' '}
          {/* `download` VA `target="_blank"` birga: Telegram WebView `download` ni
              e'tiborsiz qoldirsa, fayl hech bo'lmasa yangi oynada ochiladi. */}
          <a href={url} download target="_blank" rel="noreferrer noopener" className="font-semibold text-brand-ink underline">
            {t('tasks.voice_download')}
          </a>
        </p>
      )}
    </div>
  )
}
