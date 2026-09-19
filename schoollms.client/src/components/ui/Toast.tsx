import { useEffect } from 'react'
import { createPortal } from 'react-dom'
import { CheckCircle2, X } from 'lucide-react'
import { cn } from '@/lib/utils'

/* ==========================================================================
   Xabar oynachasi (toast)

   Mijoz, 2026-09-19: "saqlangandan keyin alert ham kelsin yashil qandeydir
   saqlanganini bildiruvchi."

   NEGA ALOHIDA KOMPONENT: "saqlandi" xabari bitta ekranga emas, hammasiga
   kerak bo'ladi — shuning uchun u shu yerda, umumiy ko'rinishda turadi
   (`Modal`, `Card` kabi). Kutubxona qo'shilmadi: bitta kichik oynacha uchun
   yangi bog'liqlik ortiqcha.

   PORTAL ORQALI: sahifaning istalgan joyidan chaqirilsa ham, oynacha eng
   ustida (modal ham, yon menyu ham ustini yopmaydi) va ota-elementning
   `overflow` i uni qirqib tashlamaydi.

   O'ZI YO'QOLADI: `duration` (sukut 3 soniya) tugagach yopiladi, lekin
   xochcha ham bor — xabarni o'qib ulgurmaganlar uchun.

   JOYI EKRANGA QARAB (mijoz, 2026-09-19: "telefon holatida bu alert o'rtada
   tepadan chiqishi kerak pasdan emas", "kompyuterda esa tepa o'ngdan
   chiqishi kerak"):
     · telefon — TEPADA, o'rtada;
     · kompyuter — TEPADA, o'ngda.
   Ikkalasida ham yuqorida: pastdagi oynacha "Saqlash" tugmasi va boshqa
   boshqaruvlarni to'sib qo'yadi.
   ========================================================================== */

export interface ToastProps {
  /** Ko'rsatiladigan matn. `null` — oynacha yopiq. */
  message: string | null
  onClose: () => void
  /** Necha millisekunddan keyin o'zi yo'qolsin (0 — yo'qolmasin). */
  duration?: number
  tone?: 'success' | 'error'
}

export function Toast({ message, onClose, duration = 3000, tone = 'success' }: ToastProps) {
  useEffect(() => {
    if (!message || duration <= 0) return
    const timer = setTimeout(onClose, duration)
    return () => clearTimeout(timer)
  }, [message, duration, onClose])

  if (!message) return null

  return createPortal(
    <div
      role="status"
      aria-live="polite"
      className={cn(
        // Telefon: tepada o'rtada, ekran enidan chetlari bilan.
        // `sm:` dan boshlab: tepada o'ngda.
        'toast-in fixed left-1/2 top-4 z-[80] w-[calc(100%-2rem)] max-w-sm -translate-x-1/2',
        'sm:left-auto sm:right-6 sm:top-6 sm:w-auto sm:translate-x-0',
        'flex items-start gap-3 rounded-xl border px-4 py-3 shadow-lg',
        tone === 'success'
          ? 'border-emerald-200 bg-emerald-50 text-emerald-800 shadow-emerald-900/5'
          : 'border-red-200 bg-red-50 text-red-800 shadow-red-900/5',
      )}
    >
      <CheckCircle2
        className={cn('mt-0.5 h-5 w-5 shrink-0', tone === 'success' ? 'text-emerald-500' : 'text-red-500')}
      />
      <span className="text-sm font-medium">{message}</span>
      <button
        type="button"
        onClick={onClose}
        aria-label="Yopish"
        className={cn(
          'rounded-md p-0.5 transition-colors',
          tone === 'success' ? 'hover:bg-emerald-100' : 'hover:bg-red-100',
        )}
      >
        <X className="h-4 w-4" />
      </button>
    </div>,
    document.body,
  )
}
