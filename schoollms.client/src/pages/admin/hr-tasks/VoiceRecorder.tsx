import { useEffect, useRef, useState } from 'react'
import { Loader2, Mic, Paperclip, Square, Trash2 } from 'lucide-react'
import { deleteAttachment, hrErrorMessage, uploadAttachment } from '@/api/services/hrTasks'
import type { HrTaskAttachment } from '@/types/hrTasks'
import { HR_MAX_VOICE_SECONDS, HR_VOICE_ACCEPT } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { hrAttachmentFileError, hrMediaSrc, hrVoiceDuration } from '@/components/hr-tasks/lib'
import { cn } from '@/lib/utils'

/**
 * Voice note: record from the microphone OR pick a file (the old `VoiceRecorder`). The file
 * uploads IMMEDIATELY (`POST /attachments`) and returns a DRAFT — one voice note sent to 30
 * employees is stored once.
 *
 * The microphone needs a secure context (HTTPS/localhost); otherwise the file path remains.
 * MIME order: `audio/mp4` (Safari) → `audio/webm;codecs=opus` → browser default.
 */
const VOICE_MIME_CANDIDATES = ['audio/mp4', 'audio/webm;codecs=opus', 'audio/webm'] as const

const NO_MIC_HINT = 'Mikrofon faqat HTTPS yoki localhost sahifada ishlaydi. Ovozni fayl qilib yuklang.'

function pickVoiceMime(): string | undefined {
  if (typeof MediaRecorder === 'undefined') return undefined
  return VOICE_MIME_CANDIDATES.find((type) => MediaRecorder.isTypeSupported(type))
}

function micErrorMessage(error: unknown): string {
  const name = typeof error === 'object' && error !== null ? (error as { name?: string }).name : ''
  if (name === 'NotAllowedError' || name === 'SecurityError') {
    return 'Mikrofonga ruxsat berilmadi. Manzil qatoridagi qulf belgisini bosib ruxsat bering.'
  }
  if (name === 'NotFoundError' || name === 'DevicesNotFoundError') return 'Mikrofon topilmadi — qurilma ulanganini tekshiring.'
  if (name === 'NotReadableError') return 'Mikrofonni boshqa dastur band qilgan. Uni yopib qayta urining.'
  return 'Mikrofonni ochib bo‘lmadi. Ovozni fayl qilib yuklang.'
}

function clock(seconds: number): string {
  return hrVoiceDuration(seconds) ?? '0:00'
}

