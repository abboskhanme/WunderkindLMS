import { useRef, useState } from 'react'
import { ArrowLeft, ImagePlus, Send, CheckCircle2, X } from 'lucide-react'
import SegmentedControl from '../components/SegmentedControl'
import AppButton from '../components/AppButton'
import { FieldLabel } from '../components/ui'
import { api } from '../lib/api'

// Feedback — suggestion/complaint toggle, text area, optional image attach.
export default function FeedbackScreen({ onBack }) {
  const [type, setType] = useState('suggestion')
  const [text, setText] = useState('')
  const [image, setImage] = useState(null)
  const [sending, setSending] = useState(false)
  const [error, setError] = useState(null)
  const [done, setDone] = useState(false)
  const fileRef = useRef(null)

  const submit = async () => {
    const t = text.trim()
    if (!t) {
      setError("Murojaat matnini kiriting")
      return
    }
    setSending(true)
    setError(null)
    try {
      await api.feedback(type, t, image)
      setDone(true)
      setTimeout(() => onBack?.(), 1200)
    } catch (e) {
      setError((e && e.message) || 'Yuborishda xatolik')
    } finally {
      setSending(false)
    }
  }

  if (done) {
    return (
      <div className="h-full flex flex-col bg-bg">
        <div className="flex-1 flex flex-col items-center justify-center p-10 text-center">
          <div className="w-20 h-20 rounded-6xl bg-primary-soft flex items-center justify-center text-primary">
            <CheckCircle2 size={36} />
          </div>
          <p className="mt-4 text-[18px] font-bold text-text">Yuborildi</p>
          <p className="mt-1.5 text-[14px] text-muted">Murojaatingiz adminga yetkazildi</p>
        </div>
      </div>
    )
  }

  return (
    <div className="h-full flex flex-col bg-bg">
      <div className="flex items-center gap-1 px-2 pt-2 pb-1">
        {onBack && (
          <button onClick={onBack} className="w-10 h-10 rounded-xl flex items-center justify-center text-text">
            <ArrowLeft size={20} />
          </button>
        )}
        <div>
          <p className="text-[20px] font-extrabold text-text" style={{ letterSpacing: '-0.025em' }}>Taklif va shikoyatlar</p>
          <p className="text-[12px] text-muted">Adminga murojaat yuboring</p>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto no-scrollbar px-5 pt-2 pb-6">
        <FieldLabel>Murojaat turi</FieldLabel>
        <div className="mt-2">
          <SegmentedControl
            value={type}
            onChange={setType}
            options={[
              { value: 'suggestion', label: 'Taklif' },
              { value: 'complaint', label: 'Shikoyat' },
            ]}
          />
        </div>

        <div className="mt-5">
          <FieldLabel>Murojaat matni</FieldLabel>
          <textarea
            rows={6}
            value={text}
            onChange={(e) => { setText(e.target.value); if (error) setError(null) }}
            placeholder={type === 'complaint' ? 'Muammoni batafsil yozing...' : 'Taklifingizni yozing...'}
            className="mt-2 w-full p-4 rounded-xl bg-surface2 border border-border outline-none text-[15px] text-text placeholder:text-faint focus:border-primary resize-none leading-relaxed"
          />
        </div>

        <div className="mt-5">
          <div className="flex items-center gap-1.5">
            <FieldLabel>Rasm</FieldLabel>
            <span className="text-[12px] text-faint">(ixtiyoriy)</span>
          </div>
          <input
            ref={fileRef}
            type="file"
            accept="image/*"
            className="hidden"
            onChange={(e) => setImage(e.target.files?.[0] || null)}
          />
          {image ? (
            <div className="mt-2 w-full rounded-2xl bg-surface border border-border flex items-center gap-3 p-3">
              <img src={URL.createObjectURL(image)} alt="" className="w-14 h-14 rounded-xl object-cover" />
              <span className="flex-1 min-w-0 text-[13px] text-text truncate">{image.name}</span>
              <button onClick={() => { setImage(null); if (fileRef.current) fileRef.current.value = '' }} className="w-8 h-8 rounded-lg bg-surface2 flex items-center justify-center text-muted">
                <X size={16} />
              </button>
            </div>
          ) : (
            <button onClick={() => fileRef.current?.click()} className="mt-2 w-full h-[110px] rounded-2xl bg-surface border border-border flex flex-col items-center justify-center text-muted">
              <ImagePlus size={28} className="text-faint" />
              <span className="mt-1.5 text-[13px]">Rasm biriktirish</span>
              <span className="text-[11px] text-faint">Kamera yoki galereya · maks 20 MB</span>
            </button>
          )}
        </div>

        {error && <p className="mt-4 text-[13px] font-semibold text-danger">{error}</p>}

        <div className="mt-7">
          <AppButton label="Yuborish" expand height={54} radius={16} loading={sending} leadingIcon={<Send size={18} />} onClick={submit} />
        </div>
      </div>
    </div>
  )
}
