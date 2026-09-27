/**
 * "Bajardim" oynasi: isbot (fayl, ovoz va/yoki izoh) bilan topshiriqni yopish
 * (eski `components/CompleteSheet.tsx`).
 *
 * QOIDA: isbot HAR topshiriq uchun majburiy, lekin ikki xil va TENG KUCHDA —
 * (a) kamida bitta fayl (istalgan tur; ovozli izoh ham fayl), yoki
 * (b) bo'sh bo'lmagan izoh. `task.proofRequired` ga QARALMAYDI.
 *
 * Oqim: har fayl `POST /hr/me/proofs` → `{id}`; keyin yig'ilgan id'lar bilan
 * `POST /hr/me/tasks/{id}/complete`. Yuklangan faylning id'si SAQLANADI —
 * ikkinchi bosqichda xato bo'lsa "Qayta urinish" 10 MB rasmni qayta yuklamaydi.
 *
 * Fayl tanlash ikkita: "Suratga olish" (`capture`) va "Fayl tanlash" (`multiple`,
 * `accept` ATAYIN yo'q). Bitta `input` da `capture` bilan `multiple` birga
 * ishlamaydi — ba'zi mijozlar galereyani umuman ochmaydi.
 */
import { useRef, useState } from 'react'
import { Camera, Check, Mic, Paperclip, X } from 'lucide-react'
import { MAX_PROOFS, MAX_PROOF_BYTES, formatDuration, toMegabytes, validateProofFile } from '../../lib/hrFormat'
import { hrApi, hrErrorMessage } from '../../lib/hrApi'
import { hapticNotify } from '../../lib/telegram'
import { BottomSheet, Btn, FieldLabel, Hint, INPUT_CLASS, Notice } from './shared'
import { VoiceRecorder } from './VoiceRecorder'
import { t } from './texts'

let pickCounter = 0

