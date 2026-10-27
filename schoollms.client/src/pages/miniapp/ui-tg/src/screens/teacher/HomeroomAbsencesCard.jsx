/**
 * SINFIM · KELMAGANLAR — sinf rahbari o'z o'quvchisi kelmagan kunga sabab va izoh yozadi (mijoz,
 * 2026-10-26: "sinf rahbarlari o'zlariga biriktirilgan o'quvchilar kelmagan kunda ular nega
 * kelmagani haqida izoh yoza olsin ... izoh ham sabab ham o'zgartira olsin").
 *
 *   HomeroomAbsencesCard  — "Bugun" dagi karta: bugun nechta o'quvchim kelmadi, nechtasi izohsiz.
 *   HomeroomAbsencesSheet — butun ekran: kun tanlanadi (oxirgi 60 kun), kelmaganlar ro'yxati; o'quvchi
 *                           bosilsa — sabab (ro'yxatdan) va izoh, "Saqlash".
 *
 * Sabab o'zgarsa u o'sha kunning JURNAL yozuvlariga tushadi — davomat foizi, intizomiy ball va ota-ona
 * ko'radigan jurnal shunga ergashadi. Faqat rahbarning o'z o'quvchilari (`/api/teacher/homeroom/absences`).
 */
import { useCallback, useEffect, useRef, useState } from 'react'
import { ChevronRight, MessageSquareText, UserX, X } from 'lucide-react'
import { Card, EmptyState, Hero, Row, Stepper } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { dateTime, fullDate } from '../../lib/format'
import { addDays, todayISO } from '../../lib/weeks'
import { haptic, hapticNotify, showBackButton, useKeyboardOpen } from '../../lib/telegram'

/** Server bilan bir xil: oxirgi shuncha kun ochiladi. */
const MAX_DAYS_BACK = 60

/** "Kun bo'yi" / "2, 3-darslar". */
function lessonsText(row) {
  if (row.status === 'absent') return "Kun bo'yi kelmadi"
  if (row.periods.length === 0) return 'Ayrim darslarga kelmadi'
  return `${row.periods.join(', ')}-${row.periods.length > 1 ? 'darslar' : 'dars'}ga kelmadi`
}

export function HomeroomAbsencesCard() {
  const today = todayISO()
  const q = useAsync(() => teacherApi.homeroomAbsences(today), [today])
  const [open, setOpen] = useState(false)
  const d = q.data

  // Rahbarligi yo'q (yoki xato) — karta yo'q.
  if (q.error || !d || d.groups.length === 0) return null

  const noComment = d.rows.filter((r) => !r.comment).length
  const openSheet = () => {
    haptic('light')
    setOpen(true)
  }

  return (
    <>
      <Card
        title="Sinfim · Kelmaganlar"
        action={
          <button type="button" onClick={openSheet} className="shrink-0 text-[14px] font-semibold text-slate-500">
            Ochish
          </button>
        }
      >
        <div className="pt-1">
          <Row
            lead={<AbsentIcon />}
            title={d.rows.length === 0 ? 'Bugun hamma keldi' : `Bugun ${d.rows.length} ta o'quvchi kelmadi`}
            subtitle={
              d.rows.length === 0
                ? "Oldingi kunlarga ham izoh yozish mumkin"
                : noComment > 0
                  ? `${noComment} tasiga izoh yozilmagan`
                  : 'Hammasiga izoh yozilgan'
            }
            right={<ChevronRight className="h-5 w-5 text-slate-400" />}
            onClick={openSheet}
          />
        </div>
      </Card>

      {open && (
        <HomeroomAbsencesSheet
          onClose={() => {
            setOpen(false)
            q.reload()
          }}
        />
      )}
    </>
  )
}

