import { Navigate } from 'react-router-dom'
import { useAuth } from '@/context/auth-context'
import { navByRole } from '@/config/navigation'
import { AdminDashboard } from '@/pages/admin/AdminDashboard'

/**
 * `/admin` kirish nuqtasi. Bosh sahifa ham ruxsat bilan (mijoz, 2026-09-24): "bazilarga u sahifa uchun ham
 * dostup bo'lmaydi". `dashboard` ruxsati yo'q xodim o'ziga ochiq BIRINCHI menyu bo'limiga o'tkaziladi —
 * Sidebar'dagi bilan bir xil qoida (`perm` bo'lmasa hammaga ochiq).
 */
export function AdminHome() {
  const { user } = useAuth()
  const perms = user?.permissions
  if (!user || user.role !== 'staff' || !perms || perms.includes('dashboard')) return <AdminDashboard />

  const allowed = (x: { perm?: string }) => !x.perm || perms.includes(x.perm)
  const first = navByRole.admin
    .flatMap((item) => (item.children ? item.children.filter(allowed) : allowed(item) ? [item] : []))
    .find((x) => x.to !== '/admin')
  if (first) return <Navigate to={first.to} replace />

  return (
    <div className="flex flex-col items-center gap-2 py-24 text-center">
      <p className="font-medium text-slate-700">Sizga hali bo'lim ochilmagan</p>
      <p className="max-w-sm text-sm text-slate-500">Tizim egasi (superadmin) Xodimlar va rollar bo'limida ruxsat bergach, bo'limlar shu yerda paydo bo'ladi.</p>
    </div>
  )
}
