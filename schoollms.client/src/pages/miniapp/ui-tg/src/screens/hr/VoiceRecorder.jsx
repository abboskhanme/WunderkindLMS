/**
 * Ovozli izoh YOZISH — "Bajardim" oynasida (hr-tasks.md §8: comment + files + voice note).
 *
 * Yozilgan ovoz oddiy ISBOT FAYLI bo'lib ketadi (`POST /hr/me/proofs`): alohida
 * endpoint yo'q, 5 ta fayl chegarasiga kiradi, isbot sifatida "fayl" bilan teng.
 * Uzunlik — ko'pi bilan `MAX_VOICE_SECONDS` (5 daqiqa), vaqt tugasa o'zi to'xtaydi.
 *
 * ZAXIRA YO'L: `MediaRecorder`/mikrofon bo'lmasa (eski WebView) yoki ruxsat
 * berilmasa, tugma oddiy `<input type="file" accept="audio/*" capture>` ni ochadi —
 * Android ovoz yozgichni, iOS fayl tanlagichni ko'rsatadi. U yo'l bilan tanlangan
 * ovozning uzunligi ham tekshiriladi (o'qib bo'lmasa — qaror serverga qoladi).
 */
import { useEffect, useRef, useState } from 'react'
import { Mic, Square } from 'lucide-react'
import { MAX_VOICE_SECONDS, audioDuration, formatDuration } from '../../lib/hrFormat'
import { haptic, hapticNotify } from '../../lib/telegram'
import { Btn } from './shared'
import { t } from './texts'

const MIME_CANDIDATES = ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4', 'audio/aac', 'audio/ogg;codecs=opus']

function canRecord() {
  return (
    typeof window !== 'undefined' &&
    typeof window.MediaRecorder !== 'undefined' &&
    Boolean(navigator.mediaDevices?.getUserMedia)
  )
}

function pickMime() {
  try {
    return MIME_CANDIDATES.find((m) => window.MediaRecorder.isTypeSupported?.(m)) ?? ''
  } catch {
    return ''
  }
}

function extensionOf(type) {
  if (type.includes('mp4') || type.includes('aac')) return 'm4a'
  if (type.includes('ogg')) return 'ogg'
  return 'webm'
}

