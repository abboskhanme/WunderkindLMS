/**
 * Qizil ro'yxat — bitta holat: nima uchun ro'yxatga tushgani, ota-ona telefonlari, amallar tarixi
 * va yangi amal (holat + izoh, "Qayta qo'ng'iroq" da — kuni). "Hal bo'ldi" holatni yopadi.
 * docs/modules/attendance-red-list.md.
 */
import { useEffect, useState } from 'react'
import { AlertTriangle, CalendarClock, Phone, Trash2, UserRound } from 'lucide-react'
import type { AxiosError } from 'axios'
import {
  addRedListAction,
  CASE_STATUSES,
  caseStatusMeta,
  deleteRedListAction,
  getRedListCase,
  overdueText,
  type AttendanceCaseDetail,
  type CaseStatus,
} from '@/api/services/attendanceRedList'
import { Drawer } from '@/components/ui/Drawer'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Textarea } from '@/components/ui/Input'
import { DatePicker } from '@/components/ui/DatePicker'
import { cn, formatDate } from '@/lib/utils'
import { addDaysISO } from '@/lib/weeks'

function errorText(err: unknown, fallback: string): string {
  return (err as AxiosError<{ message?: string }>)?.response?.data?.message ?? fallback
}

function todayISO(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function fmtDateTime(iso: string): string {
  return new Date(iso).toLocaleString('uz-UZ', {
    timeZone: 'Asia/Tashkent', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
  })
}

export function StatusBadge({ status }: { status: CaseStatus }) {
  const meta = caseStatusMeta(status)
  return (
    <span className={cn('inline-flex items-center gap-1.5 whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-medium', meta.cls)}>
      <span className={cn('h-1.5 w-1.5 rounded-full', meta.dot)} />
      {meta.label}
    </span>
  )
}

interface DrawerProps {
  caseId: string | null
  windowDays: number
  countExcused: boolean
  onClose: () => void
  /** Holat o'zgardi — ro'yxat qayta yuklansin. */
  onChanged: () => void
}

/** Har holat o'z `key` i bilan ochiladi — forma va ma'lumot oldingi holatdan qolmaydi. */
export function RedListCaseDrawer(props: DrawerProps) {
  if (!props.caseId) return null
  return <CaseDrawer key={props.caseId} {...props} caseId={props.caseId} />
}

function CaseDrawer({ caseId, windowDays, countExcused, onClose, onChanged }: DrawerProps & { caseId: string }) {
  const [detail, setDetail] = useState<AttendanceCaseDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [status, setStatus] = useState<CaseStatus>('qayta_qongiroq')
  const [comment, setComment] = useState('')
  const [callbackOn, setCallbackOn] = useState(() => addDaysISO(todayISO(), 1))
  const [busy, setBusy] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    getRedListCase(caseId)
      .then((d) => active && setDetail(d))
      .catch((e) => active && setError(errorText(e, "Holatni yuklab bo'lmadi.")))
    return () => {
      active = false
    }
  }, [caseId])

  const save = async () => {
    if (busy) return
    if (!comment.trim()) {
      setFormError('Izoh yozing: ota-ona bilan nima gaplashildi yoki nima qilindi.')
      return
    }
    setBusy(true)
    setFormError(null)
    try {
      const next = await addRedListAction(caseId, {
        status,
        comment: comment.trim(),
        callbackOn: status === 'qayta_qongiroq' ? callbackOn || null : null,
      })
      setDetail(next)
      setComment('')
      onChanged()
    } catch (e) {
      setFormError(errorText(e, "Amal saqlanmadi."))
    } finally {
      setBusy(false)
    }
  }

  const remove = async (actionId: string) => {
    if (!window.confirm("Bu amal o'chirilsinmi? Holat oldingi amaldagi holatga qaytadi.")) return
    try {
      setDetail(await deleteRedListAction(actionId))
      onChanged()
    } catch (e) {
      setFormError(errorText(e, "Amal o'chirilmadi."))
    }
  }

  const c = detail?.case
  const what = countExcused ? 'kelmadi' : 'sababsiz kelmadi'
  const reason = c
    ? c.trigger === 'consecutive'
      ? `${c.streak} kun ketma-ket ${what}`
      : `So'nggi ${windowDays} kunda ${c.windowCount} kun ${what}`
    : ''
  const late = c ? overdueText(c) : null

  return (
    <Drawer open onClose={onClose} title={c?.fullName ?? "Qizil ro'yxat"} size="lg">
      {error ? (
        <p className="text-sm text-red-600">{error}</p>
      ) : !c || !detail ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-5">
          <div className="space-y-2">
            <div className="flex flex-wrap items-center gap-2">
              <StatusBadge status={c.status} />
              <span className="text-sm text-slate-500">{c.className || '—'}</span>
              {c.archived && <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-500">Arxivda</span>}
            </div>
            {late && (
              <p className="flex items-center gap-2 rounded-xl bg-red-50 px-3 py-2 text-sm text-red-700">
                <AlertTriangle className="h-4 w-4 shrink-0" />
                {late}
              </p>
            )}
            <p className="text-sm font-medium text-slate-800">{reason}</p>
            <p className="text-sm text-slate-500">
              Ro'yxatga kiritilgan: {formatDate(c.openedOn)}
              {c.lastAbsentOn && <> · oxirgi kelmagan kuni: {formatDate(c.lastAbsentOn)}</>}
            </p>
            {c.returned && c.lastPresentOn && (
              <p className="inline-flex rounded-full bg-emerald-50 px-2.5 py-0.5 text-xs font-medium text-emerald-700">
                Qaytdi — {formatDate(c.lastPresentOn)} kuni darsda
              </p>
            )}
            {c.absentDates.length > 0 && (
              <div className="flex flex-wrap gap-1.5">
                {c.absentDates.map((d) => (
                  <span key={d} className="rounded-lg bg-red-50 px-2 py-0.5 text-xs tabular-nums text-red-700">
                    {formatDate(d).slice(0, 5)}
                  </span>
                ))}
              </div>
            )}
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <div>
              <p className="mb-1 text-xs font-medium uppercase tracking-wide text-slate-400">Ota-ona</p>
              {c.contacts.length === 0 ? (
                <p className="text-sm text-slate-400">Telefon kiritilmagan</p>
              ) : (
                <ul className="space-y-1">
                  {c.contacts.map((p) => (
                    <li key={p.phone} className="text-sm">
                      <a href={`tel:${p.phone.replace(/[^\d+]/g, '')}`} className="inline-flex items-center gap-1.5 font-medium text-brand-700 hover:underline">
                        <Phone className="h-3.5 w-3.5" />
                        {p.phone}
                      </a>
                      <span className="ml-1.5 text-slate-500">
                        {p.name}
                        {p.relation ? ` · ${p.relation}` : ''}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </div>
            <div>
              <p className="mb-1 text-xs font-medium uppercase tracking-wide text-slate-400">Sinf rahbari</p>
              {c.teachers.length === 0 ? (
                <p className="text-sm text-slate-400">Biriktirilmagan</p>
              ) : (
                <ul className="space-y-1">
                  {c.teachers.map((t) => (
                    <li key={t.id} className="flex items-center gap-1.5 text-sm text-slate-700">
                      <UserRound className="h-3.5 w-3.5 text-slate-400" />
                      {t.fullName}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </div>

          {c.closedAt ? (
            <p className="rounded-xl bg-emerald-50 px-3 py-2 text-sm text-emerald-700">
              Hal bo'ldi — {fmtDateTime(c.closedAt)}
              {c.closedByName ? `, ${c.closedByName}` : ''}. O'quvchi yana dars qoldirsa, tizim yangi holat ochadi.
            </p>
          ) : (
            <div className="space-y-3 rounded-2xl border border-slate-200 p-4">
              <p className="text-sm font-semibold text-slate-800">Yangi amal</p>
              <div className="grid grid-cols-2 gap-2">
                {CASE_STATUSES.map((s) => (
                  <button
                    key={s.key}
                    type="button"
                    onClick={() => setStatus(s.key)}
                    className={cn(
                      'flex items-center gap-2 rounded-xl border px-3 py-2 text-left text-sm font-medium transition-colors',
                      status === s.key ? 'border-brand-400 bg-brand-50 text-slate-800' : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                    )}
                  >
                    <span className={cn('h-2 w-2 shrink-0 rounded-full', s.dot)} />
                    {s.label}
                  </button>
                ))}
              </div>
              {status === 'qayta_qongiroq' && (
                <div className="flex items-center gap-2">
                  <CalendarClock className="h-4 w-4 text-slate-400" />
                  <span className="text-sm text-slate-600">Qayta qo'ng'iroq kuni</span>
                  <DatePicker value={callbackOn} onChange={setCallbackOn} min={todayISO()} max={addDaysISO(todayISO(), 60)} className="w-40" />
                </div>
              )}
              <Textarea
                rows={3}
                maxLength={1000}
                value={comment}
                onChange={(e) => setComment(e.target.value)}
                placeholder="Kim bilan gaplashildi, nima kelishildi"
              />
              {formError && <p className="text-sm text-red-600">{formError}</p>}
              <div className="flex justify-end">
                <Button onClick={save} disabled={busy}>
                  {busy ? 'Saqlanmoqda...' : 'Saqlash'}
                </Button>
              </div>
            </div>
          )}

          <div>
            <p className="mb-2 text-xs font-medium uppercase tracking-wide text-slate-400">Tarix</p>
            {detail.actions.length === 0 ? (
              <p className="text-sm text-slate-400">Hali hech kim bog'lanmagan.</p>
            ) : (
              <ol className="space-y-3">
                {detail.actions.map((a) => (
                  <li key={a.id} className="group rounded-xl bg-slate-50 px-3 py-2.5">
                    <div className="flex flex-wrap items-center gap-2">
                      <StatusBadge status={a.status} />
                      {a.callbackOn && <span className="text-xs text-slate-500">qayta: {formatDate(a.callbackOn)}</span>}
                      <span className="ml-auto text-xs text-slate-400">{fmtDateTime(a.createdAt)}</span>
                      <button
                        type="button"
                        onClick={() => remove(a.id)}
                        title="Xato yozilgan amalni o'chirish"
                        className="rounded p-1 text-slate-300 opacity-0 transition-opacity hover:text-red-600 group-hover:opacity-100"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    </div>
                    <p className="mt-1 whitespace-pre-line text-sm text-slate-700">{a.comment}</p>
                    <p className="mt-0.5 text-xs text-slate-400">
                      {a.actorName} · {a.actorType === 'teacher' ? "o'qituvchi" : 'xodim'}
                    </p>
                  </li>
                ))}
              </ol>
            )}
          </div>
        </div>
      )}
    </Drawer>
  )
}
