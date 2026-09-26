import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import { Eye } from 'lucide-react'
import { useAuth } from '@/context/auth-context'
import { levelForPath } from '@/lib/access'
import { Card } from '@/components/ui/Card'
import { Toast } from '@/components/ui/Toast'

/** `api/client.ts` "faqat ko'rish" xodimining yozish urinishi rad etilganda shu hodisani yuboradi. */
export const READ_ONLY_EVENT = 'access:readonly'

/**
 * Sahifa darajasidagi ruxsat (Boshqaruv → Rollar, 2026-09-26) — barcha admin sahifalari
 * uchun bitta joyda:
 *  - ruxsat yo'q  → sahifa o'rniga "ruxsat yo'q";
 *  - faqat ko'rish → sahifa ochiladi, tepada belgi; o'zgartirish tugmalari turadi, lekin
 *    bosilganda server rad etadi va shu yerda tushunarli xabar chiqadi (mijoz tanlovi).
 */
export function PageAccessGate({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const { pathname, search } = useLocation()
  const [denied, setDenied] = useState<string | null>(null)
  const close = useCallback(() => setDenied(null), [])

  useEffect(() => {
    const onDenied = () => setDenied("Sizda bu bo'limda faqat ko'rish huquqi bor — o'zgartirish saqlanmadi.")
    window.addEventListener(READ_ONLY_EVENT, onDenied)
    return () => window.removeEventListener(READ_ONLY_EVENT, onDenied)
  }, [])

  const guarded = pathname.startsWith('/admin') || pathname.startsWith('/cashier')
  const level = guarded ? levelForPath(user, pathname, search) : 'edit'

  if (level === 'none') {
    return (
      <Card>
        <p className="py-12 text-center text-slate-400">Bu sahifaga ruxsatingiz yo'q.</p>
      </Card>
    )
  }

  return (
    <>
      {level === 'view' && (
        <div className="mb-4 flex items-center gap-2 rounded-xl border border-amber-200 bg-amber-50 px-4 py-2 text-sm text-amber-800">
          <Eye className="h-4 w-4 shrink-0" />
          Faqat ko'rish — bu sahifada o'zgartirish kiritib bo'lmaydi.
        </div>
      )}
      {children}
      <Toast message={denied} onClose={close} tone="error" duration={4000} />
    </>
  )
}
