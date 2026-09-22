/**
 * `lobby` (§7.3) — the token is valid, the exam is published and nothing has
 * been drawn yet.
 *
 * The gate EduSchool has and we keep (§7.1 fact 7, §7.8): four rules and a
 * mandatory "I agree"; Start stays disabled until it is ticked. Pressing Start is
 * the moment the paper is drawn, the deadline is fixed and the attempt is bound
 * to this browser — so the rules say exactly those three things.
 */
import { useId, useState } from 'react'
import { BookOpen, Clock, ListChecks, Loader2, Play } from 'lucide-react'
import type { ReactNode } from 'react'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import type { PublicCandidate, PublicExamInfo } from '@/api/publicExamClient'

interface LobbyProps {
  candidate: PublicCandidate | null
  exam: PublicExamInfo
  starting: boolean
  error: string | null
  onStart: () => void
}

const RULES: readonly string[] = [
  "Test boshlangach, vaqt to'xtamaydi. Vaqt tugashi bilan test avtomatik yakunlanadi.",
  "Har bir javob tanlangan zahoti saqlanadi. Aloqa uzilsa yoki sahifa yopilsa, shu havolani qayta oching — javoblaringiz joyida qoladi.",
  "Test faqat boshlangan qurilma va brauzerda davom etadi. Boshqa qurilmadan ochib bo'lmaydi.",
  "Javobni boshqa variantga almashtirish mumkin, lekin tanlovni bekor qilib bo'lmaydi. Testni mustaqil bajaring.",
]

/** 0 is a real grade — nol sinf — not "unknown". */
function gradeLabel(grade: number): string {
  return grade === 0 ? 'Nol sinf' : `${grade}-sinf`
}

function Fact({ icon, value, label }: { icon: ReactNode; value: string; label: string }) {
  return (
    <div className="flex flex-col items-center gap-1 rounded-xl bg-slate-50 px-2 py-3 text-center">
      {icon}
      <span className="text-base font-semibold text-slate-800">{value}</span>
      <span className="text-xs text-slate-500">{label}</span>
    </div>
  )
}

export function Lobby({ candidate, exam, starting, error, onStart }: LobbyProps) {
  const [agreed, setAgreed] = useState(false)
  const agreeId = useId()

  return (
    <Card className="space-y-5">
      <div className="space-y-1">
        <p className="text-sm font-medium text-brand-600">Qabul testi</p>
        <h1 className="break-words text-xl font-semibold text-slate-800">{exam.title}</h1>
        {candidate && (
          <p className="flex flex-wrap items-center gap-2 pt-1 text-base text-slate-600">
            <span className="break-words">{candidate.fullName}</span>
            {candidate.grade !== null && (
              <span className="rounded-full bg-slate-100 px-2.5 py-0.5 text-sm text-slate-500">
                {gradeLabel(candidate.grade)}
              </span>
            )}
          </p>
        )}
      </div>

      <div className="grid grid-cols-3 gap-2">
        <Fact
          icon={<ListChecks className="h-5 w-5 text-slate-400" />}
          value={String(exam.questionCount)}
          label="savol"
        />
        <Fact
          icon={<Clock className="h-5 w-5 text-slate-400" />}
          value={String(exam.timeLimitMin)}
          label="daqiqa"
        />
        <Fact
          icon={<BookOpen className="h-5 w-5 text-slate-400" />}
          value={String(exam.subjects.length)}
          label="fan"
        />
      </div>

      {exam.subjects.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {exam.subjects.map((subject) => (
            <span
              key={subject.id}
              className="rounded-full border border-slate-200 px-3 py-1 text-sm text-slate-600"
            >
              {subject.name}
            </span>
          ))}
        </div>
      )}

      <div>
        <h2 className="mb-2.5 text-sm font-semibold text-slate-700">Test qoidalari</h2>
        <ol className="space-y-2.5">
          {RULES.map((rule, index) => (
            <li key={rule} className="flex gap-3 text-base text-slate-600">
              <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-brand-50 text-sm font-semibold text-brand-600">
                {index + 1}
              </span>
              <span>{rule}</span>
            </li>
          ))}
        </ol>
      </div>

      <label
        htmlFor={agreeId}
        className="flex cursor-pointer items-start gap-3 rounded-xl border border-slate-200 px-3.5 py-3 text-base text-slate-700 transition-colors has-[:checked]:border-brand-500 has-[:checked]:bg-brand-50"
      >
        <input
          id={agreeId}
          type="checkbox"
          checked={agreed}
          onChange={(event) => setAgreed(event.target.checked)}
          disabled={starting}
          className="mt-0.5 h-5 w-5 shrink-0 accent-brand-600"
        />
        <span>Qoidalar bilan tanishdim va ularga roziman</span>
      </label>

      {error && (
        <p role="alert" className="rounded-xl bg-rose-50 px-3.5 py-3 text-sm text-rose-600">
          {error}
        </p>
      )}

      <Button onClick={onStart} disabled={!agreed || starting} className="h-12 w-full text-base">
        {starting ? (
          <>
            <Loader2 className="h-4 w-4 animate-spin" />
            Boshlanmoqda…
          </>
        ) : (
          <>
            <Play className="h-4 w-4" />
            Testni boshlash
          </>
        )}
      </Button>
    </Card>
  )
}
