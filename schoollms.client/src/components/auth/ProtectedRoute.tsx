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
 */
export function ProtectedRoute({ role }: { role?: Role }) {
  const { isAuthenticated, user } = useAuth()
  const location = useLocation()

  if (!isAuthenticated || !user) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
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
