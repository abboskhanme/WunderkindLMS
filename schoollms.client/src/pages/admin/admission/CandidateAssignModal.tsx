/**
 * "Imtihonga biriktirish" — put one or more leads on an admission exam
 * (`docs/modules/admission-and-testing.md` §3.1 screens 1–2, §6.3, §8.5; unit C2).
 *
 * THE DOOR INTO THE LIST. A candidate is a lead whose `admission_status` is
 * not `none`, and the only thing that moves it off `none` is a participant row
 * on an admission exam (§8.5: `invited`). The Lidlar board is design-frozen
 * (§2.3), so the board cannot do it — this modal can: in `pickLeads` mode it
 * lists the board's leads (`getLeads`, the unchanged board endpoint) and the
 * user ticks who sits which exam.
 *
 * ONE CALL, THE EXAMS API. `POST /api/admin/exams/{id}/participants
 * { leadIds }` (§6.3) — idempotent, so a lead already on that exam comes back
 * in `skipped` rather than twice. Perm `exams`, plus `admission` for an
 * admission exam; the caller only renders this for a user holding both.
 *
 * NO LINK IS ISSUED HERE. §7.2: a link is issued explicitly, per candidate,
 * by a person — never on assignment. The success sentence says where to do it.
 */
import { useEffect, useMemo, useState } from 'react'
import { AlertTriangle, ClipboardList, Loader2, Search } from 'lucide-react'
import type { Lead } from '@/types'
import { getLeads } from '@/api/services/leads'
import { addParticipants, listExams, type ExamRow } from '@/api/services/exams'
import { candidateError } from '@/api/services/candidates'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'
import {
  DASH,
  control,
  examStatusLabel,
  examStatusTone,
  formatDay,
  formatWallClock,
  gradeLabel,
} from '../exams/examLabels'
import { isOpenExamStatus } from './CandidateHelpers'
import { formatPhone } from '@/lib/phone'

/** Who is being assigned — the three fields the picker and the grade check need. */
export interface AssignTarget {
  leadId: string
  fullName: string
  targetGrade: number
}

interface Props {
  /** Pre-chosen candidates (a row, the bulk selection, the card). Ignored in `pickLeads` mode. */
  targets: AssignTarget[]
  /** "Nomzod qo'shish": choose the leads here, from the board's list. */
  pickLeads: boolean
  onClose: () => void
  /** Saved; the parent toasts the sentence and re-reads. */
  onAssigned: (message: string) => void
}

type Load<T> = { status: 'loading' } | { status: 'error'; message: string } | { status: 'ready'; data: T }

/** The picker is a checklist, not a register: past this many matches, ask for a narrower search. */
const LEAD_PICKER_CAP = 100

const checkbox = 'h-4 w-4 shrink-0 rounded border-slate-300 accent-brand-600'

function examWindow(exam: ExamRow): string {
  if (exam.opensAt || exam.closesAt) {
    return `${formatWallClock(exam.opensAt)} — ${formatWallClock(exam.closesAt)}`
  }
  return exam.examDate ? formatDay(exam.examDate) : DASH
}

