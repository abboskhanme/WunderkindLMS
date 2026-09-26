import type { Role, User } from '@/types'

/** Kassaga kira oladigan rollar (SPEC §4.3, `FinanceAction.AcceptPayment`). */
export const CASH_DESK_ROLES: Role[] = ['cashier', 'admin', 'superadmin']

/** Kassa: yuqoridagi rollar YOKI rolida "Moliya" ruxsati bor xodim (server `Roles.CashierOrAdmin`). */
export function canUseCashDesk(user: User | null): boolean {
  if (!user) return false
  const perms = user.permissions ?? []
  return CASH_DESK_ROLES.includes(user.role) || (user.role === 'staff' && (perms.includes('finance') || perms.includes('finance:view')))
}
