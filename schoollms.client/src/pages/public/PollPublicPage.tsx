/**
 * The public poll page — `/sorovnoma/:token` (client, 2026-10-15: "so'rovnoma Google form
 * holatda bo'lishi"; docs/modules/surveys-forms.md).
 *
 * Like the enrolment form `/ariza/:slug`: outside `AppLayout`, no session, phone first. It
 * makes two requests — the public GET and POST — and nothing else. The link is shared by
 * the school (Telegram post, QR code at a parents' meeting); a parents/teachers poll can be
 * on it too when the admin switches "Havola orqali ham ochiq" on.
 *
 * One answer per browser: a random id kept in localStorage goes with the answers and the
 * server keeps only its hash. When the poll asks for a name and a phone, one answer per
 * phone number as well.
 */
import { useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { useParams } from 'react-router-dom'
import { AlertTriangle, CheckCircle2, FileSearch, Loader2, Lock, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { useAsync } from '@/hooks/useAsync'
import {
  loadPublicPoll,
  markAnswered,
  respondentToken,
  submitPublicPoll,
  wasAnswered,
  type PublicPoll,
} from '@/api/services/publicPoll'
import { PhoneField, TextField } from './SurveyFields'
import { fullPhone } from './phone'
import { PollQuestionsForm } from './PollFormFields'
import { toAnswerInputs, validateAnswers, type AnswerValue, type Answers } from './pollAnswers'

const CHECK_MESSAGE = 'Javoblarni tekshiring'
const LOAD_FAILED_MESSAGE = "Serverga ulanib bo'lmadi. Internet aloqasini tekshiring."

function Shell({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-screen bg-slate-100 px-4 py-8">
      <div className="mx-auto w-full max-w-xl space-y-4">
        <div className="flex items-center justify-center gap-3">
          {/* Same logo tile as the login page — the school's own yellow. */}
          <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-[#FFD006]">
            <img src="/logo.png" alt="" className="h-7 w-7 object-contain" />
          </div>
          <span className="text-base font-semibold text-slate-800">Wunderkind International School</span>
        </div>
        {children}
      </div>
    </div>
  )
}

function StateCard({ icon, title, text, children }: { icon: ReactNode; title: string; text: string; children?: ReactNode }) {
  return (
    <Card className="flex flex-col items-center gap-3 py-8 text-center">
      {icon}
      <h1 className="text-lg font-semibold text-slate-800">{title}</h1>
      <p className="text-base text-slate-500">{text}</p>
      {children}
    </Card>
  )
}

function RetryButton({ onRetry }: { onRetry: () => void }) {
  return (
    <Button variant="secondary" onClick={onRetry}>
      <RefreshCw className="h-4 w-4" /> Qayta urinish
    </Button>
  )
}

const ClosedCard = ({ title }: { title?: string }) => (
  <StateCard
    icon={<FileSearch className="h-10 w-10 text-slate-300" />}
    title="So'rovnoma yopilgan"
    text={title ? `«${title}» bo'yicha javoblar hozir qabul qilinmaydi.` : 'Javoblar hozir qabul qilinmaydi.'}
  />
)

const ThanksCard = ({ again }: { again?: boolean }) => (
  <StateCard
    icon={<CheckCircle2 className="h-12 w-12 text-emerald-500" />}
    title="Rahmat!"
    text={again ? 'Siz bu so\'rovnomaga javob bergansiz.' : 'Javobingiz qabul qilindi.'}
  />
)

function PollForm({ token, poll }: { token: string; poll: PublicPoll }) {
  const [answers, setAnswers] = useState<Answers>({})
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [name, setName] = useState('')
  const [phone, setPhone] = useState('')
  const [contactErrors, setContactErrors] = useState<{ name?: string; phone?: string }>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [done, setDone] = useState<'sent' | 'again' | 'closed' | null>(wasAnswered(token) ? 'again' : null)

  const change = (questionId: string, value: AnswerValue) => {
    setAnswers((a) => ({ ...a, [questionId]: value }))
    setErrors((e) => {
      if (!e[questionId]) return e
      const rest = { ...e }
      delete rest[questionId]
      return rest
    })
  }

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()
    if (submitting) return
    const local = validateAnswers(poll.questions, answers)
    const contact: { name?: string; phone?: string } = {}
    if (poll.collectContact) {
      if (name.trim().length < 2) contact.name = 'Ismingizni kiriting'
      if (phone.length < 9) contact.phone = "Telefon raqamini to'liq kiriting"
    }
    setErrors(local)
    setContactErrors(contact)
    if (Object.keys(local).length > 0 || Object.keys(contact).length > 0) {
      setFormError(CHECK_MESSAGE)
      return
    }

    setFormError(null)
    setSubmitting(true)
    const result = await submitPublicPoll(token, {
      answers: toAnswerInputs(poll.questions, answers),
      respondentToken: respondentToken(),
      ...(poll.collectContact ? { contactName: name.trim(), contactPhone: fullPhone(phone) } : {}),
    })
    setSubmitting(false)
    switch (result.kind) {
      case 'ok':
        markAnswered(token)
        setDone('sent')
        window.scrollTo({ top: 0 })
        return
      case 'answered':
        markAnswered(token)
        setDone('again')
        return
      case 'closed':
      case 'not_found':
        setDone('closed')
        return
      default:
        setFormError(result.message)
    }
  }

  if (done === 'closed') return <ClosedCard title={poll.title} />
  if (done) return <ThanksCard again={done === 'again'} />

  return (
    <form onSubmit={(event) => void handleSubmit(event)} className="space-y-4" noValidate>
      <Card className="border-t-4 border-t-[#FFD006]">
        <h1 className="text-xl font-semibold text-slate-800">{poll.title}</h1>
        {poll.description && <p className="mt-2 whitespace-pre-line text-base text-slate-600">{poll.description}</p>}
        {poll.anonymous && (
          <p className="mt-3 flex items-center gap-1.5 text-sm text-slate-400">
            <Lock className="h-4 w-4" /> So'rovnoma anonim — ismingiz so'ralmaydi
          </p>
        )}
      </Card>

      {poll.collectContact && (
        <Card className="space-y-4">
          <TextField
            label="Ism va familiya"
            required
            value={name}
            maxLength={100}
            onValueChange={(v) => {
              setName(v)
              setContactErrors((e) => ({ ...e, name: undefined }))
            }}
            error={contactErrors.name}
            autoComplete="name"
          />
          <PhoneField
            label="Telefon raqami"
            required
            digits={phone}
            onDigitsChange={(v) => {
              setPhone(v)
              setContactErrors((e) => ({ ...e, phone: undefined }))
            }}
            error={contactErrors.phone}
            autoComplete="tel-national"
          />
        </Card>
      )}

      <PollQuestionsForm
        questions={poll.questions}
        answers={answers}
        errors={errors}
        onChange={change}
      />

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
  )
}

export function PollPublicPage() {
  const { token = '' } = useParams<{ token: string }>()
  const load = useAsync(() => loadPublicPoll(token), [token])

  if (load.loading)
    return (
      <Shell>
        <Card className="space-y-4">
          <div className="h-6 w-2/3 animate-pulse rounded bg-slate-100" />
          <div className="h-4 w-1/2 animate-pulse rounded bg-slate-100" />
          {[0, 1, 2].map((row) => (
            <div key={row} className="h-12 w-full animate-pulse rounded-xl bg-slate-100" />
          ))}
        </Card>
      </Shell>
    )

  const result = load.data
  if (load.error || !result || result.kind === 'failed' || result.kind === 'rate_limited')
    return (
      <Shell>
        <StateCard
          icon={<AlertTriangle className="h-10 w-10 text-rose-400" />}
          title={result?.kind === 'rate_limited' ? "Juda ko'p urinish" : "Sahifani ochib bo'lmadi"}
          text={result && result.kind !== 'ok' && result.kind !== 'not_found' ? result.message : (load.error ?? LOAD_FAILED_MESSAGE)}
        >
          <RetryButton onRetry={load.refetch} />
        </StateCard>
      </Shell>
    )

  if (result.kind === 'not_found')
    return (
      <Shell>
        <StateCard
          icon={<FileSearch className="h-10 w-10 text-slate-300" />}
          title={result.message}
          text="Havola noto'g'ri yoki eskirgan bo'lishi mumkin. Maktab bilan bog'laning."
        />
      </Shell>
    )

  return (
    <Shell>{result.poll.accepting ? <PollForm token={token} poll={result.poll} /> : <ClosedCard title={result.poll.title} />}</Shell>
  )
}
