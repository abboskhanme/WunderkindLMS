import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { ArrowLeft, Lock } from 'lucide-react'
import { adminSeasonalEntryApi } from '@/api/services/seasonalMarks'
import { SeasonalEntryWorkspace, type SeasonalEntryInitial } from './SeasonalEntryWorkspace'
import { useSeasonalAccess } from './access'
import { periodFromParams } from './periods'

/**
 * BAHOLASH (KIRITISH) — `/admin/seasonal-marks/entry`, screen 11 of
 * `docs/modules/admission-and-testing.md` §3.3. Reached from the list's
 * "Baholash" button, not from the menu (§3.6).
 *
 * The list may hand over its filters in the query string
 * (`periodKind, year, month, quarter, classId, subjectId`); they are read once.
 */
export function SeasonalEntryPage() {
  const [params] = useSearchParams()
  const { canWrite } = useSeasonalAccess()
  const [initial] = useState<SeasonalEntryInitial>(() => ({
    period: periodFromParams(params),
    classId: params.get('classId') ?? undefined,
    subjectId: params.get('subjectId') ?? undefined,
  }))

  return (
    <div className="space-y-6">
      <div>
        <Link
          to="/admin/seasonal-marks"
          className="mb-2 inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 transition-colors hover:text-brand-600"
        >
          <ArrowLeft className="h-4 w-4" /> Mavsumiy baholash
        </Link>
        <h1 className="text-xl font-semibold text-slate-800">Baholash</h1>
        <p className="text-sm text-slate-400">
          Oylik, choraklik yoki yillik ball — 0 dan 100 gacha. Butun sinf bitta "Saqlash" bilan
          yoziladi.
        </p>
      </div>

      {!canWrite && (
        <p className="flex items-center gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
          <Lock className="h-4 w-4 shrink-0" />
          Ballarni faqat ko'rishingiz mumkin — kiritish uchun “Mavsumiy baholash” ruxsati kerak.
        </p>
      )}

      <SeasonalEntryWorkspace api={adminSeasonalEntryApi} canEdit={canWrite} initial={initial} />
    </div>
  )
}
