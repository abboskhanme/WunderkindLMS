import { Navigate } from 'react-router-dom'
import type { Role } from '@/types'
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

  // Sidebar bilan AYNAN bir xil qoida: bo'limning O'Z ruxsati va roli ham, bolalarniki ham. Ilgari bo'lim ruxsati
  // (masalan Moliya — `finance`) hisobga olinmasdi va xodim `/cashier` ga yuborilardi, u yerdan esa yana `/admin`
  // ga qaytarilardi — cheksiz aylanish (Telegram'da "yonib-o'chish", 2026-09-25).
  const allowed = (x: { perm?: string; roles?: Role[] }) =>
    (!x.roles || x.roles.includes(user.role)) && (!x.perm || perms.includes(x.perm))
  const first = navByRole.admin
    .filter(allowed)
    .flatMap((item) => (item.children ? item.children.filter(allowed) : [item]))
    // Faqat AYNAN berilgan ruxsatli bo'lim: ruxsati yozilmagan menyu bandining sahifasi (masalan Dars jadvali)
    // baribir ruxsat so'rashi va `/admin` ga qaytarishi mumkin — bu yana aylanish bo'lardi.
    .find((x) => !!x.perm && perms.includes(x.perm) && x.to !== '/admin' && x.to.startsWith('/admin'))
  if (first) return <Navigate to={first.to} replace />

  return (
    <div className="flex flex-col items-center gap-2 py-24 text-center">
      <p className="font-medium text-slate-700">Sizga hali bo'lim ochilmagan</p>
      <p className="max-w-sm text-sm text-slate-500">Tizim egasi (superadmin) Xodimlar va rollar bo'limida ruxsat bergach, bo'limlar shu yerda paydo bo'ladi.</p>
    </div>
  )
}
