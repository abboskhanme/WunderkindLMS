import { Navigate } from 'react-router-dom'
import { useAuth } from '@/context/auth-context'
import { navByRole } from '@/config/navigation'
import { canSeeNav, pageLevel } from '@/lib/access'
import { AdminDashboard } from '@/pages/admin/AdminDashboard'

/**
 * `/admin` kirish nuqtasi. Bosh sahifa ham ruxsat bilan (mijoz, 2026-09-24): "bazilarga u sahifa uchun ham
 * dostup bo'lmaydi". `dashboard` ruxsati yo'q xodim o'ziga ochiq BIRINCHI menyu bo'limiga o'tkaziladi —
 * Sidebar'dagi bilan bir xil qoida (`perm` bo'lmasa hammaga ochiq).
 */
export function AdminHome() {
  const { user } = useAuth()
  if (!user || user.role !== 'staff' || pageLevel(user, '/admin') !== 'none') return <AdminDashboard />

  // Sidebar bilan AYNAN bir xil qoida (lib/access.ts `canSeeNav`). `/admin` ning o'zi bu yerda
  // yo'q — aks holda cheksiz aylanish bo'lardi (2026-09-25).
  const first = navByRole.admin
    .filter((x) => canSeeNav(user, x))
    .flatMap((item) => (item.children ? item.children.filter((c) => canSeeNav(user, c)) : [item]))
    .find((x) => x.to !== '/admin' && x.to.startsWith('/admin'))
  if (first) return <Navigate to={first.to} replace />

  return (
    <div className="flex flex-col items-center gap-2 py-24 text-center">
      <p className="font-medium text-slate-700">Sizga hali bo'lim ochilmagan</p>
      <p className="max-w-sm text-sm text-slate-500">Tizim egasi (superadmin) Boshqaruv → Rollar orqali sizga rol biriktirgach, bo'limlar shu yerda paydo bo'ladi.</p>
    </div>
  )
}
