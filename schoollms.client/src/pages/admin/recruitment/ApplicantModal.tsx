import { useCallback, useEffect, useRef, useState } from 'react'
import { Check, ChevronLeft, ChevronRight, Download, Mail, Pencil, Phone, Upload, UserCheck, XCircle } from 'lucide-react'
import {
  finishOnboarding,
  getApplicant,
  hireApplicant,
  moveApplicant,
  openApplicantCv,
  rejectApplicant,
  saveStageResult,
  setOnboardingStep,
  uploadApplicantCv,
  getVacancies,
  PIPELINE,
  STAGE_LABEL,
  type ApplicantDetail,
  type ApplicantStage,
  type OnboardingStep,
  type Vacancy,
} from '@/api/services/recruitment'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { cn } from '@/lib/utils'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { DatePicker } from '@/components/ui/DatePicker'
import { fmtDate, todayIso } from '@/components/lesson-analysis/format'
import { ApplicantFormModal } from './ApplicantFormModal'

const STEPPER: ApplicantStage[] = ['new', ...PIPELINE, 'onboarding', 'adapted']

/** What a stage's result asks for. */
const RESULT_HINT: Partial<Record<ApplicantStage, string>> = {
  cv: "Ma'lumot va CV bo'yicha xulosa",
  test: 'Test natijasi (0–100) va izoh',
  demo: 'Demo dars bahosi (0–100) — kuzatuvchilar xulosasi',
  hr_interview: 'HR suhbati xulosasi',
  director_interview: 'Direktor qarori va izohi',
}

interface Props {
  id: string
  onClose: () => void
  onChanged: (message: string) => void
}

