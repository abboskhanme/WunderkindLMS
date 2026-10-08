/**
 * "Chiqish" — sariq hero'ning o'ng burchagida.
 *
 * Tasdiq ilova ICHIDA, pastdan chiqadigan varaqda so'raladi: brauzerning
 * `confirm` oynasi Telegram WebView'da xunuk va ba'zi mijozlarda umuman
 * chiqmaydi. Telegram ichida chiqish hisobni shu Telegram'dan UZADI —
 * matn buni ochiq aytadi, keyingi safar parol so'ralishi kutilmagan bo'lmasin.
 */
import { useState } from 'react'
import { LogOut } from 'lucide-react'
import { useSession } from '../lib/session'
import { haptic, inTelegram } from '../lib/telegram'
import { panelKey } from '../lib/panelKey'

/**
 * `variant="icon"` — hero burchagidagi dumaloq belgi; `variant="block"` —
 * profil ekranining pastidagi to'liq kenglikdagi tugma.
 */
export function LogoutButton({ variant = 'icon' }) {
  const { logout } = useSession()
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  const close = () => {
    if (busy) return
    setOpen(false)
    setError(null)
  }

  const confirm = async () => {
    setBusy(true)
    setError(null)
    try {
      await logout()
      haptic('medium')
    } catch (e) {
      setError(e.message)
      setBusy(false)
    }
  }

  return (
    <>
      <button
        type="button"
        onClick={() => {
          haptic('light')
          setOpen(true)
        }}
        aria-label="Chiqish"
        className={
          variant === 'block'
            ? 'mx-4 mt-3 flex w-[calc(100%-2rem)] items-center justify-center gap-2 rounded-card bg-white py-4 text-[16px] font-semibold text-red-600'
            : 'flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-brand-ink/10 text-brand-ink'
        }
      >
        <LogOut className="h-5 w-5" />
        {variant === 'block' && 'Hisobdan chiqish'}
      </button>

      {open && (
        <div
          role="dialog"
          aria-modal="true"
          aria-label="Hisobdan chiqish"
          onClick={close}
          className="fixed inset-0 z-50 flex items-end bg-black/40"
        >
          <div
            onClick={(e) => e.stopPropagation()}
            className="mx-auto w-full max-w-md rounded-t-[26px] bg-white px-5 pb-[calc(20px+var(--tg-safe-bottom))] pt-5 text-brand-ink"
          >
            <p className="text-[18px] font-bold">Hisobdan chiqasizmi?</p>
            <p className="mt-1.5 text-[14px] leading-relaxed text-slate-500">
              {inTelegram || panelKey
                ? "Shu akkaunt bu Telegram'dan chiqariladi. Boshqa akkaunt ham qo'shilgan bo'lsa, ilova unga o'tadi; bo'lmasa keyingi safar login va parol so'raladi."
                : 'Qayta kirish uchun login va parol kerak bo\'ladi.'}
            </p>
            {error && <p className="mt-3 text-[13px] text-red-600">{error}</p>}
            <button
              type="button"
              onClick={confirm}
              disabled={busy}
              className="mt-5 w-full rounded-2xl bg-red-500 py-3.5 text-[16px] font-bold text-white disabled:opacity-50"
            >
              {busy ? 'Chiqilmoqda…' : 'Chiqish'}
            </button>
            <button
              type="button"
              onClick={close}
              disabled={busy}
              className="mt-2 w-full py-3 text-[15px] font-semibold text-slate-500"
            >
              Bekor qilish
            </button>
          </div>
        </div>
      )}
    </>
  )
}
