/**
 * Chegirmalar — the catalog of discount TYPES ("Chegirma turlari"), nothing else.
 *
 * 2026-09-28 (client): discounts are named, fixed so'm amounts (docs/modules/billing-plans.md §8)
 * attached from the student's profile ("Abonementlar" tab → "Chegirmalar" card) with no approval.
 * The old per-student request list with its director-approval queue is gone from here ("o'quvchi
 * chegirmalar qismi kerakmas"); requests already written stay in the database and keep their rules.
 */
import { BillingGuard } from './BillingUi'
import { DiscountTypesCard } from './DiscountTypesCard'

export function DiscountsPage() {
  return (
    <BillingGuard>
      <div className="space-y-6">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Chegirmalar</h1>
          <p className="text-sm text-slate-400">
            Chegirma turi o'quvchi profilidagi Abonementlar bo'limidan biriktiriladi — tasdiq shart emas.
          </p>
        </div>
        <DiscountTypesCard />
      </div>
    </BillingGuard>
  )
}
