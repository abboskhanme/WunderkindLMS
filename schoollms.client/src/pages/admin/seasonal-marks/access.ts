import { useAuth } from '@/context/auth-context'
import { SEASONAL_PERM } from './periods'

export interface SeasonalAccess {
  /**
   * Inline edit, delete, "Baholash" and the grid's Saqlash (§3.7). Admin and
   * superadmin carry no `permissions` list and are never restricted; staff and
   * teachers need the `seasonalMarks` key.
   */
  canWrite: boolean
  /**
   * The mark-history dialog reads `GET /api/admin/audit`, which is gated to
   * `admin,superadmin` (`AuditController`). A staff account would get a 403,
   * so the "Tarix" control is not rendered for it.
   */
  canSeeHistory: boolean
}

export function useSeasonalAccess(): SeasonalAccess {
  const { user } = useAuth()
  const canWrite = !user?.permissions || user.permissions.includes(SEASONAL_PERM)
  const canSeeHistory = user?.role === 'admin' || user?.role === 'superadmin'
  return { canWrite, canSeeHistory }
}