export function CompleteSheet({ task, onSubmit, initialComment = '', initialError = null, onClose }) {
  const [comment, setComment] = useState(initialComment)
  const [picked, setPicked] = useState([])
  const [error, setError] = useState(initialError)
  const [phase, setPhase] = useState('idle') // idle | uploading | saving
  const [recording, setRecording] = useState(false)

  const cameraRef = useRef(null)
  const fileRef = useRef(null)

  const busy = phase !== 'idle'
  const uploadedCount = picked.filter((p) => p.proofId !== null).length
  const trimmedComment = comment.trim()

  /**
   * Topshiriqda ALLAQACHON turgan isbotlar (belgilash qaytarib olingandan keyin
   * saqlanib qoladi) ham hisobga olinadi — server ham sonni BAZADAN o'qiydi.
   */
  const attached = (task.proofs || []).length
  const room = Math.max(0, MAX_PROOFS - attached - picked.length)

  const hasProof = attached > 0 || picked.length > 0 || trimmedComment.length > 0
  const canSubmit = hasProof && !recording

  /** Tekshiruv `setPicked` ichida EMAS — React yangilagichni ikki marta chaqirishi mumkin. */
  const addFiles = (list, voiceSeconds) => {
    if (!list || list.length === 0) return
    const accepted = []
    let problem = null
    let free = room
    for (const file of Array.from(list)) {
      if (free <= 0) {
        problem = t('complete.too_many', { max: MAX_PROOFS })
        break
      }
      const invalid = validateProofFile(file)
      if (invalid !== null) {
        problem = invalid
        continue
      }
      pickCounter += 1
      accepted.push({ key: `p${pickCounter}`, file, proofId: null, percent: 0, voiceSeconds })
      free -= 1
    }
    setError(problem)
    if (problem !== null) hapticNotify('warning')
    if (accepted.length > 0) setPicked((current) => [...current, ...accepted])
  }

  const removeFile = (key) => {
    if (busy) return
    setPicked((current) => current.filter((p) => p.key !== key))
  }

  const patchPicked = (key, patch) =>
    setPicked((current) => current.map((p) => (p.key === key ? { ...p, ...patch } : p)))

  const submit = async () => {
    if (busy) return
    if (recording) {
      setError(t('voice.stop_first'))
      hapticNotify('warning')
      return
    }
    if (!hasProof) {
      setError(t('complete.no_proof'))
      hapticNotify('warning')
      return
    }
    setError(null)

    // ── 1-bosqich: fayllar
    const ids = []
    if (picked.length > 0) {
      setPhase('uploading')
      for (const item of picked) {
        if (item.proofId !== null) {
          ids.push(item.proofId)
          continue
        }
        try {
          const uploaded = await hrApi.uploadProof(item.file, task.id, (percent) => patchPicked(item.key, { percent }))
          patchPicked(item.key, { proofId: uploaded.id, percent: 100 })
          ids.push(uploaded.id)
        } catch (uploadError) {
          setPhase('idle')
          setError(hrErrorMessage(uploadError))
          hapticNotify('error')
          return
        }
      }
    }

    // ── 2-bosqich: belgilash
    setPhase('saving')
    try {
      await onSubmit({ comment: trimmedComment.length > 0 ? trimmedComment : null, proofIds: ids }, picked.length > 0)
    } catch (saveError) {
      // Faylsiz (optimistik) holatda bu oyna allaqachon yopilgan — xato qayta
      // ochilgan oynaga `initialError` orqali keladi.
      setPhase('idle')
      setError(hrErrorMessage(saveError))
      hapticNotify('error')
    }
  }

  const buttonLabel =
    phase === 'uploading'
      ? t('complete.uploading', { done: uploadedCount, total: picked.length })
      : phase === 'saving'
        ? t('common.sending')
        : t('complete.submit')

  const footer = (
    <>
      {!hasProof && <p className="mb-2 text-center text-[13px] font-medium text-slate-500">{t('complete.proof_rule')}</p>}
      <Btn variant="primary" block loading={busy} disabled={!canSubmit} onClick={() => void submit()}>
        {buttonLabel}
      </Btn>
    </>
  )

  return (
    <BottomSheet title={t('complete.title')} busy={busy} onClose={onClose} footer={footer}>
      <p className="break-words rounded-2xl bg-slate-50 px-3.5 py-2.5 text-[15px] font-semibold leading-snug">{task.title}</p>

      {error !== null && (
        <Notice tone="danger" onDismiss={() => setError(null)} className="mt-3">
          {error}
        </Notice>
      )}

      <FieldLabel htmlFor="hr-complete-comment">{t('complete.comment_label')}</FieldLabel>
      <textarea
        id="hr-complete-comment"
        rows={4}
        maxLength={2000}
        placeholder={t('complete.comment_placeholder')}
        value={comment}
        disabled={busy}
        onChange={(e) => {
          setComment(e.target.value)
          setError(null)
        }}
        className={INPUT_CLASS + ' resize-none leading-snug'}
      />

      <FieldLabel>{t('complete.proof_label')}</FieldLabel>
      <Hint className="mt-0">{t('complete.proof_hint', { max: MAX_PROOFS, size: toMegabytes(MAX_PROOF_BYTES) })}</Hint>
      {attached > 0 && <Hint>{t('complete.already_attached', { count: attached })}</Hint>}

      {picked.length > 0 && (
        <ul className="mt-3 space-y-2">
          {picked.map((item) => {
            const isVoice = item.voiceSeconds !== undefined
            const done = item.proofId !== null
            const Icon = done ? Check : isVoice ? Mic : Paperclip
            const voiceLabel = formatDuration(item.voiceSeconds)
            return (
              <li key={item.key} className="rounded-xl bg-slate-50 px-3 py-2.5">
                <div className="flex items-center gap-2">
                  <Icon className={'h-4 w-4 shrink-0 ' + (done ? 'text-emerald-600' : 'text-slate-500')} />
                  <span className="min-w-0 flex-1 truncate text-[14px] font-medium">
                    {isVoice ? t('voice.file_name', { duration: voiceLabel ?? '—' }) : item.file.name}
                  </span>
                  <span className="shrink-0 text-[12px] tabular-nums text-slate-400">{toMegabytes(item.file.size)} MB</span>
                  <button
                    type="button"
                    aria-label={t('complete.remove_file')}
                    disabled={busy}
                    onClick={() => removeFile(item.key)}
                    className="-mr-1 shrink-0 rounded-full p-1 text-slate-400 disabled:opacity-40"
                  >
                    <X className="h-4 w-4" />
                  </button>
                </div>
                {(phase === 'uploading' || done) && (
                  <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-slate-200">
                    <div
                      className={'h-full rounded-full transition-[width] ' + (done ? 'bg-emerald-500' : 'bg-brand')}
                      style={{ width: `${done ? 100 : item.percent}%` }}
                    />
                  </div>
                )}
              </li>
            )
          })}
        </ul>
      )}

      <div className="mt-3 flex flex-wrap gap-2">
        {!recording && (
          <>
            <Btn size="sm" disabled={busy || room === 0} onClick={() => cameraRef.current?.click()}>
              <span className="inline-flex items-center gap-1.5">
                <Camera className="h-4 w-4" />
                {t('complete.take_photo')}
              </span>
            </Btn>
            <Btn size="sm" disabled={busy || room === 0} onClick={() => fileRef.current?.click()}>
              <span className="inline-flex items-center gap-1.5">
                <Paperclip className="h-4 w-4" />
                {t('complete.pick_file')}
              </span>
            </Btn>
          </>
        )}
        <VoiceRecorder
          disabled={busy || room === 0}
          onRecorded={(file, seconds) => addFiles([file], seconds ?? null)}
          onError={(message) => setError(message)}
          onRecordingChange={setRecording}
        />
      </div>

      {/* Ikkala `input` ham yashirin; `value` har tanlovdan keyin tozalanadi —
          bir xil faylni ikkinchi marta tanlaganda ham `change` ishlasin. */}
      <input
        ref={cameraRef}
        type="file"
        accept="image/*"
        capture="environment"
        hidden
        onChange={(e) => {
          addFiles(e.target.files)
          e.target.value = ''
        }}
      />
      <input
        ref={fileRef}
        type="file"
        multiple
        hidden
        onChange={(e) => {
          addFiles(e.target.files)
          e.target.value = ''
        }}
      />
    </BottomSheet>
  )
}
