/**
 * E'lon rasmlari (mijoz, 2026-10-24: "e'lon jo'natish qismida xabarlarga rasm qo'shib jo'natish");
 * 2026-10-25 dan avtomatik xabar qoidalarida ham.
 * Har rasm tanlanishi bilan `/admin/uploads` ga yuklanadi; e'lon bilan faqat URL'lar ketadi.
 * Telegram cheklovlari: bitta e'longa 10 tagacha rasm, har biri 10 MB gacha, JPG / PNG / WEBP.
 */
import { useEffect, useRef, useState, type Dispatch, type SetStateAction } from 'react'
import { ImagePlus, Loader2, X } from 'lucide-react'
import { uploadAdminFile } from '@/api/services/students'

export const MAX_BROADCAST_IMAGES = 10
const MAX_BYTES = 10 * 1024 * 1024
const TYPES = ['image/jpeg', 'image/png', 'image/webp']

export function BroadcastImagePicker({
  value,
  onChange,
  onBusyChange,
}: {
  value: string[]
  onChange: Dispatch<SetStateAction<string[]>>
  /** Yuklash davom etayotganda "Yuborish" bosilmasin. */
  onBusyChange: (busy: boolean) => void
}) {
  const fileRef = useRef<HTMLInputElement>(null)
  const [pending, setPending] = useState(0)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => onBusyChange(pending > 0), [pending, onBusyChange])

  const addFiles = async (files: File[]) => {
    setError(null)
    const room = MAX_BROADCAST_IMAGES - value.length - pending
    const accepted = files.filter((f) => TYPES.includes(f.type) && f.size <= MAX_BYTES).slice(0, Math.max(0, room))
    if (accepted.length < files.length) {
      setError(
        files.length > room
          ? `Bitta xabarga ko'pi bilan ${MAX_BROADCAST_IMAGES} ta rasm qo'shiladi.`
          : "Faqat JPG, PNG yoki WEBP rasm, har biri 10 MB gacha.",
      )
    }
    if (accepted.length === 0) return
    setPending((n) => n + accepted.length)
    // Ketma-ket — rasmlar Telegramda tanlangan tartibda chiqsin.
    for (const file of accepted) {
      try {
        const { url } = await uploadAdminFile(file)
        onChange((prev) => [...prev, url])
      } catch {
        setError("Rasmni yuklab bo'lmadi — qaytadan urinib ko'ring.")
      } finally {
        setPending((n) => n - 1)
      }
    }
  }

  const full = value.length + pending >= MAX_BROADCAST_IMAGES

  return (
    <div className="mt-3">
      <div className="flex flex-wrap gap-2">
        {value.map((url, i) => (
          <div key={`${url}-${i}`} className="relative h-20 w-20 overflow-hidden rounded-lg border border-slate-200">
            <img src={url} alt="" className="h-full w-full object-cover" />
            <button
              type="button"
              onClick={() => onChange((prev) => prev.filter((_, j) => j !== i))}
              className="absolute right-1 top-1 rounded-full bg-slate-900/60 p-0.5 text-white hover:bg-slate-900"
              title="Olib tashlash"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          </div>
        ))}
        {Array.from({ length: pending }, (_, i) => (
          <div
            key={`pending-${i}`}
            className="flex h-20 w-20 items-center justify-center rounded-lg border border-dashed border-brand-200"
          >
            <Loader2 className="h-5 w-5 animate-spin text-brand-500" />
          </div>
        ))}
        {!full && (
          <button
            type="button"
            onClick={() => fileRef.current?.click()}
            className="flex h-20 w-20 flex-col items-center justify-center gap-1 rounded-lg border-2 border-dashed border-slate-200 text-slate-400 transition-colors hover:border-brand-300 hover:text-brand-600"
          >
            <ImagePlus className="h-5 w-5" />
            <span className="text-[11px] font-medium">Rasm</span>
          </button>
        )}
      </div>
      <input
        ref={fileRef}
        type="file"
        accept={TYPES.join(',')}
        multiple
        className="hidden"
        onChange={(e) => {
          const files = Array.from(e.target.files ?? [])
          e.target.value = ''
          if (files.length) void addFiles(files)
        }}
      />
      <p className={error ? 'mt-1 text-xs text-red-600' : 'mt-1 text-xs text-slate-400'}>
        {error ?? `Rasm ixtiyoriy: ${MAX_BROADCAST_IMAGES} tagacha, JPG / PNG / WEBP, har biri 10 MB gacha.`}
      </p>
    </div>
  )
}
