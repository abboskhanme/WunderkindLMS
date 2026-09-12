import { Navigate, Outlet, useLocation } from 'react-router-dom'
import type { Role } from '@/types'
import { useAuth } from '@/context/auth-context'
import { homeByRole } from '@/config/navigation'

/**
 * Marshrutni himoyalaydi:
 *  - kirilmagan bo'lsa -> /login (qaytib kelish uchun manzilni saqlaydi)
 *  - roli mos kelmasa -> o'z bo'limiga yo'naltiradi
 *
 * <c>role="admin"</c> berilganda <c>superadmin</c> ham qabul qilinadi (tizim egasi
 * admin panelidan foydalanadi). Aks holda aniq belgilangan rol talab etiladi.
 *
 * <c>roles</c> — rollarning aniq ro'yxati, hech qanday kengaytmasiz. Kassa uchun
 * kerak: u yerga <c>cashier</c> kirishi, <c>staff</c> esa kirmasligi lozim, ya'ni
 * "admin" darvozasi aynan teskari ishlaydi (SPEC §4.3).
 */
export function ProtectedRoute({ role, roles }: { role?: Role; roles?: Role[] }) {
  const { isAuthenticated, user } = useAuth()
  const location = useLocation()

  if (!isAuthenticated || !user) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  if (roles && !roles.includes(user.role)) {
    return <Navigate to={homeByRole[user.role]} replace />
  }

  if (role) {
    // "admin" darvozasi: admin + superadmin + xodim (staff). Xodimning ko'radigan bo'limlari
    // nav filtri (Sidebar) va route RequirePerm bilan cheklanadi.
    const allowed = role === 'admin' ? ['admin', 'superadmin', 'staff'] : [role]
    if (!allowed.includes(user.role)) {
      return <Navigate to={homeByRole[user.role]} replace />
    }
  }

  return <Outlet />
}

/** Ildiz (/) va noma'lum manzillar uchun: rolga qarab bosh sahifa yoki login. */
export function RootRedirect() {
  const { isAuthenticated, user } = useAuth()
  return <Navigate to={isAuthenticated && user ? homeByRole[user.role] : '/login'} replace />
}
