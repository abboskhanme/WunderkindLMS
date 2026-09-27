import { getAccess } from '@/api/services/hrTasks'
import { useAuth } from '@/context/auth-context'
import { useLoad } from '@/components/hr-tasks/useLoad'

/**
 * Is the current user an HR DIRECTOR (docs/modules/hr-tasks.md §3)? admin/superadmin —
 * always; anyone else — per `GET /api/admin/hr/access` (`isDirector`). The positions registry
 * and HR settings are for directors only. UI only — the server is the real gate.
 */
export function useIsHrDirector(): boolean {
  const { user } = useAuth()
  const privileged = user?.role === 'admin' || user?.role === 'superadmin'
  const access = useLoad(!user || privileged ? null : `hr-access|${user.id}`, getAccess)
  if (privileged) return true
  return access.data?.isDirector === true
}