export function ApplicantModal({ id, onClose, onChanged }: Props) {
  const [data, setData] = useState<ApplicantDetail | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [editing, setEditing] = useState(false)
  const [vacancies, setVacancies] = useState<Vacancy[]>([])
  const [rejecting, setRejecting] = useState(false)
  const [reason, setReason] = useState('')
  const [hireOn, setHireOn] = useState(todayIso())
  const fileRef = useRef<HTMLInputElement>(null)

  const load = useCallback(() => {
    getApplicant(id)
      .then(setData)
      .catch((e) => setError(hrErrorMessage(e, "Yuklab bo'lmadi")))
  }, [id])
  useEffect(load, [load])

  const act = async (action: () => Promise<unknown>, message: string) => {
    setBusy(true)
    setError('')
    try {
      await action()
      load()
      onChanged(message)
    } catch (e) {
      setError(hrErrorMessage(e, "Bajarib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  if (!data)
    return (
      <Modal open onClose={onClose} size="lg" title="Nomzod">
        {error ? <p className="text-sm text-red-600">{error}</p> : <Loader label="Yuklanmoqda..." />}
      </Modal>
    )

  const r = data.row
  const stageIndex = PIPELINE.indexOf(r.stage)
  const inPipeline = stageIndex >= 0
  const reached = r.stage === 'rejected' ? STEPPER.indexOf(r.rejectedAtStage ?? 'new') : STEPPER.indexOf(r.stage)
  const allDone = data.onboarding.length > 0 && data.onboarding.every((s) => s.doneAt)

  return (
    <Modal open onClose={onClose} size="xl" title={r.fullName}>
      <div className="space-y-5">
        {/* Contact and CV */}
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="space-y-1 text-sm">
            <p className="font-medium text-slate-700">{r.vacancyTitle ?? r.position ?? 'Lavozim ko\'rsatilmagan'}</p>
            <p className="flex flex-wrap items-center gap-x-4 gap-y-1 text-slate-500">
              <span className="inline-flex items-center gap-1">
                <Phone className="h-3.5 w-3.5" /> {r.phone}
              </span>
              {r.email && (
                <span className="inline-flex items-center gap-1">
                  <Mail className="h-3.5 w-3.5" /> {r.email}
                </span>
              )}
              <span>
                {r.source === 'public' ? 'Sayt orqali' : "Qo'lda qo'shilgan"} · {fmtDate(r.createdAt)}
              </span>
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            {r.hasCv && (
              <Button variant="secondary" onClick={() => openApplicantCv(r.id, data.cvName).catch(() => setError("CV'ni ochib bo'lmadi"))}>
                <Download className="h-4 w-4" /> CV
              </Button>
            )}
            <Button variant="secondary" onClick={() => fileRef.current?.click()} disabled={busy}>
              <Upload className="h-4 w-4" /> {r.hasCv ? 'CV almashtirish' : 'CV yuklash'}
            </Button>
            <input
              ref={fileRef}
              type="file"
              accept=".pdf,.doc,.docx,.jpg,.jpeg,.png"
              className="hidden"
              onChange={(e) => {
                const file = e.target.files?.[0]
                e.target.value = ''
                if (file) void act(() => uploadApplicantCv(r.id, file), 'CV yuklandi')
              }}
            />
            <Button
              variant="ghost"
              onClick={() => {
                getVacancies().then(setVacancies).catch(() => setVacancies([]))
                setEditing(true)
              }}
            >
              <Pencil className="h-4 w-4" /> Tahrirlash
            </Button>
          </div>
        </div>

        {/* Stepper */}
        <div className="flex flex-wrap items-center gap-1">
          {STEPPER.map((s, i) => (
            <div key={s} className="flex items-center gap-1">
              <span
                className={cn(
                  'rounded-lg px-2.5 py-1 text-xs font-medium',
                  r.stage === s
                    ? 'bg-brand-600 text-white'
                    : i < reached
                      ? 'bg-emerald-50 text-emerald-700'
                      : r.stage === 'rejected' && i === reached
                        ? 'bg-red-50 text-red-600'
                        : 'bg-slate-100 text-slate-400',
                )}
              >
                {STAGE_LABEL[s]}
              </span>
              {i < STEPPER.length - 1 && <ChevronRight className="h-3.5 w-3.5 text-slate-300" />}
            </div>
          ))}
        </div>

        {r.stage === 'rejected' && (
          <div className="rounded-xl bg-red-50 px-4 py-3 text-sm text-red-700">
            <b>Rad etilgan</b> ({r.rejectedAtStage ? STAGE_LABEL[r.rejectedAtStage] : ''} bosqichida): {r.rejectReason}
          </div>
        )}

        {(data.experience || data.about) && (
          <div className="grid gap-3 md:grid-cols-2">
            {data.experience && <TextBlock title="Ish tajribasi" text={data.experience} />}
            {data.about && <TextBlock title="O'zi haqida" text={data.about} />}
          </div>
        )}

        {/* Stage results */}
        {(inPipeline || data.results.length > 0) && (
          <div className="space-y-3">
            <h3 className="text-sm font-semibold text-slate-700">Bosqich natijalari</h3>
            {inPipeline && (
              <ResultForm
                key={r.stage}
                stage={r.stage}
                current={data.results.find((x) => x.stage === r.stage) ?? null}
                busy={busy}
                onSave={(score, comment) => act(() => saveStageResult(r.id, r.stage, score, comment), 'Natija saqlandi')}
              />
            )}
            {data.results
              .filter((x) => x.stage !== r.stage || !inPipeline)
              .map((x) => (
                <div key={x.stage} className="flex gap-3 rounded-xl border border-slate-100 px-3 py-2.5 text-sm">
                  <span className="w-32 shrink-0 font-medium text-slate-600">{x.stageLabel}</span>
                  <div className="min-w-0 flex-1">
                    {x.score !== null && <span className="mr-2 font-semibold text-brand-700">{x.score} ball</span>}
                    <span className="whitespace-pre-line text-slate-700">{x.comment}</span>
                    <div className="text-xs text-slate-400">
                      {x.byName} · {fmtDate(x.at)}
                    </div>
                  </div>
                </div>
              ))}
          </div>
        )}

        {/* Onboarding checklist */}
        {data.onboarding.length > 0 && (
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-semibold text-slate-700">
                Adaptatsiya — {data.onboarding.filter((s) => s.doneAt).length}/{data.onboarding.length} bosqich
              </h3>
              {data.onboardingDay !== null && (
                <span className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-semibold text-brand-700">
                  {Math.min(data.onboardingDay, 30)}/30 kun · {r.hiredOn && `ishga kirgan ${fmtDate(r.hiredOn)}`}
                </span>
              )}
            </div>
            {data.onboarding.map((s) => (
              <StepRow
                key={s.id}
                step={s}
                editable={r.stage === 'onboarding'}
                onToggle={(done, comment) =>
                  act(() => setOnboardingStep(r.id, s.id, done, comment), done ? 'Bosqich bajarildi' : 'Bosqich ochildi')
                }
              />
            ))}
          </div>
        )}

        {rejecting && (
          <div className="space-y-2 rounded-xl border border-red-100 bg-red-50/40 p-3">
            <textarea
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              rows={2}
              maxLength={1000}
              placeholder="Rad etish sababi (nomzod kartasida saqlanadi)"
              className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm outline-none focus:border-red-300"
            />
            <div className="flex justify-end gap-2">
              <Button variant="secondary" onClick={() => setRejecting(false)}>
                Bekor qilish
              </Button>
              <Button
                variant="danger"
                disabled={busy || !reason.trim()}
                onClick={() =>
                  act(async () => {
                    await rejectApplicant(r.id, reason.trim())
                    setRejecting(false)
                    setReason('')
                  }, 'Nomzod rad etildi')
                }
              >
                Rad etish
              </Button>
            </div>
          </div>
        )}

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

        {/* Actions */}
        <div className="flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 pt-4">
          <div className="flex flex-wrap gap-2">
            {r.stage !== 'rejected' && r.stage !== 'adapted' && !rejecting && (
              <Button variant="ghost" onClick={() => setRejecting(true)} disabled={busy}>
                <XCircle className="h-4 w-4 text-red-500" /> Rad etish
              </Button>
            )}
            {inPipeline && stageIndex > 0 && (
              <Button variant="secondary" disabled={busy} onClick={() => act(() => moveApplicant(r.id, PIPELINE[stageIndex - 1]), 'Oldingi bosqichga qaytarildi')}>
                <ChevronLeft className="h-4 w-4" /> {STAGE_LABEL[PIPELINE[stageIndex - 1]]}
              </Button>
            )}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {r.stage === 'new' && (
              <Button disabled={busy} onClick={() => act(() => moveApplicant(r.id, 'cv'), 'Nomzod jarayonga olindi')}>
                Jarayonga olish <ChevronRight className="h-4 w-4" />
              </Button>
            )}
            {inPipeline && stageIndex < PIPELINE.length - 1 && (
              <Button disabled={busy} onClick={() => act(() => moveApplicant(r.id, PIPELINE[stageIndex + 1]), 'Keyingi bosqichga o\'tkazildi')}>
                {STAGE_LABEL[PIPELINE[stageIndex + 1]]} <ChevronRight className="h-4 w-4" />
              </Button>
            )}
            {r.stage === 'director_interview' && (
              <>
                <DatePicker value={hireOn} onChange={(v: string) => setHireOn(v)} className="w-40" />
                <Button disabled={busy || !hireOn} onClick={() => act(() => hireApplicant(r.id, hireOn), 'Ishga olindi — adaptatsiya boshlandi')}>
                  <UserCheck className="h-4 w-4" /> Ishga olish
                </Button>
              </>
            )}
            {r.stage === 'onboarding' && (
              <Button
                disabled={busy || !allDone}
                title={allDone ? undefined : 'Avval barcha bosqichlarni bajaring'}
                onClick={() => act(() => finishOnboarding(r.id), 'Adaptatsiya yakunlandi')}
              >
                <Check className="h-4 w-4" /> Adaptatsiyani yakunlash
              </Button>
            )}
            {r.stage === 'rejected' && (
              <Button variant="secondary" disabled={busy} onClick={() => act(() => moveApplicant(r.id, 'cv'), "Nomzod qayta ko'rib chiqishga olindi")}>
                Qayta ko'rib chiqish
              </Button>
            )}
          </div>
        </div>

        {/* History */}
        <details className="text-sm">
          <summary className="cursor-pointer text-slate-500">Tarix ({data.events.length})</summary>
          <ol className="mt-2 space-y-1.5">
            {data.events.map((e, i) => (
              <li key={i} className="flex gap-2 text-slate-600">
                <span className="w-24 shrink-0 text-xs text-slate-400">{fmtDate(e.at)}</span>
                <span>
                  <b>{e.toLabel}</b>
                  {e.note && ` — ${e.note}`}
                  {e.byName && <span className="text-slate-400"> · {e.byName}</span>}
                </span>
              </li>
            ))}
          </ol>
        </details>
      </div>

      {editing && (
        <ApplicantFormModal
          vacancies={vacancies}
          applicant={data}
          onClose={() => setEditing(false)}
          onSaved={() => {
            setEditing(false)
            load()
            onChanged('Saqlandi')
          }}
        />
      )}
    </Modal>
  )
}

function TextBlock({ title, text }: { title: string; text: string }) {
  return (
    <div className="rounded-xl bg-slate-50 px-3 py-2.5">
      <p className="mb-1 text-xs font-medium uppercase tracking-wide text-slate-400">{title}</p>
      <p className="whitespace-pre-line text-sm text-slate-700">{text}</p>
    </div>
  )
}

function ResultForm({
  stage,
  current,
  busy,
  onSave,
}: {
  stage: ApplicantStage
  current: { score: number | null; comment: string | null } | null
  busy: boolean
  onSave: (score: number | null, comment: string | null) => void
}) {
  const [score, setScore] = useState(current?.score?.toString() ?? '')
  const [comment, setComment] = useState(current?.comment ?? '')
  const value = score.trim() === '' ? null : Number(score)
  const invalid = value !== null && (!Number.isInteger(value) || value < 0 || value > 100)
  return (
    <div className="space-y-2 rounded-xl border border-brand-100 bg-brand-50/30 p-3">
      <p className="text-sm font-medium text-slate-700">
        {STAGE_LABEL[stage]} — <span className="font-normal text-slate-500">{RESULT_HINT[stage]}</span>
      </p>
      <div className="flex flex-wrap gap-2">
        <input
          type="number"
          min={0}
          max={100}
          value={score}
          onChange={(e) => setScore(e.target.value)}
          placeholder="Ball"
          className={cn(
            'w-24 rounded-lg border bg-white px-3 py-2 text-sm outline-none focus:border-brand-400',
            invalid ? 'border-red-300' : 'border-slate-200',
          )}
        />
        <textarea
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          rows={2}
          maxLength={4000}
          placeholder="Izoh / xulosa"
          className="min-w-60 flex-1 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm outline-none focus:border-brand-400"
        />
        <Button
          variant="secondary"
          disabled={busy || invalid || (value === null && !comment.trim())}
          onClick={() => onSave(value, comment.trim() || null)}
        >
          Saqlash
        </Button>
      </div>
    </div>
  )
}

function StepRow({
  step,
  editable,
  onToggle,
}: {
  step: OnboardingStep
  editable: boolean
  onToggle: (done: boolean, comment: string | null) => void
}) {
  const [comment, setComment] = useState(step.comment ?? '')
  const done = Boolean(step.doneAt)
  return (
    <div className={cn('flex gap-3 rounded-xl border px-3 py-2.5', step.overdue ? 'border-red-100 bg-red-50/30' : 'border-slate-100')}>
      <button
        type="button"
        disabled={!editable}
        onClick={() => onToggle(!done, comment.trim() || null)}
        aria-label={done ? 'Bajarilmagan deb belgilash' : 'Bajarildi deb belgilash'}
        className={cn(
          'mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-md border',
          done ? 'border-emerald-500 bg-emerald-500 text-white' : 'border-slate-300 bg-white',
          !editable && 'opacity-60',
        )}
      >
        {done && <Check className="h-3.5 w-3.5" />}
      </button>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className={cn('text-sm font-medium', done ? 'text-slate-400 line-through' : 'text-slate-800')}>
            {step.position}. {step.title}
          </p>
          <span className={cn('text-xs', step.overdue ? 'font-medium text-red-600' : 'text-slate-400')}>
            {step.day}-kun · {fmtDate(step.dueOn)}
          </span>
        </div>
        {step.description && <p className="text-xs text-slate-500">{step.description}</p>}
        {done ? (
          <p className="mt-1 text-xs text-emerald-700">
            Bajarildi {step.doneAt && fmtDate(step.doneAt)}
            {step.doneByName && ` · ${step.doneByName}`}
            {step.comment && ` — ${step.comment}`}
          </p>
        ) : (
          editable && (
            <input
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              maxLength={1000}
              placeholder="Izoh (ixtiyoriy)"
              className="mt-1.5 w-full rounded-lg border border-slate-200 px-2.5 py-1.5 text-xs outline-none focus:border-brand-400"
            />
          )
        )}
      </div>
    </div>
  )
}
