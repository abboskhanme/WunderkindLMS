import { useRef, useState } from 'react'
import { Upload, X, Loader2 } from 'lucide-react'
import { uploadAdminFile } from '@/api/services/students'
import { Button } from './Button'
import { cn } from '@/lib/utils'

interface Props {
  label: string
  /** Saqlangan rasm URL'i (`/uploads/...`) yoki null */
  value: string | null
  onChange: (url: string | null) => void
}

/** Rasm (profil surati) yuklash maydoni — admin uploads endpoint'iga yuklab, URL'ni qaytaradi. */
export function PhotoUpload({ label, value, onChange }: Props) {
  const ref = useRef<HTMLInputElement | null>(null)
  const [uploading, setUploading] = useState(false)

  const onFile = async (file: File) => {
    setUploading(true)
    try {
      const res = await uploadAdminFile(file)
      onChange(res.url)
    } catch {
      // tarmoq/mock xatosi — sukut bilan o'tkazamiz
    } finally {
      setUploading(false)
    }
  }

  return (
    <div>
      <span className="mb-1 block text-sm font-medium text-slate-600">{label}</span>
      <div className="flex items-start gap-3">
        <div
          className={cn(
            'flex h-20 w-20 shrink-0 items-center justify-center overflow-hidden rounded-lg border border-dashed bg-white',
            value ? 'border-brand-200' : 'border-slate-200 text-slate-300',
          )}
        >
          {uploading ? (
            <Loader2 className="h-5 w-5 animate-spin text-brand-500" />
          ) : value ? (
            <img src={value} alt="" className="h-full w-full object-cover" />
          ) : (
            <Upload className="h-5 w-5" />
          )}
        </div>
        <div className="flex flex-1 flex-col gap-2">
          <input
            ref={ref}
            type="file"
            accept="image/*"
            className="hidden"
            onChange={(e) => {
              const f = e.target.files?.[0]
              if (f) onFile(f)
              if (ref.current) ref.current.value = ''
            }}
          />
          <div className="flex gap-2">
            <Button
              type="button"
              variant="secondary"
              onClick={() => ref.current?.click()}
              disabled={uploading}
            >
              <Upload className="h-4 w-4" /> {value ? 'Yangilash' : 'Rasm yuklash'}
            </Button>
            {value && (
              <Button type="button" variant="danger" onClick={() => onChange(null)}>
                <X className="h-4 w-4" /> O'chirish
              </Button>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
