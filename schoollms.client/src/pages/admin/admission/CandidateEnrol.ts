/**
 * "O'quvchi qilib ro'yxatga olish" — the candidate side of enrolment
 * (`docs/modules/admission-and-testing.md` §2.2 2026-09-22 note, §3.1 screen 2,
 * §6.1; unit C2).
 *
 * NOTHING IS REBUILT. The form and the call already exist:
 * `pages/admin/leads-enrol/LeadEnrolModal.tsx` opens the standard student form
 * pre-filled from a `Lead` and calls `POST /api/admin/leads/{id}/enrol`, which
 * creates the pupil and DELETES the lead. The screens only have to hand it a
 * `Lead`.
 *
 * WHERE THE `Lead` COMES FROM: `getLeads()` — the board's own
 * `GET /api/admin/leads`, unchanged by contract (§2.3 rule 4) — and the row
 * with this id is picked out of it. Not synthesised from the candidate DTOs:
 * §6.1's row carries no gender, birth date, parent name or stage, and the card
 * DTO is not built yet (B5). Reading the board's record means the pupil form is
 * pre-filled with exactly what the lead holds today, whatever the candidate
 * DTOs end up carrying. The price is one list read per enrolment click.
 *
 * A hook, not a component: `react-refresh` wants component files to export
 * only components, and the two screens render `LeadEnrolModal` themselves.
 */
import { useCallback, useState } from 'react'
import type { Lead } from '@/types'
import { getLeads } from '@/api/services/leads'
import { candidateError } from '@/api/services/candidates'

const LEAD_GONE =
  "Lid topilmadi — u allaqachon o'quvchiga aylantirilgan yoki doskadan o'chirilgan bo'lishi mumkin"

export interface CandidateEnrol {
  /** The lead to hand to `LeadEnrolModal`; `null` — the modal is closed. */
  lead: Lead | null
  /** The lead whose record is being fetched right now, for a spinner on its button. */
  pendingId: string | null
  start: (leadId: string) => Promise<void>
  close: () => void
}

export function useCandidateEnrol(onError: (message: string) => void): CandidateEnrol {
  const [lead, setLead] = useState<Lead | null>(null)
  const [pendingId, setPendingId] = useState<string | null>(null)

  const start = useCallback(
    async (leadId: string) => {
      setPendingId(leadId)
      try {
        const found = (await getLeads()).find((l) => l.id === leadId)
        if (found) setLead(found)
        else onError(LEAD_GONE)
      } catch (err) {
        onError(candidateError(err, 'enrolLookup').message)
      } finally {
        setPendingId(null)
      }
    },
    [onError],
  )

  // Stable: `Modal` keys its Escape listener on `onClose`.
  const close = useCallback(() => setLead(null), [])

  return { lead, pendingId, start, close }
}