/** Mounted fresh per opening, so every piece of state starts clean. */
export function CandidateAssignModal({ targets, pickLeads, onClose, onAssigned }: Props) {
  const [exams, setExams] = useState<Load<ExamRow[]>>({ status: 'loading' })
  const [leads, setLeads] = useState<Load<Lead[]>>(
    pickLeads ? { status: 'loading' } : { status: 'ready', data: [] },
  )
  const [examsToken, setExamsToken] = useState(0)
  const [leadsToken, setLeadsToken] = useState(0)

  const [examId, setExamId] = useState('')
  const [picked, setPicked] = useState<Map<string, AssignTarget>>(() => new Map())
  const [search, setSearch] = useState('')

  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Open admission exams. §6: `limit ≤ 200` — a school runs a handful per season.
  useEffect(() => {
    let cancelled = false
    listExams({ kind: 'admission', page: 1, limit: 200 })
      .then((page) => {
        if (cancelled) return
        const open = page.items.filter((e) => isOpenExamStatus(e.status))
        setExams({ status: 'ready', data: open })
      })
      .catch((err: unknown) => {
        if (!cancelled) setExams({ status: 'error', message: candidateError(err, 'lookup').message })
      })
    return () => {
      cancelled = true
    }
  }, [examsToken])

  useEffect(() => {
    if (!pickLeads) return
    let cancelled = false
    getLeads()
      .then((data) => {
        if (!cancelled) setLeads({ status: 'ready', data })
      })
      .catch((err: unknown) => {
        if (!cancelled) setLeads({ status: 'error', message: candidateError(err, 'lookup').message })
      })
    return () => {
      cancelled = true
    }
  }, [pickLeads, leadsToken])

  const chosen = useMemo<AssignTarget[]>(
    () => (pickLeads ? [...picked.values()] : targets),
    [pickLeads, picked, targets],
  )
  const chosenGrades = useMemo(() => new Set(chosen.map((t) => t.targetGrade)), [chosen])

  const openExams = useMemo(() => (exams.status === 'ready' ? exams.data : []), [exams])
  // The exam the user did not have to choose: one grade among the candidates, one exam for it.
  const suggested = useMemo(() => {
    if (chosenGrades.size !== 1) return ''
    const [grade] = [...chosenGrades]
    const matching = openExams.filter((e) => e.grade === grade)
    return matching.length === 1 ? matching[0].id : ''
  }, [chosenGrades, openExams])
  const effectiveExamId = examId || suggested
  const exam = openExams.find((e) => e.id === effectiveExamId) ?? null

  const mismatched =
    exam && exam.grade !== null ? chosen.filter((t) => t.targetGrade !== exam.grade).length : 0

  const term = search.trim().toLocaleLowerCase()
  const leadMatches = useMemo(() => {
    if (leads.status !== 'ready') return []
    return term
      ? leads.data.filter(
          (l) => l.fullName.toLocaleLowerCase().includes(term) || l.parentPhone.includes(term),
        )
      : leads.data
  }, [leads, term])

  const toggleLead = (lead: Lead) =>
    setPicked((prev) => {
      const next = new Map(prev)
      if (next.has(lead.id)) next.delete(lead.id)
      else next.set(lead.id, { leadId: lead.id, fullName: lead.fullName, targetGrade: lead.targetGrade })
      return next
    })

  const submit = async () => {
    if (!exam || chosen.length === 0 || saving) return
    setSaving(true)
    setError(null)
    try {
      const result = await addParticipants(exam.id, { leadIds: chosen.map((t) => t.leadId) })
      const parts = [`«${exam.title}» imtihoniga ${result.added} ta nomzod biriktirildi`]
      if (result.skipped > 0) parts.push(`${result.skipped} tasi avval biriktirilgan`)
      onAssigned(
        result.added > 0
          ? `${parts.join(', ')}. Havolani nomzod kartochkasidan chiqaring.`
          : `Hech kim qo'shilmadi — tanlanganlarning hammasi bu imtihonga avval biriktirilgan.`,
      )
    } catch (err) {
      setError(candidateError(err, 'assign').message)
    } finally {
      setSaving(false)
    }
  }

  const loadFailed = (message: string, retry: () => void) => (
    <div className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
      <span className="flex-1">{message}</span>
      <button type="button" onClick={retry} className="font-medium underline-offset-2 hover:underline">
        Qayta urinish
      </button>
    </div>
  )

  return (
    <Modal
      open
      onClose={saving ? () => undefined : onClose}
      size="lg"
      title={pickLeads ? "Nomzod qo'shish" : 'Imtihonga biriktirish'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button onClick={() => void submit()} disabled={saving || !exam || chosen.length === 0}>
            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
            {chosen.length > 1 ? `${chosen.length} ta nomzodni biriktirish` : 'Biriktirish'}
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        {pickLeads ? (
          <section className="space-y-2">
            <div className="flex items-baseline justify-between gap-3">
              <h4 className="text-sm font-semibold text-slate-800">Lidlar</h4>
              <span className="text-xs text-slate-400">
                {picked.size > 0 ? `${picked.size} ta tanlandi` : 'Kimlar imtihon topshiradi?'}
              </span>
            </div>
            {leads.status === 'loading' ? (
              <p className="flex items-center gap-2 py-6 text-sm text-slate-400">
                <Loader2 className="h-4 w-4 animate-spin" /> Lidlar yuklanmoqda...
              </p>
            ) : leads.status === 'error' ? (
              loadFailed(leads.message, () => {
                setLeads({ status: 'loading' })
                setLeadsToken((t) => t + 1)
              })
            ) : leads.data.length === 0 ? (
              <p className="rounded-lg bg-slate-50 px-3 py-4 text-sm text-slate-500">
                Lidlar doskasi bo'sh — avval lid qo'shing, keyin uni imtihonga biriktiring.
              </p>
            ) : (
              <>
                <div className="relative">
                  <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
                  <input
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Ism yoki telefon..."
                    aria-label="Lid qidirish"
                    className={cn(control, 'w-full pl-9')}
                  />
                </div>
                <ul className="max-h-64 divide-y divide-slate-50 overflow-y-auto rounded-xl border border-slate-100">
                  {leadMatches.length === 0 ? (
                    <li className="px-3 py-4 text-center text-sm text-slate-400">Hech kim topilmadi</li>
                  ) : (
                    leadMatches.slice(0, LEAD_PICKER_CAP).map((lead) => (
                      <li key={lead.id}>
                        <label className="flex cursor-pointer items-center gap-3 px-3 py-2 hover:bg-slate-50">
                          <input
                            type="checkbox"
                            checked={picked.has(lead.id)}
                            onChange={() => toggleLead(lead)}
                            className={checkbox}
                          />
                          <span className="min-w-0 flex-1 truncate text-sm text-slate-800">
                            {lead.fullName}
                          </span>
                          <span className="shrink-0 text-xs text-slate-400">
                            {gradeLabel(lead.targetGrade)}
                          </span>
                          <span className="hidden shrink-0 text-xs text-slate-400 sm:inline">
                            {formatPhone(lead.parentPhone)}
                          </span>
                        </label>
                      </li>
                    ))
                  )}
                </ul>
                {leadMatches.length > LEAD_PICKER_CAP && (
                  <p className="text-xs text-slate-400">
                    Birinchi {LEAD_PICKER_CAP} tasi ko'rsatildi — qidiruvni aniqlashtiring.
                  </p>
                )}
                <p className="text-xs text-slate-400">
                  Bu imtihonga avval biriktirilgan lid qayta qo'shilmaydi.
                </p>
              </>
            )}
          </section>
        ) : (
          <p className="text-sm text-slate-600">
            {targets.length === 1 ? (
              <>
                <span className="font-medium text-slate-800">{targets[0].fullName}</span> (
                {gradeLabel(targets[0].targetGrade)}) qaysi imtihonni topshiradi?
              </>
            ) : (
              <>
                Tanlangan <span className="font-medium text-slate-800">{targets.length} ta nomzod</span>{' '}
                qaysi imtihonni topshiradi?
              </>
            )}
          </p>
        )}

        <section className="space-y-2">
          <h4 className="text-sm font-semibold text-slate-800">Qabul imtihoni</h4>
          {exams.status === 'loading' ? (
            <p className="flex items-center gap-2 py-6 text-sm text-slate-400">
              <Loader2 className="h-4 w-4 animate-spin" /> Imtihonlar yuklanmoqda...
            </p>
          ) : exams.status === 'error' ? (
            loadFailed(exams.message, () => {
              setExams({ status: 'loading' })
              setExamsToken((t) => t + 1)
            })
          ) : openExams.length === 0 ? (
            <div className="flex flex-col items-center gap-2 rounded-xl bg-slate-50 px-4 py-6 text-center">
              <ClipboardList className="h-6 w-6 text-slate-300" />
              <p className="text-sm font-medium text-slate-600">Ochiq qabul imtihoni yo'q</p>
              <p className="max-w-sm text-xs text-slate-400">
                Avval «Imtihonlar» bo'limida qabul imtihonini yarating. Yopilgan va bekor qilingan
                imtihonlarga nomzod biriktirib bo'lmaydi.
              </p>
            </div>
          ) : (
            <ul className="space-y-2">
              {openExams.map((e) => {
                const selected = e.id === effectiveExamId
                return (
                  <li key={e.id}>
                    <label
                      className={cn(
                        'flex cursor-pointer items-start gap-3 rounded-xl border px-3 py-2.5 transition-colors',
                        selected
                          ? 'border-brand-300 bg-brand-50/60'
                          : 'border-slate-200 hover:bg-slate-50',
                      )}
                    >
                      <input
                        type="radio"
                        name="candidate-exam"
                        checked={selected}
                        onChange={() => setExamId(e.id)}
                        className="mt-0.5 h-4 w-4 shrink-0 accent-brand-600"
                      />
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-sm font-medium text-slate-800">
                          {e.title}
                        </span>
                        <span className="mt-0.5 block text-xs text-slate-400">
                          {gradeLabel(e.grade)} · {examWindow(e)}
                        </span>
                      </span>
                      <span
                        className={cn(
                          'shrink-0 rounded-full px-2 py-0.5 text-xs font-medium',
                          examStatusTone(e.status),
                        )}
                      >
                        {examStatusLabel(e.status)}
                      </span>
                    </label>
                  </li>
                )
              })}
            </ul>
          )}

          {exam && mismatched > 0 && (
            <p className="flex items-start gap-2 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              {chosen.length === 1
                ? `Nomzod ${gradeLabel(chosen[0].targetGrade)}ga keladi, imtihon esa ${gradeLabel(exam.grade)} uchun.`
                : `${mismatched} ta nomzodning sinfi imtihon sinfiga (${gradeLabel(exam.grade)}) mos emas.`}
            </p>
          )}
        </section>

        {error && (
          <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {error}
          </p>
        )}
      </div>
    </Modal>
  )
}