export function HomeroomAbsencesSheet({ onClose }) {
  const today = todayISO()
  const [date, setDate] = useState(today)
  const q = useAsync(() => teacherApi.homeroomAbsences(date), [date])
  const [selectedId, setSelectedId] = useState(null)
  const keyboardOpen = useKeyboardOpen()

  const back = useCallback(() => (selectedId ? setSelectedId(null) : onClose()), [selectedId, onClose])
  useEffect(() => showBackButton(back), [back])

  const d = q.data
  const selected = d?.rows.find((r) => r.studentId === selectedId) ?? null
  const minDate = addDays(today, -MAX_DAYS_BACK)

  const shiftDay = (n) => {
    haptic('light')
    setSelectedId(null)
    setDate((cur) => addDays(cur, n))
  }

  return (
    <div role="dialog" aria-label="Kelmaganlar" className="fixed inset-0 z-40 overflow-y-auto overscroll-contain bg-ground">
      <div className="pb-[calc(24px+var(--tg-safe-bottom))]">
        <Hero
          title={selected ? selected.fullName : 'Kelmaganlar'}
          subtitle={
            selected
              ? `${selected.group} · ${fullDate(date)}`
              : d
                ? `${d.groups.join(', ')} · ${d.rows.length} ta o'quvchi`
                : undefined
          }
          right={
            <button
              type="button"
              onClick={onClose}
              aria-label="Yopish"
              className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-brand-ink/10 text-brand-ink"
            >
              <X className="h-5 w-5" />
            </button>
          }
        />

        {!selected && (
          <div className="mx-4 mt-3 rounded-2xl bg-white px-2 py-1">
            <Stepper
              label={date === today ? `Bugun · ${fullDate(date)}` : fullDate(date)}
              onPrev={() => shiftDay(-1)}
              onNext={() => shiftDay(1)}
              disabledPrev={date <= minDate}
              disabledNext={date >= today}
            />
          </div>
        )}

        {q.loading && !d ? (
          <p className="px-4 py-6 text-center text-[14px] text-slate-500">Yuklanmoqda…</p>
        ) : q.error ? (
          <p className="px-4 py-6 text-center text-[14px] text-red-600">{q.error}</p>
        ) : selected ? (
          <AbsenceEditor
            key={`${selected.studentId}-${date}`}
            row={selected}
            date={date}
            reasons={d.reasons}
            onSaved={() => {
              setSelectedId(null)
              q.reload()
            }}
          />
        ) : (
          <Card>
            {d.rows.length === 0 ? (
              <EmptyState
                icon={<UserX className="h-9 w-9" />}
                title="Kelmagan o'quvchi yo'q"
                note="Bu kuni o'quvchilaringizning hammasi kelgan yoki davomat hali belgilanmagan."
              />
            ) : (
              d.rows.map((r) => (
                <button
                  key={r.studentId}
                  type="button"
                  onClick={() => {
                    haptic('light')
                    setSelectedId(r.studentId)
                  }}
                  className="flex w-full items-start gap-3 border-t border-slate-100 px-4 py-3 text-left first:border-t-0"
                >
                  <Initial name={r.fullName} />
                  <div className="min-w-0 flex-1">
                    <p className="break-words text-[15px] font-semibold leading-snug">{r.fullName}</p>
                    <p className="text-[13px] text-slate-500">
                      {d.groups.length > 1 ? `${r.group} · ` : ''}
                      {lessonsText(r)}
                    </p>
                    {r.comment ? (
                      <p className="mt-0.5 line-clamp-2 text-[13px] text-slate-700">«{r.comment}»</p>
                    ) : (
                      <p className="mt-0.5 text-[12px] font-medium text-amber-600">Izoh yozilmagan</p>
                    )}
                  </div>
                  <ReasonChip name={reasonName(d.reasons, r.reasonId)} unexcused={r.unexcused} />
                </button>
              ))
            )}
          </Card>
        )}
        {/* Izoh yozilayotganda klaviatura pastni yopadi — "Saqlash" uning ustiga surila olishi uchun joy. */}
        {keyboardOpen && <div aria-hidden="true" className="h-[50vh]" />}
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------ bitta o'quvchi */

function AbsenceEditor({ row, date, reasons, onSaved }) {
  const [reasonId, setReasonId] = useState(row.reasonId ?? '')
  const [comment, setComment] = useState(row.comment ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)
  const saveRef = useRef(null)

  // Klaviatura ochilib bo'lgach "Saqlash" ko'rinadigan joyga suriladi.
  const revealSave = () =>
    window.setTimeout(() => saveRef.current?.scrollIntoView({ block: 'center', behavior: 'smooth' }), 350)

  const reasonChanged = reasonId !== '' && reasonId !== row.reasonId
  const commentChanged = comment.trim() !== (row.comment ?? '')
  const dirty = reasonChanged || commentChanged

  const save = async () => {
    if (busy || !dirty) return
    setBusy(true)
    setError(null)
    try {
      await teacherApi.saveHomeroomAbsence(row.studentId, date, {
        reasonId: reasonChanged ? reasonId : null,
        comment: comment.trim(),
      })
      hapticNotify('success')
      onSaved()
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || 'Saqlanmadi')
      setBusy(false)
    }
  }

  return (
    <>
      <Card>
        <div className="space-y-1 px-4 py-4">
          <p className="text-[15px] font-semibold">{lessonsText(row)}</p>
          <p className="text-[13px] text-slate-500">
            {row.heldLessons} ta darsdan · hozirgi sabab: {reasonName(reasons, row.reasonId)}
          </p>
        </div>
      </Card>

      <Card title="Sabab">
        <div className="grid grid-cols-2 gap-2 px-4 pb-4 pt-2">
          {reasons.map((r) => (
            <button
              key={r.id}
              type="button"
              onClick={() => {
                haptic('light')
                setReasonId(r.id)
              }}
              className={
                'rounded-xl px-3 py-3 text-left text-[14px] font-semibold transition ' +
                (reasonId === r.id ? 'bg-brand-ink text-brand' : 'bg-slate-100 text-slate-700')
              }
            >
              {r.name}
            </button>
          ))}
        </div>
        <p className="px-4 pb-4 text-[12px] text-slate-400">
          Sabab o'sha kunning barcha qoldirilgan darslariga yoziladi — davomat va intizomiy ball shunga qarab hisoblanadi.
        </p>
      </Card>

      <Card title="Izoh">
        <div className="space-y-3 px-4 pb-4 pt-2">
          <textarea
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            onFocus={revealSave}
            rows={3}
            maxLength={1000}
            placeholder="Nega kelmadi? Masalan: ota-onasi qo'ng'iroq qildi, isitmasi bor"
            className="w-full rounded-xl border border-slate-200 bg-white px-3 py-3 text-[15px]"
          />
          {row.comment && row.commentBy && (
            <p className="flex items-center gap-1.5 text-[12px] text-slate-400">
              <MessageSquareText className="h-3.5 w-3.5" />
              {row.commentBy}
              {row.commentAt ? ` · ${dateTime(row.commentAt)}` : ''}
            </p>
          )}
          {error && <p className="text-[13px] text-red-600">{error}</p>}
          <button
            ref={saveRef}
            type="button"
            onClick={save}
            disabled={busy || !dirty}
            className="w-full rounded-2xl bg-brand-ink py-3.5 text-[16px] font-semibold text-brand disabled:opacity-50"
          >
            {busy ? 'Saqlanmoqda…' : 'Saqlash'}
          </button>
        </div>
      </Card>
    </>
  )
}

/* ------------------------------------------------------------------ mayda qismlar */

function reasonName(reasons, id) {
  if (!id) return '—'
  return reasons.find((r) => r.id === id)?.name ?? "O'chirilgan sabab"
}

function ReasonChip({ name, unexcused }) {
  return (
    <span
      className={
        'inline-flex shrink-0 items-center rounded-full px-2.5 py-1 text-[12px] font-medium ' +
        (unexcused ? 'bg-red-50 text-red-700' : 'bg-slate-100 text-slate-600')
      }
    >
      {name}
    </span>
  )
}

function Initial({ name }) {
  return (
    <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-slate-100 text-[16px] font-bold text-slate-600">
      {String(name || '?').trim().charAt(0).toUpperCase()}
    </div>
  )
}

function AbsentIcon() {
  return (
    <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-amber-50 text-amber-600">
      <UserX className="h-5 w-5" />
    </div>
  )
}
