/**
 * One submitted application, in full — the right-hand drawer of
 * `docs/modules/sales-marketing.md` §2.7 / §6.2 (task SM-9).
 *
 * WHY A DRAWER AND NOT A MODAL: the register is read row by row ("who applied
 * yesterday, and did we call them"). A drawer keeps the list visible behind it,
 * so the admissions officer does not lose their place in a filtered page of 50.
 * It is built from `Card`/`Button`/`Loader` and our own palette — no second
 * component set (`CLAUDE.md`: our visual language is ours).
 *
 * WHAT IT SHOWS THAT THE LIST CANNOT: the raw values as the parent typed them
 * (before anyone edited the lead), and `ip` / `userAgent`. Those two are the
 * only place a member of the public's IP surfaces in this system (§4.2): here,
 * for abuse triage, and never in the export.
 *
 * THE LEAD IS LINKED, NOT EMBEDDED. `pages/admin/leads/*` is design-frozen —
 * the drawer points at the board (`/admin/leads`) and stops there.
 */
import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { AlertTriangle, ExternalLink, RefreshCw, X } from 'lucide-react'
import {
  getSubmission,
  submissionsErrorMessage,
  type Submission,
  type SubmissionDetail,
} from '@/api/services/surveySubmissions'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import {
  DASH,
  formatDateTime,
  genderLabel,
  gradeLabel,
  orDash,
  statusLabel,
  statusTone,
} from './submissionLabels'

interface Props {
  /** The row the user clicked. The drawer opens with it while the full record loads. */
  row: Submission
  /** Hidden when the user has no `leads` permission — the board would refuse them anyway. */
  canOpenLeads: boolean
  onClose: () => void
}

interface State {
  data: SubmissionDetail | null
  loading: boolean
  error: string | null
}

/** One "label / value" line. Long values wrap; nothing is silently cut. */
function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[9rem_1fr] gap-3 py-2">
      <dt className="text-xs font-medium uppercase tracking-wide text-slate-400">{label}</dt>
      <dd className="text-sm break-words text-slate-700">{children}</dd>
    </div>
  )
}

function Section({
  title,
  note,
  children,
}: {
  title: string
  /** A sentence under the list — kept outside the `<dl>`, where a `<p>` is invalid. */
  note?: string
  children: ReactNode
}) {
  return (
    <section className="border-t border-slate-100 px-5 py-3 first:border-t-0">
      <h3 className="mb-1 text-sm font-semibold text-slate-800">{title}</h3>
      <dl className="divide-y divide-slate-50">{children}</dl>
      {note && <p className="pt-2 text-xs text-slate-400">{note}</p>}
    </section>
  )
}

