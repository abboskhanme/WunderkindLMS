import type { ReactNode } from 'react'
import { useAuth } from '@/context/auth-context'
import { Card } from '@/components/ui/Card'

/**
 * O'qituvchi bo'limlari uchun ruxsat darvozasi. Foydalanuvchi permissions ro'yxatida shu bo'lim
 * bo'lmasa — "ruxsat yo'q" ko'rsatadi. permissions umuman bo'lmasa (admin) — har doim ochiq.
 *
 * `perm` ro'yxat bo'lsa — birortasi yetarli (sahifa bir necha bo'lim ma'lumotini ko'rsatganda,
 * masalan Boshqaruv → Xodimlar: `staff` yoki `teachers`; sahifa o'zi ruxsati yo'q qismini yashiradi).
 */
export function RequirePerm({ perm, children }: { perm: string | readonly string[]; children: ReactNode }) {
  const { user } = useAuth()
  const keys: readonly string[] = typeof perm === 'string' ? [perm] : perm
  // Bo'lim kaliti: to'liq (`finance`) yoki faqat ko'rish (`finance:view`) — sahifa ochiladi.
  const ok =
    !user?.permissions ||
    keys.some((k) => user.permissions?.includes(k) || user.permissions?.includes(`${k}:view`))
  if (!ok) {
    return (
      <Card>
        <p className="py-12 text-center text-slate-400">Bu bo'limga ruxsatingiz yo'q.</p>
      </Card>
    )
  }
  return <>{children}</>
}
