/**
 * The public vacancy page (client, 2026-10-07: "vakansiya qoldirgan xodimlar").
 *
 *   /vakansiya         — the open vacancies
 *   /vakansiya/:slug   — one vacancy and the application form (with a CV)
 *
 * No session, like the enrolment form `/ariza/:slug`: the link goes to Telegram, Instagram or a
 * job site. The server serves this SPA for `/vakansiya` on any host.
 */
import { useRef, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Briefcase, CheckCircle2, ChevronRight, FileText, Loader2, Paperclip } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { useAsync } from '@/hooks/useAsync'
import {
  applyToVacancy,
  getPublicVacancies,
  getPublicVacancy,
  publicErrorMessage,
  type PublicVacancy,
} from '@/api/services/recruitment'
import { Honeypot, PhoneField, TextField } from './SurveyFields'
import { fullPhone, phoneDigits } from './phone'

function Shell({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-screen bg-slate-100 px-4 py-8">
      <div className="mx-auto w-full max-w-xl space-y-4">
        <div className="flex items-center justify-center gap-3">
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

export function VacancyListPage() {
  const q = useAsync(() => getPublicVacancies(), [])
  return (
    <Shell>
      <Card className="space-y-1">
        <h1 className="text-lg font-semibold text-slate-800">Bo'sh ish o'rinlari</h1>
        <p className="text-sm text-slate-500">Jamoamizga qo'shiling — vakansiyani tanlab, ariza qoldiring.</p>
      </Card>
      {q.loading ? (
        <Card>
          <Loader2 className="mx-auto h-6 w-6 animate-spin text-slate-400" />
        </Card>
      ) : (q.data ?? []).length === 0 ? (
        <Card>
          <p className="py-6 text-center text-sm text-slate-500">Hozircha ochiq vakansiya yo'q.</p>
        </Card>
      ) : (
        <Card className="divide-y divide-slate-100 p-0">
          {(q.data ?? []).map((v) => (
            <Link key={v.slug} to={`/vakansiya/${v.slug}`} className="flex items-center gap-3 px-4 py-4 hover:bg-slate-50">
              <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-[#FFD006]/30 text-slate-800">
                <Briefcase className="h-5 w-5" />
              </div>
              <div className="min-w-0 flex-1">
                <p className="font-medium text-slate-800">{v.title}</p>
                {v.department && <p className="text-sm text-slate-500">{v.department}</p>}
              </div>
              <ChevronRight className="h-5 w-5 text-slate-300" />
            </Link>
          ))}
        </Card>
      )}
    </Shell>
  )
}

export function VacancyPage() {
  const { slug = '' } = useParams()
  const q = useAsync(() => getPublicVacancy(slug), [slug])
  return (
    <Shell>
      {q.loading ? (
        <Card>
          <Loader2 className="mx-auto h-6 w-6 animate-spin text-slate-400" />
        </Card>
      ) : q.error || !q.data ? (
        <Card className="space-y-3 text-center">
          <p className="text-sm text-slate-600">Vakansiya topilmadi yoki yopilgan.</p>
          <Link to="/vakansiya" className="text-sm font-medium text-brand-700">
            Boshqa vakansiyalar
          </Link>
        </Card>
      ) : (
        <>
          <VacancyCard v={q.data} />
          <ApplyForm slug={slug} />
        </>
      )}
    </Shell>
  )
}

function VacancyCard({ v }: { v: PublicVacancy }) {
  return (
    <Card className="space-y-3">
      <div>
        <h1 className="text-lg font-semibold text-slate-800">{v.title}</h1>
        {v.department && <p className="text-sm text-slate-500">{v.department}</p>}
      </div>
      {v.description && <p className="whitespace-pre-line text-sm text-slate-700">{v.description}</p>}
      {v.requirements && (
        <div>
          <p className="mb-1 text-sm font-semibold text-slate-700">Talablar</p>
          <p className="whitespace-pre-line text-sm text-slate-700">{v.requirements}</p>
        </div>
      )}
    </Card>
  )
}

function ApplyForm({ slug }: { slug: string }) {
  const [fullName, setFullName] = useState('')
  const [digits, setDigits] = useState('')
  const [email, setEmail] = useState('')
  const [experience, setExperience] = useState('')
  const [about, setAbout] = useState('')
  const [website, setWebsite] = useState('')
  const [cv, setCv] = useState<File | null>(null)
  const [sending, setSending] = useState(false)
  const [sent, setSent] = useState(false)
  const [error, setError] = useState('')
  const fileRef = useRef<HTMLInputElement>(null)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!fullName.trim()) return setError('F.I.Sh. kiriting')
    if (phoneDigits(digits).length !== 9) return setError("Telefon raqamini to'liq kiriting")
    if (cv && cv.size > 10_000_000) return setError('CV 10 MB dan katta')
    setSending(true)
    setError('')
    try {
      await applyToVacancy(slug, { fullName: fullName.trim(), phone: fullPhone(digits), email, experience, about, website }, cv)
      setSent(true)
    } catch (err) {
      setError(publicErrorMessage(err, "Yuborib bo'lmadi — qayta urinib ko'ring"))
    } finally {
      setSending(false)
    }
  }

  if (sent)
    return (
      <Card className="space-y-2 text-center">
        <CheckCircle2 className="mx-auto h-10 w-10 text-emerald-500" />
        <p className="font-semibold text-slate-800">Arizangiz qabul qilindi!</p>
        <p className="text-sm text-slate-500">HR bo'limi arizangizni ko'rib chiqib, siz bilan bog'lanadi.</p>
      </Card>
    )

  return (
    <Card>
      <form onSubmit={submit} className="relative space-y-4">
        <h2 className="text-base font-semibold text-slate-800">Ariza qoldirish</h2>
        <TextField label="F.I.Sh." required value={fullName} onValueChange={setFullName} maxLength={200} autoComplete="name" />
        <PhoneField label="Telefon" required digits={digits} onDigitsChange={setDigits} autoComplete="tel-national" />
        <TextField label="Email" value={email} onValueChange={setEmail} type="email" maxLength={200} autoComplete="email" />
        <label className="block">
          <span className="mb-1.5 block text-sm font-medium text-slate-700">Ish tajribangiz</span>
          <textarea
            value={experience}
            onChange={(e) => setExperience(e.target.value)}
            rows={3}
            maxLength={2000}
            placeholder="Qayerda, qancha yil, qaysi lavozimda"
            className="w-full rounded-xl border border-slate-200 px-3.5 py-2.5 text-sm text-slate-800 outline-none focus:border-brand-400"
          />
        </label>
        <label className="block">
          <span className="mb-1.5 block text-sm font-medium text-slate-700">O'zingiz haqingizda</span>
          <textarea
            value={about}
            onChange={(e) => setAbout(e.target.value)}
            rows={3}
            maxLength={4000}
            placeholder="Ta'lim, sertifikatlar, nega aynan biz"
            className="w-full rounded-xl border border-slate-200 px-3.5 py-2.5 text-sm text-slate-800 outline-none focus:border-brand-400"
          />
        </label>
        <div>
          <span className="mb-1.5 block text-sm font-medium text-slate-700">Rezyume (CV)</span>
          <button
            type="button"
            onClick={() => fileRef.current?.click()}
            className="flex w-full items-center gap-2 rounded-xl border border-dashed border-slate-300 px-3.5 py-3 text-sm text-slate-600 hover:bg-slate-50"
          >
            {cv ? <FileText className="h-4 w-4 text-brand-600" /> : <Paperclip className="h-4 w-4" />}
            <span className="truncate">{cv ? cv.name : 'PDF, Word yoki rasm — 10 MB gacha'}</span>
          </button>
          <input
            ref={fileRef}
            type="file"
            accept=".pdf,.doc,.docx,.jpg,.jpeg,.png"
            className="hidden"
            onChange={(e) => setCv(e.target.files?.[0] ?? null)}
          />
        </div>
        <Honeypot value={website} onValueChange={setWebsite} />
        {error && (
          <p role="alert" className="rounded-xl bg-rose-50 px-3.5 py-3 text-sm text-rose-600">
            {error}
          </p>
        )}
        <Button type="submit" disabled={sending} className="w-full justify-center">
          {sending ? 'Yuborilmoqda...' : 'Ariza yuborish'}
        </Button>
      </form>
    </Card>
  )
}