export function SubmissionDetailDrawer({ row, canOpenLeads, onClose }: Props) {
  // Mounted with `key={row.id}`, so this runs once per opened row: the initial
  // state is the loading state and the effect only writes from its callbacks.
  const [state, setState] = useState<State>({ data: null, loading: true, error: null })
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let cancelled = false
    getSubmission(row.id)
      .then((data) => {
        if (!cancelled) setState({ data, loading: false, error: null })
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setState({
          data: null,
          loading: false,
          error: submissionsErrorMessage(err, "Arizani yuklab bo'lmadi"),
        })
      })
    return () => {
      cancelled = true
    }
  }, [row.id, attempt])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const retry = () => {
    setState({ data: null, loading: true, error: null })
    setAttempt((n) => n + 1)
  }

  const detail = state.data

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      <div className="absolute inset-0 bg-slate-900/40 backdrop-blur-sm" onClick={onClose} />

      <aside
        role="dialog"
        aria-modal="true"
        aria-label="Ariza tafsilotlari"
        className="relative z-10 flex h-full w-full max-w-md flex-col overflow-y-auto border-l border-slate-200 bg-white shadow-xl"
      >
        <header className="sticky top-0 z-10 flex items-start justify-between gap-3 border-b border-slate-100 bg-white px-5 py-4">
          <div className="min-w-0">
            <h2 className="truncate font-semibold text-slate-800">{row.parentFullName}</h2>
            <p className="mt-0.5 flex flex-wrap items-center gap-2 text-xs text-slate-400">
              <span
                className={cn(
                  'inline-flex items-center rounded-full px-2 py-0.5 font-medium',
                  statusTone(row.status),
                )}
              >
                {statusLabel(row.status)}
              </span>
              <span>{formatDateTime(row.createdAt)}</span>
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Yopish"
            className="rounded-lg p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
          >
            <X className="h-5 w-5" />
          </button>
        </header>

        {state.loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : state.error ? (
          <div className="flex flex-1 flex-col items-center justify-center gap-3 px-6 py-12 text-center">
            <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-red-50 text-red-600">
              <AlertTriangle className="h-6 w-6" />
            </div>
            <div>
              <p className="font-medium text-slate-800">Arizani ochib bo'lmadi</p>
              <p className="mt-1 text-sm text-slate-500">{state.error}</p>
            </div>
            <Button variant="secondary" onClick={retry}>
              <RefreshCw className="h-4 w-4" /> Qayta urinish
            </Button>
          </div>
        ) : detail ? (
          <div className="pb-6">
            <Section title="Ariza">
              <Field label="Forma">{detail.surveyName}</Field>
              <Field label="Topshirilgan">{formatDateTime(detail.createdAt)}</Field>
            </Section>

            <Section title="Ota-ona">
              <Field label="Ismi">{orDash(detail.parentFirstName)}</Field>
              <Field label="Familiyasi">{orDash(detail.parentLastName)}</Field>
              <Field label="Telefon">
                {detail.parentPhone ? (
                  <a
                    href={`tel:${detail.parentPhone.replace(/[^\d+]/g, '')}`}
                    className="font-medium text-brand-600 hover:text-brand-700"
                  >
                    {detail.parentPhone}
                  </a>
                ) : (
                  DASH
                )}
              </Field>
            </Section>

            <Section title="O'quvchi">
              <Field label="Ismi">{orDash(detail.studentFirstName)}</Field>
              <Field label="Familiyasi">{orDash(detail.studentLastName)}</Field>
              <Field label="Jinsi">{genderLabel(detail.studentGender)}</Field>
              <Field label="Sinf">{gradeLabel(detail.studentGrade)}</Field>
              <Field label="Telefon">
                {detail.studentPhone ? (
                  <a
                    href={`tel:${detail.studentPhone.replace(/[^\d+]/g, '')}`}
                    className="font-medium text-brand-600 hover:text-brand-700"
                  >
                    {detail.studentPhone}
                  </a>
                ) : (
                  DASH
                )}
              </Field>
            </Section>

            <Section title="Lid">
              <Field label="Holati">{statusLabel(detail.status)}</Field>
              <Field label="Bosqich">{orDash(detail.leadStageTitle)}</Field>
              <Field label="Doskada">
                {/* The board has no per-lead route and its files are frozen, so this
                    opens Lidlar itself — the row is in the stage named above. */}
                {detail.leadId === null ? (
                  <span className="text-slate-400">
                    Lid o'chirilgan — ariza esa shu yerda qoladi
                  </span>
                ) : !canOpenLeads ? (
                  <span className="text-slate-400">Lidlar bo'limiga ruxsatingiz yo'q</span>
                ) : (
                  <Link
                    to="/admin/leads"
                    className="inline-flex items-center gap-1 font-medium text-brand-600 hover:text-brand-700"
                  >
                    Lidlar doskasini ochish
                    <ExternalLink className="h-3.5 w-3.5" />
                  </Link>
                )}
              </Field>
              {detail.status === 'duplicate' && (
                <Field label="Izoh">
                  <span className="text-slate-500">
                    Shu telefon va shu bola bilan 24 soat ichida ikkinchi marta ariza
                    topshirilgan — yangi lid yaratilmadi, mavjudi saqlandi.
                  </span>
                </Field>
              )}
            </Section>

            <Section
              title="Texnik ma'lumot"
              note="Bu ikki qator faqat suiiste'molni tekshirish uchun saqlanadi va Excel eksportiga tushmaydi."
            >
              <Field label="IP manzil">
                <span className="font-mono text-xs">{orDash(detail.ip)}</span>
              </Field>
              <Field label="Brauzer">
                <span className="font-mono text-xs leading-5">{orDash(detail.userAgent)}</span>
              </Field>
            </Section>
          </div>
        ) : null}
      </aside>
    </div>
  )
}