function clock(seconds) {
  const s = Math.max(0, Math.floor(seconds))
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`
}

function toFile(blob, name, type) {
  try {
    return new File([blob], name, { type })
  } catch {
    // Juda eski WebView'da `File` konstruktori yo'q — nomli Blob ham `FormData` ga tushadi.
    return Object.assign(blob, { name, lastModified: Date.now() })
  }
}

/**
 * @param disabled      oyna band (yuklanmoqda) yoki fayl uchun joy qolmagan
 * @param onRecorded    `(file, seconds)` — tayyor ovoz fayli
 * @param onError       `(message)` — oynaning xato bannerida ko'rsatiladi
 * @param onRecordingChange `(bool)` — yozish paytida "Bajardim" bosilmasin
 */
export function VoiceRecorder({ disabled, onRecorded, onError, onRecordingChange }) {
  const [phase, setPhase] = useState('idle') // idle | starting | recording
  const [elapsed, setElapsed] = useState(0)
  const [useFallback, setUseFallback] = useState(() => !canRecord())

  const recorderRef = useRef(null)
  const streamRef = useRef(null)
  const timerRef = useRef(null)
  const discardRef = useRef(false)
  const fallbackRef = useRef(null)

  const releaseStream = () => {
    streamRef.current?.getTracks().forEach((track) => track.stop())
    streamRef.current = null
    if (timerRef.current) clearInterval(timerRef.current)
    timerRef.current = null
  }

  // Oyna yopilsa (yoki komponent o'chsa) yozuv TASHLAB yuboriladi va mikrofon bo'shatiladi.
  useEffect(
    () => () => {
      discardRef.current = true
      try {
        if (recorderRef.current && recorderRef.current.state !== 'inactive') recorderRef.current.stop()
      } catch {
        /* jim */
      }
      releaseStream()
    },
    [],
  )

  useEffect(() => {
    onRecordingChange?.(phase !== 'idle')
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phase])

  const stop = () => {
    const rec = recorderRef.current
    if (rec && rec.state !== 'inactive') rec.stop()
  }

  const start = async () => {
    setPhase('starting')
    let stream
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true })
    } catch {
      setPhase('idle')
      setUseFallback(true)
      hapticNotify('error')
      onError(t('voice.denied'))
      return
    }

    try {
      streamRef.current = stream
      const mime = pickMime()
      const rec = mime ? new window.MediaRecorder(stream, { mimeType: mime }) : new window.MediaRecorder(stream)
      const chunks = []
      const startedAt = Date.now()
      rec.ondataavailable = (event) => {
        if (event.data && event.data.size > 0) chunks.push(event.data)
      }
      rec.onstop = () => {
        releaseStream()
        recorderRef.current = null
        if (discardRef.current) return
        const seconds = Math.min(MAX_VOICE_SECONDS, Math.max(1, Math.round((Date.now() - startedAt) / 1000)))
        const type = (rec.mimeType || mime || 'audio/webm').split(';')[0]
        // Belgilar to'plami ko'rinishidagi regex'ni Tailwind CSS klassi deb o'qib, build'da ogohlantirish berardi.
        const stamp = new Date().toISOString().slice(0, 19).replace(/-|:|T/g, '')
        const blob = new Blob(chunks, { type })
        setPhase('idle')
        setElapsed(0)
        if (blob.size === 0) return
        haptic('light')
        onRecorded(toFile(blob, `ovozli-izoh-${stamp}.${extensionOf(type)}`, type), seconds)
      }
      recorderRef.current = rec
      rec.start(1000)
      setElapsed(0)
      setPhase('recording')
      haptic('medium')
      timerRef.current = setInterval(() => {
        const secs = (Date.now() - startedAt) / 1000
        setElapsed(secs)
        if (secs >= MAX_VOICE_SECONDS) stop()
      }, 250)
    } catch {
      releaseStream()
      recorderRef.current = null
      setPhase('idle')
      setUseFallback(true)
      onError(t('voice.denied'))
    }
  }

  /** Zaxira yo'l bilan tanlangan ovoz: uzunligi tekshiriladi. */
  const onFallbackPicked = async (list) => {
    const file = list?.[0]
    if (!file) return
    const seconds = await audioDuration(file)
    if (seconds !== null && seconds > MAX_VOICE_SECONDS + 0.5) {
      hapticNotify('warning')
      onError(t('voice.too_long', { max: MAX_VOICE_SECONDS / 60 }))
      return
    }
    onRecorded(file, seconds === null ? null : Math.round(seconds))
  }

  if (phase === 'recording' || phase === 'starting') {
    return (
      <div className="flex w-full items-center gap-3 rounded-2xl bg-red-50 px-3 py-2">
        <span className="h-2.5 w-2.5 shrink-0 animate-pulse rounded-full bg-red-500" />
        <span className="min-w-0 flex-1 truncate text-[14px] font-semibold tabular-nums text-red-700">
          {t('voice.recording', { time: clock(elapsed), max: formatDuration(MAX_VOICE_SECONDS) })}
        </span>
        <button
          type="button"
          onClick={stop}
          disabled={phase === 'starting'}
          className="inline-flex shrink-0 items-center gap-1.5 rounded-xl bg-red-500 px-3 py-2 text-[14px] font-bold text-white disabled:opacity-50"
        >
          <Square className="h-3.5 w-3.5 fill-current" />
          {t('voice.stop')}
        </button>
      </div>
    )
  }

  return (
    <>
      <Btn
        size="sm"
        disabled={disabled}
        onClick={() => {
          if (useFallback) fallbackRef.current?.click()
          else void start()
        }}
      >
        <span className="inline-flex items-center gap-1.5">
          <Mic className="h-4 w-4" />
          {t('voice.record')}
        </span>
      </Btn>
      <input
        ref={fallbackRef}
        type="file"
        accept="audio/*"
        capture
        hidden
        onChange={(event) => {
          void onFallbackPicked(event.target.files)
          event.target.value = ''
        }}
      />
    </>
  )
}