export function VoiceRecorder({
  value,
  onChange,
  disabled = false,
}: {
  value: HrTaskAttachment | null
  onChange: (attachment: HrTaskAttachment | null) => void
  disabled?: boolean
}) {
  const [phase, setPhase] = useState<'idle' | 'recording' | 'uploading'>('idle')
  const [seconds, setSeconds] = useState(0)
  const [percent, setPercent] = useState(0)
  const [error, setError] = useState<string | null>(null)

  const recorderRef = useRef<MediaRecorder | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const chunksRef = useRef<Blob[]>([])
  const timerRef = useRef<number | null>(null)
  const secondsRef = useRef(0)
  const fileInputRef = useRef<HTMLInputElement | null>(null)

  const micReady =
    typeof MediaRecorder !== 'undefined' && typeof navigator !== 'undefined' && Boolean(navigator.mediaDevices?.getUserMedia)

  function clearTimer(): void {
    if (timerRef.current !== null) {
      window.clearInterval(timerRef.current)
      timerRef.current = null
    }
  }

  function stopStream(): void {
    streamRef.current?.getTracks().forEach((track) => track.stop())
    streamRef.current = null
  }

  // Leaving the page or removing the row must not leave the microphone on.
  useEffect(
    () => () => {
      if (timerRef.current !== null) window.clearInterval(timerRef.current)
      const recorder = recorderRef.current
      if (recorder && recorder.state !== 'inactive') recorder.stop()
      streamRef.current?.getTracks().forEach((track) => track.stop())
    },
    [],
  )

  async function sendBlob(blob: Blob, filename?: string): Promise<void> {
    if (blob.size === 0) {
      setError('Ovoz yozilmadi — mikrofon jim qoldi.')
      setPhase('idle')
      return
    }
    const fileError = hrAttachmentFileError({ size: blob.size, type: blob.type })
    if (fileError) {
      setError(fileError)
      setPhase('idle')
      return
    }
    setPhase('uploading')
    setPercent(0)
    try {
      const attachment = await uploadAttachment({ file: blob, filename, kind: 'voice', onProgress: setPercent })
      onChange(attachment)
      setError(null)
    } catch (err) {
      setError(hrErrorMessage(err))
    } finally {
      setPhase('idle')
    }
  }

  function stopRecording(): void {
    clearTimer()
    const recorder = recorderRef.current
    if (recorder && recorder.state !== 'inactive') {
      // Upload happens in `onstop` — the last chunk arrives after `stop()`.
      recorder.stop()
      return
    }
    stopStream()
    setPhase('idle')
  }

  async function startRecording(): Promise<void> {
    setError(null)
    if (!micReady) {
      setError(NO_MIC_HINT)
      return
    }
    let stream: MediaStream
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true })
    } catch (mediaError) {
      setError(micErrorMessage(mediaError))
      return
    }
    const mimeType = pickVoiceMime()
    let recorder: MediaRecorder
    try {
      recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined)
    } catch {
      stream.getTracks().forEach((track) => track.stop())
      setError('Bu brauzer ovoz yozishni qo‘llab-quvvatlamaydi. Faylni yuklang.')
      return
    }

    chunksRef.current = []
    recorder.ondataavailable = (event) => {
      if (event.data.size > 0) chunksRef.current.push(event.data)
    }
    recorder.onstop = () => {
      const type = recorder.mimeType || mimeType || 'audio/webm'
      const blob = new Blob(chunksRef.current, { type })
      chunksRef.current = []
      stopStream()
      void sendBlob(blob)
    }

    recorderRef.current = recorder
    streamRef.current = stream
    secondsRef.current = 0
    setSeconds(0)
    setPhase('recording')
    recorder.start()

    timerRef.current = window.setInterval(() => {
      secondsRef.current += 1
      setSeconds(secondsRef.current)
      // Stops by itself at the limit.
      if (secondsRef.current >= HR_MAX_VOICE_SECONDS) stopRecording()
    }, 1000)
  }

  async function handleDelete(): Promise<void> {
    const current = value
    onChange(null)
    setError(null)
    if (!current) return
    try {
      await deleteAttachment(current.id)
    } catch {
      // A leftover draft on the server is never bound to a task — harmless.
    }
  }

  function handleFile(file: File | undefined): void {
    if (!file) return
    const fileError = hrAttachmentFileError(file)
    if (fileError) {
      setError(fileError)
      return
    }
    void sendBlob(file, file.name)
  }

  const busy = disabled || phase === 'uploading'
  const nearLimit = seconds >= HR_MAX_VOICE_SECONDS - 30

  if (value) {
    const duration = hrVoiceDuration(value.durationSeconds)
    return (
      <div className="flex flex-wrap items-center gap-2">
        <audio className="h-10 min-w-0 max-w-full flex-1" controls preload="metadata" src={hrMediaSrc(value.url) ?? undefined} />
        {duration && <span className="shrink-0 text-xs tabular-nums text-slate-400">{duration}</span>}
        <Button type="button" variant="ghost" className="px-3" disabled={disabled} onClick={() => void handleDelete()}>
          <Trash2 className="h-4 w-4" />
          O‘chirish
        </Button>
      </div>
    )
  }

  return (
    <div className="space-y-1.5">
      <div className="flex flex-wrap items-center gap-2">
        {phase === 'recording' ? (
          <Button type="button" variant="danger" className="px-3" onClick={stopRecording}>
            <Square className="h-4 w-4" />
            To‘xtatish
          </Button>
        ) : (
          <Button
            type="button"
            variant="secondary"
            className="px-3"
            disabled={busy || !micReady}
            onClick={() => void startRecording()}
          >
            {phase === 'uploading' ? <Loader2 className="h-4 w-4 animate-spin" /> : <Mic className="h-4 w-4" />}
            {phase === 'uploading' ? `Yuklanmoqda ${percent}%` : 'Ovozli izoh'}
          </Button>
        )}

        {phase === 'recording' ? (
          <span role="status" className={cn('shrink-0 text-sm font-medium tabular-nums', nearLimit ? 'text-red-600' : 'text-slate-700')}>
            {clock(seconds)} / {clock(HR_MAX_VOICE_SECONDS)}
          </span>
        ) : (
          <>
            <Button type="button" variant="ghost" className="px-3" disabled={busy} onClick={() => fileInputRef.current?.click()}>
              <Paperclip className="h-4 w-4" />
              Fayl yuklash
            </Button>
            <input
              ref={fileInputRef}
              type="file"
              className="hidden"
              accept={HR_VOICE_ACCEPT}
              onChange={(event) => {
                handleFile(event.target.files?.[0])
                event.target.value = ''
              }}
            />
          </>
        )}
      </div>
      {error ? (
        <p className="text-xs text-red-600" role="alert">
          {error}
        </p>
      ) : !micReady ? (
        <p className="text-xs text-slate-400">{NO_MIC_HINT}</p>
      ) : null}
    </div>
  )
}
