import { useCallback, useEffect, useMemo, useState } from 'react'
import { ArrowRight, Repeat2, X } from 'lucide-react'
import type { Teacher } from '@/types'
import { getTeachers } from '@/api/services/teachers'
import {
  cancelSubstitution,
  createSubstitutions,
  getSubstitutionDay,
  getSubstitutions,
  type Substitution,
  type SubstitutionDay,
} from '@/api/services/substitutions'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { DatePicker } from '@/components/ui/DatePicker'
import { Toast } from '@/components/ui/Toast'
import { fmtDate, todayIso } from '@/components/lesson-analysis/format'

/**
 * O'rindosh ustoz (client, 2026-10-07): a teacher does not teach on a date — the admin names who
 * covers each of their lessons. The substitute sees the lesson in the Mini App, may fill that
 * journal on that day, is paid for it at their own rate, and the absent teacher is not.
 */
export function SubstitutionsPage() {
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [date, setDate] = useState(todayIso())
  const [teacherId, setTeacherId] = useState('')
  const [reason, setReason] = useState('')
  const [day, setDay] = useState<SubstitutionDay | null>(null)
  const [dayLoading, setDayLoading] = useState(false)
  const [picks, setPicks] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [toast, setToast] = useState<string | null>(null)
  const [recent, setRecent] = useState<Substitution[] | null>(null)

  useEffect(() => {
    getTeachers()
      .then((rows) => setTeachers(rows.filter((t) => !t.isArchived).sort((a, b) => a.fullName.localeCompare(b.fullName))))
      .catch(() => setTeachers([]))
  }, [])

  const loadRecent = useCallback(() => {
    getSubstitutions({})
      .then(setRecent)
      .catch(() => setRecent([]))
  }, [])
  useEffect(loadRecent, [loadRecent])

  const loadDay = useCallback(() => {
    if (!teacherId || !date) {
      setDay(null)
      return
    }
    setDayLoading(true)
    setError('')
    getSubstitutionDay(teacherId, date)
      .then((d) => {
        setDay(d)
        setPicks({})
      })
      .catch((e) => {
        setDay(null)
        setError(hrErrorMessage(e, "Darslarni yuklab bo'lmadi"))
      })
      .finally(() => setDayLoading(false))
  }, [teacherId, date])
  // eslint-disable-next-line react-hooks/set-state-in-effect -- ustoz yoki sana o'zgarganda kun qayta yuklanadi (maqsadli)
  useEffect(loadDay, [loadDay])

  const keyOf = (l: { period: number; ownerId: string; subjectId: string; subGroup: number }) =>
    `${l.period}|${l.ownerId}|${l.subjectId}|${l.subGroup}`

  const chosen = useMemo(
    () => (day?.lessons ?? []).filter((l) => picks[keyOf(l)] && picks[keyOf(l)] !== l.substituteTeacherId),
    [day, picks],
  )

  const save = async () => {
    if (!day || chosen.length === 0) return
    setSaving(true)
    setError('')
    try {
      await createSubstitutions({
        date: day.date,
        absentTeacherId: day.teacherId,
        reason: reason.trim() || null,
        items: chosen.map((l) => ({
          period: l.period,
          ownerId: l.ownerId,
          subjectId: l.subjectId,
          subGroup: l.subGroup,
          substituteTeacherId: picks[keyOf(l)],
        })),
      })
      setToast(`${chosen.length} ta darsga o'rindosh tayinlandi — ustozlarga botdan xabar ketdi`)
      loadDay()
      loadRecent()
    } catch (e) {
      setError(hrErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const cancel = async (id: string) => {
    try {
      await cancelSubstitution(id)
      setToast("O'rindosh bekor qilindi")
      loadDay()
      loadRecent()
    } catch (e) {
      setError(hrErrorMessage(e, "Bekor qilib bo'lmadi"))
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">O'rindosh ustozlar</h1>
        <p className="text-sm text-slate-400">
          Ustoz darsga kirmasa — uning o'sha kungi darslariga boshqa ustozni tayinlang. O'rindosh jurnalni to'ldiradi va dars
          uning maoshiga qo'shiladi.
        </p>
      </div>

      <Card className="space-y-4">
        <div className="grid gap-3 sm:grid-cols-[180px_1fr_1fr]">
          <div>
            <span className="mb-1 block text-sm font-medium text-slate-600">Sana</span>
            <DatePicker value={date} onChange={(v: string) => setDate(v)} />
          </div>
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-slate-600">Darsga kirmaydigan ustoz</span>
            <select
              value={teacherId}
              onChange={(e) => setTeacherId(e.target.value)}
              className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
            >
              <option value="">— tanlang —</option>
              {teachers.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.fullName}
                </option>
              ))}
            </select>
          </label>
          <Input label="Sabab (ixtiyoriy)" value={reason} maxLength={300} onChange={(e) => setReason(e.target.value)} placeholder="Kasal, safarda…" />
        </div>

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

        {!teacherId ? (
          <p className="py-6 text-center text-sm text-slate-400">Ustozni tanlang — uning shu kungi darslari chiqadi</p>
        ) : dayLoading ? (
          <Loader label="Yuklanmoqda..." />
        ) : day && day.lessons.length === 0 ? (
          <p className="py-6 text-center text-sm text-slate-400">Bu kunda ustozning dars jadvalida darsi yo'q</p>
        ) : day ? (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-100 text-left text-xs font-medium uppercase tracking-wide text-slate-400">
                  <th className="py-2 pr-3">Dars</th>
                  <th className="py-2 pr-3">Sinf · fan</th>
                  <th className="py-2 pr-3">O'rindosh</th>
                  <th className="py-2" />
                </tr>
              </thead>
              <tbody>
                {day.lessons.map((l) => {
                  const k = keyOf(l)
                  return (
                    <tr key={k} className="border-b border-slate-50 last:border-0">
                      <td className="py-2.5 pr-3 align-middle">
                        <div className="font-medium text-slate-800">{l.period}-dars</div>
                        {l.startTime && (
                          <div className="text-xs text-slate-400">
                            {l.startTime}–{l.endTime}
                          </div>
                        )}
                      </td>
                      <td className="py-2.5 pr-3 align-middle text-slate-700">
                        {l.ownerName} · {l.subjectName}
                        {l.subGroup > 0 && <span className="ml-1 text-xs text-slate-400">({l.subGroup}-guruh)</span>}
                      </td>
                      <td className="py-2.5 pr-3 align-middle">
                        <select
                          value={picks[k] ?? l.substituteTeacherId ?? ''}
                          onChange={(e) => setPicks((p) => ({ ...p, [k]: e.target.value }))}
                          className="w-full min-w-56 rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400"
                        >
                          <option value="">— tayinlanmagan —</option>
                          {day.teachers.map((t) => {
                            const busy = t.busyPeriods.includes(l.period) && t.id !== l.substituteTeacherId
                            return (
                              <option key={t.id} value={t.id} disabled={busy}>
                                {t.fullName}
                                {busy ? ' (band)' : ''}
                              </option>
                            )
                          })}
                        </select>
                      </td>
                      <td className="py-2.5 text-right align-middle">
                        {l.substitutionId && (
                          <button
                            type="button"
                            onClick={() => cancel(l.substitutionId!)}
                            className="inline-flex items-center gap-1 rounded-lg px-2 py-1 text-xs font-medium text-red-600 hover:bg-red-50"
                          >
                            <X className="h-3.5 w-3.5" /> Bekor qilish
                          </button>
                        )}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
            <div className="mt-4 flex justify-end">
              <Button onClick={save} disabled={saving || chosen.length === 0}>
                <Repeat2 className="h-4 w-4" />
                {saving ? 'Saqlanmoqda...' : chosen.length ? `Tayinlash (${chosen.length})` : 'Tayinlash'}
              </Button>
            </div>
          </div>
        ) : null}
      </Card>

      <div>
        <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-slate-400">So'nggi o'rindoshlar (±30 kun)</h2>
        {recent === null ? (
          <Loader label="Yuklanmoqda..." />
        ) : recent.length === 0 ? (
          <Card>
            <p className="py-8 text-center text-sm text-slate-400">Hali o'rindosh tayinlanmagan</p>
          </Card>
        ) : (
          <Card className="divide-y divide-slate-50 p-0">
            {recent.map((s) => (
              <div key={s.id} className="flex flex-wrap items-center gap-x-4 gap-y-1 px-4 py-3 text-sm">
                <div className="w-28 shrink-0">
                  <div className="font-medium text-slate-800">{fmtDate(s.date)}</div>
                  <div className="text-xs text-slate-400">{s.period}-dars</div>
                </div>
                <div className="min-w-40 flex-1 text-slate-700">
                  {s.ownerName} · {s.subjectName}
                  {s.reason && <div className="text-xs text-slate-400">{s.reason}</div>}
                </div>
                <div className="flex min-w-64 flex-1 items-center gap-2 text-slate-600">
                  <span>{s.absentTeacherName}</span>
                  <ArrowRight className="h-4 w-4 shrink-0 text-slate-300" />
                  <span className="font-medium text-slate-800">{s.substituteTeacherName}</span>
                </div>
                <button
                  type="button"
                  onClick={() => cancel(s.id)}
                  className="rounded-lg px-2 py-1 text-xs font-medium text-red-600 hover:bg-red-50"
                >
                  Bekor qilish
                </button>
              </div>
            ))}
          </Card>
        )}
      </div>

      <Toast message={toast} onClose={() => setToast(null)} />
    </div>
  )
}
