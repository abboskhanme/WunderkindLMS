/**
 * Public enrolment form — `/ariza/:slug` (§6.1 of `docs/modules/sales-marketing.md`,
 * task SM-7).
 *
 * The only page in the SPA that renders outside `AppLayout` and without a
 * session. A parent opens it from a Telegram post or an Instagram bio, on a
 * phone, and fills it in once. It makes exactly two requests — the public GET
 * and the public POST of §5.1 — and no others: no school metadata, no session
 * check, no analytics, nothing from a CDN.
 *
 * Which fields exist is the server's answer, not ours: the three parent fields
 * are always shown, every pupil field follows its toggle in the GET response
 * (§2.4). Grade and gender are always on — Rule T pins them — but the code still
 * reads the flags rather than assuming them.
 *
 * The route is registered by task SM-12; until then this file is unreachable,
 * which is intended.
 */
import { useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { useParams } from 'react-router-dom'
import { AlertTriangle, CheckCircle2, FileSearch, FileText, Loader2, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { useAsync } from '@/hooks/useAsync'
import { loadPublicSurvey, submitPublicSurvey } from '@/api/services/publicSurvey'
import type {
  PublicSurvey,
  PublicSurveyFields,
  PublicSurveySubmission,
  StudentGender,
  SurveyFieldErrors,
} from '@/api/services/publicSurvey'
import { Honeypot, PhoneField, RadioPills, SelectField, TextField } from './SurveyFields'
import { fullPhone } from './phone'

/** Used when neither the POST response nor the survey carries its own text (§5.1). */
const DEFAULT_THANK_YOU = "Arizangiz qabul qilindi. Tez orada siz bilan bog'lanamiz."
const CHECK_FORM_MESSAGE = "Ma'lumotlarni tekshiring"
const LOAD_FAILED_MESSAGE = "Serverga ulanib bo'lmadi. Internet aloqasini tekshiring."

/** 0 is a real grade — nol sinf — not "unknown" (§2.1). */
const GRADES: readonly number[] = Array.from({ length: 12 }, (_, i) => i)

function gradeLabel(grade: number): string {
  return grade === 0 ? 'Nol sinf' : `${grade}-sinf`
}

const GENDER_OPTIONS: ReadonlyArray<{ value: StudentGender; label: string }> = [
  { value: 'male', label: "O'g'il bola" },
  { value: 'female', label: 'Qiz bola' },
]

interface FormValues {
  parentFirstName: string
  parentLastName: string
  /** Digits after +998, at most 9. */
  parentPhone: string
  studentFirstName: string
  studentLastName: string
  /** Digits after +998, at most 9. */
  studentPhone: string
  /** '' or '0'…'11'. */
  studentGrade: string
  studentGender: StudentGender | ''
}

const EMPTY_VALUES: FormValues = {
  parentFirstName: '',
  parentLastName: '',
  parentPhone: '',
  studentFirstName: '',
  studentLastName: '',
  studentPhone: '',
  studentGrade: '',
  studentGender: '',
}

/**
 * The same rules the server applies (§2.4), run here first so the parent is not
 * made to wait for a round trip to be told a field is empty. The server stays
 * the authority: whatever it returns in `errors` overwrites this.
 */
function validate(values: FormValues, fields: PublicSurveyFields): SurveyFieldErrors {
  const errors: SurveyFieldErrors = {}
  const parentFirstName = values.parentFirstName.trim()

  if (!parentFirstName) errors.parentFirstName = 'Ismni kiriting'
  else if (parentFirstName.length < 2) errors.parentFirstName = "Ism kamida 2 ta harfdan iborat bo'lsin"

  if (values.parentPhone.length < 9) errors.parentPhone = "Telefon raqamini to'liq kiriting"

  if (fields.studentFirstName && !values.studentFirstName.trim()) {
    errors.studentFirstName = "O'quvchining ismini kiriting"
  }
  if (fields.studentGender && !values.studentGender) errors.studentGender = 'Jinsini tanlang'
  if (fields.studentGrade && !values.studentGrade) errors.studentGrade = 'Sinfni tanlang'
  if (fields.studentPhone && values.studentPhone.length > 0 && values.studentPhone.length < 9) {
    errors.studentPhone = "Telefon raqamini to'liq kiriting"
  }
  return errors
}

/** Only non-empty values are attached, and only for fields this survey renders (§2.4). */
function buildPayload(
  values: FormValues,
  survey: PublicSurvey,
  website: string,
): PublicSurveySubmission {
  const fields = survey.fields
  const payload: PublicSurveySubmission = {
    parentFirstName: values.parentFirstName.trim(),
    parentPhone: fullPhone(values.parentPhone),
    // The server hands `servedAt` out and wants it back (§2.5): a bot filter,
    // not a security boundary — the rate limit is what actually defends this.
    servedAt: survey.servedAt,
    website,
  }

  const parentLastName = values.parentLastName.trim()
  if (parentLastName) payload.parentLastName = parentLastName

  if (fields.studentFirstName) {
    const studentFirstName = values.studentFirstName.trim()
    if (studentFirstName) payload.studentFirstName = studentFirstName
  }
  if (fields.studentLastName) {
    const studentLastName = values.studentLastName.trim()
    if (studentLastName) payload.studentLastName = studentLastName
  }
  if (fields.studentPhone && values.studentPhone) {
    payload.studentPhone = fullPhone(values.studentPhone)
  }
  if (fields.studentGrade && values.studentGrade) {
    payload.studentGrade = Number(values.studentGrade)
  }
  if (fields.studentGender && values.studentGender) {
    payload.studentGender = values.studentGender
  }
  return payload
}

function PageShell({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-screen bg-slate-100 px-4 py-8">
      <div className="mx-auto w-full max-w-md space-y-4">
        <div className="flex items-center justify-center gap-3">
          {/* Same logo tile as the login page — the school's own yellow. */}
          <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-[#FFD006]">
            <img src="/logo.png" alt="" className="h-7 w-7 object-contain" />
          </div>
          <span className="text-base font-semibold text-slate-800">
            Wunderkind International School
          </span>
        </div>
        {children}
      </div>
    </div>
  )
}

interface StateCardProps {
  icon: ReactNode
  title: string
  text: string
  children?: ReactNode
}

/** One card for every non-form state: loading failure, 429, 404, success. */
function StateCard({ icon, title, text, children }: StateCardProps) {
  return (
    <Card className="flex flex-col items-center gap-3 py-8 text-center">
      {icon}
      <h1 className="text-lg font-semibold text-slate-800">{title}</h1>
      <p className="text-base text-slate-500">{text}</p>
      {children}
    </Card>
  )
}

/** Same retry affordance as the portal's error card (`pages/portal/FinanceView.tsx`). */
function RetryButton({ onRetry }: { onRetry: () => void }) {
  return (
    <Button variant="secondary" onClick={onRetry}>
      <RefreshCw className="h-4 w-4" /> Qayta urinish
    </Button>
  )
}

function SkeletonCard() {
  return (
    <Card className="space-y-4">
      <div className="h-36 w-full animate-pulse rounded-xl bg-slate-100" />
      <div className="h-5 w-2/3 animate-pulse rounded bg-slate-100" />
      <div className="h-4 w-1/2 animate-pulse rounded bg-slate-100" />
      <div className="space-y-3 pt-2">
        {[0, 1, 2, 3].map((row) => (
          <div key={row} className="h-12 w-full animate-pulse rounded-xl bg-slate-100" />
        ))}
      </div>
    </Card>
  )
}

/** Unknown slug and closed survey arrive as the same 404 — one card answers both (D8, §5.1). */
function NotFoundCard({ message }: { message: string }) {
  return (
    <StateCard
      icon={<FileSearch className="h-10 w-10 text-slate-300" />}
      title={message}
      text="Havola eskirgan yoki ariza qabul qilish yakunlangan bo'lishi mumkin. Maktab bilan bog'laning."
    />
  )
}

function OfferLink({ url }: { url: string }) {
  return (
    <a
      href={url}
      target="_blank"
      rel="noreferrer"
      className="inline-flex items-center justify-center gap-2 rounded-xl border border-slate-200 px-3.5 py-2.5 text-sm font-medium text-brand-700 transition-colors hover:bg-slate-50"
    >
      <FileText className="h-4 w-4" />
      Taklif hujjati
    </a>
  )
}

/**
 * The form itself. It owns every value the parent types, so the four §5.6
 * answers to the POST — success, validation, 404, 429 — are handled where they
 * can still do something about them.
 */
function SurveyForm({ slug, survey }: { slug: string; survey: PublicSurvey }) {
  const [values, setValues] = useState<FormValues>(EMPTY_VALUES)
  const [website, setWebsite] = useState('')
  const [errors, setErrors] = useState<SurveyFieldErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [thankYou, setThankYou] = useState<string | null>(null)
  /** Set when the survey was closed between opening the page and submitting. */
  const [closedMessage, setClosedMessage] = useState<string | null>(null)
  const [bannerBroken, setBannerBroken] = useState(false)

  const fields = survey.fields

  const setValue = <K extends keyof FormValues>(key: K, value: FormValues[K]) => {
    setValues((previous) => ({ ...previous, [key]: value }))
    setErrors((previous) => (previous[key as keyof SurveyFieldErrors] ? { ...previous, [key]: undefined } : previous))
  }

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()
    if (submitting) return

    const localErrors = validate(values, fields)
    setErrors(localErrors)
    if (Object.keys(localErrors).length > 0) {
      setFormError(CHECK_FORM_MESSAGE)
      return
    }

    setFormError(null)
    setSubmitting(true)
    const result = await submitPublicSurvey(slug, buildPayload(values, survey, website))
    setSubmitting(false)

    switch (result.kind) {
      case 'ok':
        // A duplicate, a honeypot hit and a real submission answer identically.
        setThankYou(result.thankYou ?? survey.thankYouText ?? DEFAULT_THANK_YOU)
        window.scrollTo({ top: 0 })
        return
      case 'validation':
        setErrors(result.errors)
        setFormError(result.message)
        return
      case 'not_found':
        setClosedMessage(result.message)
        return
      case 'rate_limited':
      case 'failed':
        setFormError(result.message)
        return
    }
  }

  if (closedMessage) return <NotFoundCard message={closedMessage} />

  // Success replaces the whole card. There is deliberately no "send another":
  // a second child means opening the link again, which keeps the server's
  // 24-hour duplicate rule meaningful (§2.6).
  if (thankYou !== null) {
    return (
      <StateCard
        icon={<CheckCircle2 className="h-12 w-12 text-emerald-500" />}
        title="Rahmat!"
        text={thankYou}
      >
        {survey.offerUrl && <OfferLink url={survey.offerUrl} />}
      </StateCard>
    )
  }

  return (
    <Card>
      {survey.imageUrl && !bannerBroken && (
        <img
          src={survey.imageUrl}
          alt=""
          onError={() => setBannerBroken(true)}
          className="mb-4 h-40 w-full rounded-xl bg-slate-100 object-cover"
        />
      )}

      <h1 className="text-xl font-semibold text-slate-800">{survey.name}</h1>
      {survey.subtitle && <p className="mt-1 text-base text-slate-500">{survey.subtitle}</p>}

      <form onSubmit={(event) => void handleSubmit(event)} className="relative mt-5 space-y-4" noValidate>
        <TextField
          label="Ota-onaning ismi"
          required
          value={values.parentFirstName}
          onValueChange={(value) => setValue('parentFirstName', value)}
          error={errors.parentFirstName}
          autoComplete="given-name"
          placeholder="Aziz"
        />
        <TextField
          label="Ota-onaning familiyasi"
          value={values.parentLastName}
          onValueChange={(value) => setValue('parentLastName', value)}
          error={errors.parentLastName}
          autoComplete="family-name"
          placeholder="Karimov"
        />
        <PhoneField
          label="Telefon raqami"
          required
          digits={values.parentPhone}
          onDigitsChange={(value) => setValue('parentPhone', value)}
          error={errors.parentPhone}
          hint="Shu raqamga qo'ng'iroq qilamiz"
          autoComplete="tel-national"
        />

        {fields.studentFirstName && (
          <TextField
            label="O'quvchining ismi"
            required
            value={values.studentFirstName}
            onValueChange={(value) => setValue('studentFirstName', value)}
            error={errors.studentFirstName}
            placeholder="Ali"
          />
        )}
        {fields.studentLastName && (
          <TextField
            label="O'quvchining familiyasi"
            value={values.studentLastName}
            onValueChange={(value) => setValue('studentLastName', value)}
            error={errors.studentLastName}
            placeholder="Karimov"
          />
        )}
        {fields.studentGender && (
          <RadioPills
            label="Jinsi"
            name="studentGender"
            required
            value={values.studentGender}
            options={GENDER_OPTIONS}
            onValueChange={(value) => setValue('studentGender', value)}
            error={errors.studentGender}
          />
        )}
        {fields.studentGrade && (
          <SelectField
            label="Nechanchi sinfga"
            required
            value={values.studentGrade}
            onValueChange={(value) => setValue('studentGrade', value)}
            error={errors.studentGrade}
          >
            <option value="">Tanlang</option>
            {GRADES.map((grade) => (
              <option key={grade} value={String(grade)}>
                {gradeLabel(grade)}
              </option>
            ))}
          </SelectField>
        )}
        {fields.studentPhone && (
          <PhoneField
            label="O'quvchining telefoni"
            digits={values.studentPhone}
            onDigitsChange={(value) => setValue('studentPhone', value)}
            error={errors.studentPhone}
            hint="Majburiy emas"
          />
        )}

        <Honeypot value={website} onValueChange={setWebsite} />

        {formError && (
          <p role="alert" className="rounded-xl bg-rose-50 px-3.5 py-3 text-sm text-rose-600">
            {formError}
          </p>
        )}

        <Button type="submit" disabled={submitting} className="h-12 w-full">
          {submitting ? (
            <>
              <Loader2 className="h-4 w-4 animate-spin" />
              Yuborilmoqda…
            </>
          ) : (
            'Yuborish'
          )}
        </Button>
      </form>

      {survey.offerUrl && (
        <div className="mt-4 border-t border-slate-100 pt-4">
          <OfferLink url={survey.offerUrl} />
        </div>
      )}
    </Card>
  )
}

export function SurveyPage() {
  const { slug = '' } = useParams<{ slug: string }>()
  const load = useAsync(() => loadPublicSurvey(slug), [slug])

  if (load.loading) {
    return (
      <PageShell>
        <SkeletonCard />
      </PageShell>
    )
  }

  const result = load.data
  if (load.error || !result) {
    return (
      <PageShell>
        <StateCard
          icon={<AlertTriangle className="h-10 w-10 text-rose-400" />}
          title="Sahifani ochib bo'lmadi"
          text={load.error ?? LOAD_FAILED_MESSAGE}
        >
          <RetryButton onRetry={load.refetch} />
        </StateCard>
      </PageShell>
    )
  }

  switch (result.kind) {
    case 'not_found':
      return (
        <PageShell>
          <NotFoundCard message={result.message} />
        </PageShell>
      )
    case 'rate_limited':
      return (
        <PageShell>
          <StateCard
            icon={<AlertTriangle className="h-10 w-10 text-amber-400" />}
            title="Juda ko'p urinish"
            text={result.message}
          >
            <RetryButton onRetry={load.refetch} />
          </StateCard>
        </PageShell>
      )
    case 'failed':
      return (
        <PageShell>
          <StateCard
            icon={<AlertTriangle className="h-10 w-10 text-rose-400" />}
            title="Sahifani ochib bo'lmadi"
            text={result.message}
          >
            <RetryButton onRetry={load.refetch} />
          </StateCard>
        </PageShell>
      )
    case 'ok':
      return (
        <PageShell>
          <SurveyForm slug={slug} survey={result.survey} />
        </PageShell>
      )
  }
}
