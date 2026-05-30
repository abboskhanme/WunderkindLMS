import type { ReactNode } from 'react'
import { useAuth } from '@/context/auth-context'
import { Card } from '@/components/ui/Card'

/**
 * O'qituvchi bo'limlari uchun ruxsat darvozasi. Foydalanuvchi permissions ro'yxatida shu bo'lim
 * bo'lmasa — "ruxsat yo'q" ko'rsatadi. permissions umuman bo'lmasa (admin) — har doim ochiq.
 */
export function RequirePerm({ perm, children }: { perm: string; children: ReactNode }) {
  const { user } = useAuth()
  const ok = !user?.permissions || user.permissions.includes(perm)
  if (!ok) {
    return (
      <Card>
        <p className="py-12 text-center text-slate-400">Bu bo'limga ruxsatingiz yo'q.</p>
      </Card>
    )
  }
  return <>{children}</>
}
