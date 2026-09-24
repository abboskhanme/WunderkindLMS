import type { Role } from '@/types'

/** Kassaga kira oladigan rollar (SPEC §4.3, `FinanceAction.AcceptPayment`). */
export const CASH_DESK_ROLES: Role[] = ['cashier', 'admin', 'superadmin']
