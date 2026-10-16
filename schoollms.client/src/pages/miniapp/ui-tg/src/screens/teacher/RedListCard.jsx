/**
 * SINFIM · QIZIL RO'YXAT — ketma-ket yoki ko'p sababsiz kelmagan o'quvchilar va ota-ona bilan aloqa
 * (mijoz, 2026-10-15: "Sinf rahbari va maktab rahbariyatiga ota-ona bilan bog'lanish talabi avtomatik
 * ochilsin. Holatlar: Aloqa kerak, Qayta qo'ng'iroq, Hal bo'ldi, Bog'lanib bo'lmadi").
 *
 *   RedListCard  — "Bugun" dagi karta: faqat ochiq holat bo'lsa chiziladi; nechta o'quvchi va eng
 *                  muhim uchtasi (muddati o'tganlar tepada).
 *   RedListSheet — butun ekran: holatlar ro'yxati; o'quvchi bosilsa — uning sahifasi: kelmagan kunlar,
 *                  ota-ona telefoni (bosib qo'ng'iroq), tarix va amal (holat + izoh).
 *
 * Faqat rahbarning o'z o'quvchilari (rahbarlikdagi sinf va yo'nalish guruhi) — server tekshiradi
 * (`GET /api/teacher/homeroom/red-list`). Ota-onaga hech narsa yuborilmaydi: qo'ng'iroqni ustoz qiladi.
 */
import { useCallback, useEffect, useState } from 'react'
import { AlertTriangle, CalendarClock, ChevronRight, Phone, Siren, X } from 'lucide-react'
import { Card, EmptyState, Hero, Row } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { dateTime, dayMonth } from '../../lib/format'
import { todayISO } from '../../lib/weeks'
import { haptic, hapticNotify, showBackButton } from '../../lib/telegram'

const STATUSES = [
  { key: 'aloqa_kerak', label: 'Aloqa kerak', cls: 'bg-red-50 text-red-700', dot: 'bg-red-500' },
  { key: 'qayta_qongiroq', label: "Qayta qo'ng'iroq", cls: 'bg-amber-50 text-amber-700', dot: 'bg-amber-500' },
  { key: 'boglanib_bolmadi', label: "Bog'lanib bo'lmadi", cls: 'bg-violet-50 text-violet-700', dot: 'bg-violet-500' },
  { key: 'hal_boldi', label: "Hal bo'ldi", cls: 'bg-emerald-50 text-emerald-700', dot: 'bg-emerald-500' },
]
const statusOf = (key) => STATUSES.find((s) => s.key === key) ?? STATUSES[0]

/** "3 kun ketma-ket sababsiz kelmadi" / "14 kunda 5 kun sababsiz kelmadi". */
function reasonText(c, list) {
  const what = list.countExcused ? 'kelmadi' : 'sababsiz kelmadi'
  return c.trigger === 'consecutive'
    ? `${c.streak} kun ketma-ket ${what}`
    : `${list.windowDays} kunda ${c.windowCount} kun ${what}`
}

function overdueText(c) {
  if (c.overdueReason === 'unreachable') return "Bog'lanib bo'lmadi — rahbariyat ko'radi"
  if (c.overdueReason === 'callback_due') return "Qayta qo'ng'iroq kuni o'tdi"
  if (c.overdueReason === 'no_action') return `${c.daysWithoutAction} kundan beri amal yo'q`
  return null
}

function telHref(phone) {
  const clean = String(phone).replace(/[^\d+]/g, '')
  return clean ? `tel:${clean}` : null
}

function addDays(iso, n) {
  const d = new Date(`${iso}T00:00:00`)
  d.setDate(d.getDate() + n)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

export function RedListCard() {
  const q = useAsync(() => teacherApi.homeroomRedList(), [])
  const [open, setOpen] = useState(false)
  const d = q.data

  // Holat yo'q (yoki xato) — karta umuman yo'q: "Bugun" ekrani faqat ish bo'lganda eslatadi.
  if (q.error || !d || d.cases.length === 0) return null

  const openSheet = () => {
    haptic('light')
    setOpen(true)
  }

  return (
    <>
      <Card
        title="Sinfim · Qizil ro'yxat"
        className="ring-2 ring-red-200"
        action={
          <button type="button" onClick={openSheet} className="shrink-0 text-[14px] font-semibold text-slate-500">
            Barchasi
          </button>
        }
      >
        <div className="pt-1">
          <Row
            lead={<RedIcon />}
            title={`${d.cases.length} ta o'quvchi`}
            subtitle={
              d.overdue > 0
                ? `Ota-ona bilan bog'lanish kerak · ${d.overdue} tasi kechikkan`
                : "Ota-ona bilan bog'lanish kerak"
            }
            right={<ChevronRight className="h-5 w-5 text-slate-400" />}
            onClick={openSheet}
          />
          {d.cases.slice(0, 3).map(({ case: c }) => (
            <Row
              key={c.id}
              lead={<Initial name={c.fullName} />}
              title={c.fullName}
              subtitle={reasonText(c, d)}
              right={<StatusChip status={c.status} />}
              onClick={openSheet}
            />
          ))}
        </div>
      </Card>

      {open && (
        <RedListSheet
          onClose={() => {
            setOpen(false)
            q.reload()
          }}
        />
      )}
    </>
  )
}

export function RedListSheet({ onClose }) {
  const q = useAsync(() => teacherApi.homeroomRedList(), [])
  const [selectedId, setSelectedId] = useState(null)

  const back = useCallback(() => (selectedId ? setSelectedId(null) : onClose()), [selectedId, onClose])
  useEffect(() => showBackButton(back), [back])

  const d = q.data
  const selected = d?.cases.find((x) => x.case.id === selectedId) ?? null

  return (
    <div role="dialog" aria-label="Qizil ro'yxat" className="fixed inset-0 z-40 overflow-y-auto overscroll-contain bg-ground">
      <div className="pb-[calc(24px+var(--tg-safe-bottom))]">
        <Hero
          title={selected ? selected.case.fullName : "Qizil ro'yxat"}
          subtitle={
            selected
              ? `${selected.case.group} · ${reasonText(selected.case, d)}`
              : d
                ? `${d.groups.join(', ')} · ${d.cases.length} ta o'quvchi`
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

        {q.loading && !d ? (
          <p className="px-4 py-6 text-center text-[14px] text-slate-500">Yuklanmoqda…</p>
        ) : q.error ? (
          <p className="px-4 py-6 text-center text-[14px] text-red-600">{q.error}</p>
        ) : selected ? (
          <CaseView
            key={selected.case.id}
            item={selected}
            list={d}
            onSaved={(closed) => {
              if (closed) setSelectedId(null)
              q.reload()
            }}
          />
        ) : (
          <>
            <Card>
              {d.cases.length === 0 ? (
                <EmptyState
                  icon={<Siren className="h-9 w-9" />}
                  title="Ro'yxat bo'sh"
                  note="Hozir ota-ona bilan bog'lanish kerak bo'lgan o'quvchi yo'q."
                />
              ) : (
                d.cases.map(({ case: c }) => (
                  <button
                    key={c.id}
                    type="button"
                    onClick={() => {
                      haptic('light')
                      setSelectedId(c.id)
                    }}
                    className="flex w-full items-start gap-3 border-t border-slate-100 px-4 py-3 text-left first:border-t-0"
                  >
                    <Initial name={c.fullName} />
                    <div className="min-w-0 flex-1">
                      <p className="break-words text-[15px] font-semibold leading-snug">{c.fullName}</p>
                      <p className="text-[13px] text-slate-500">
                        {d.groups.length > 1 ? `${c.group} · ` : ''}
                        {reasonText(c, d)}
                      </p>
                      {c.returned && c.lastPresentOn && (
                        <p className="text-[12px] font-medium text-emerald-700">Qaytdi — {dayMonth(c.lastPresentOn)}</p>
                      )}
                      {overdueText(c) && (
                        <p className="mt-0.5 flex items-center gap-1 text-[12px] font-medium text-red-600">
                          <AlertTriangle className="h-3.5 w-3.5" />
                          {overdueText(c)}
                        </p>
                      )}
                    </div>
                    <StatusChip status={c.status} />
                  </button>
                ))
              )}
            </Card>
            <p className="mx-6 mt-3 text-center text-[12px] text-slate-400">
              Ro'yxatga {d.consecutiveDays} kun ketma-ket yoki {d.windowDays} kunda {d.windowUnexcused} kun
              {d.countExcused ? ' kelmagan' : ' sababsiz kelmagan'} o'quvchi tushadi. "Hal bo'ldi" holatni yopadi.
            </p>
          </>
        )}
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------ bitta o'quvchi */

function CaseView({ item, list, onSaved }) {
  const c = item.case
  const today = todayISO()
  const [status, setStatus] = useState('qayta_qongiroq')
  const [comment, setComment] = useState('')
  const [callbackOn, setCallbackOn] = useState(addDays(today, 1))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)
  const late = overdueText(c)

  const save = async () => {
    if (busy) return
    if (!comment.trim()) {
      setError('Izoh yozing: ota-ona bilan nima gaplashildi.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await teacherApi.addRedListAction(c.id, {
        status,
        comment: comment.trim(),
        callbackOn: status === 'qayta_qongiroq' ? callbackOn || null : null,
      })
      hapticNotify('success')
      setComment('')
      onSaved(status === 'hal_boldi')
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || 'Saqlanmadi')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <Card>
        <div className="space-y-2 px-4 py-4">
          <div className="flex flex-wrap items-center gap-2">
            <StatusChip status={c.status} />
            {c.status === 'qayta_qongiroq' && c.callbackOn && (
              <span className="text-[13px] text-slate-500">qayta: {dayMonth(c.callbackOn)}</span>
            )}
          </div>
          {late && (
            <p className="flex items-center gap-2 rounded-xl bg-red-50 px-3 py-2 text-[13px] font-medium text-red-700">
              <AlertTriangle className="h-4 w-4 shrink-0" />
              {late}
            </p>
          )}
          {c.returned && c.lastPresentOn && (
            <p className="text-[13px] font-medium text-emerald-700">Qaytdi — {dayMonth(c.lastPresentOn)} kuni darsda</p>
          )}
          {c.absentDates.length > 0 && (
            <div>
              <p className="mb-1.5 text-[13px] text-slate-500">Kelmagan kunlari</p>
              <div className="flex flex-wrap gap-1.5">
                {c.absentDates.map((day) => (
                  <span key={day} className="rounded-lg bg-red-50 px-2 py-0.5 text-[12px] text-red-700">
                    {dayMonth(day)}
                  </span>
                ))}
              </div>
            </div>
          )}
        </div>
      </Card>

      <Card title="Ota-ona">
        {c.contacts.length === 0 ? (
          <p className="px-4 pb-4 text-[14px] text-slate-400">Telefon kiritilmagan — maktab ma'muriyatidan so'rang.</p>
        ) : (
          <div className="pt-1">
            {c.contacts.map((p) => (
              <a
                key={p.phone}
                href={telHref(p.phone) ?? undefined}
                className="flex items-center gap-3 border-t border-slate-100 px-4 py-3 first:border-t-0"
              >
                <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-emerald-50 text-emerald-700">
                  <Phone className="h-5 w-5" />
                </div>
                <div className="min-w-0 flex-1">
                  <p className="text-[15px] font-semibold">{p.phone}</p>
                  <p className="text-[13px] text-slate-500">
                    {p.name}
                    {p.relation ? ` · ${p.relation}` : ''}
                  </p>
                </div>
              </a>
            ))}
          </div>
        )}
      </Card>

      <Card title="Natija">
        <div className="space-y-3 px-4 pb-4 pt-2">
          <div className="grid grid-cols-2 gap-2">
            {STATUSES.map((s) => (
              <button
                key={s.key}
                type="button"
                onClick={() => {
                  haptic('light')
                  setStatus(s.key)
                }}
                className={
                  'flex items-center gap-2 rounded-xl px-3 py-3 text-left text-[14px] font-semibold transition ' +
                  (status === s.key ? 'bg-brand-ink text-brand' : 'bg-slate-100 text-slate-700')
                }
              >
                <span className={'h-2 w-2 shrink-0 rounded-full ' + s.dot} />
                {s.label}
              </button>
            ))}
          </div>
          {status === 'qayta_qongiroq' && (
            <label className="flex items-center gap-2 text-[14px] text-slate-600">
              <CalendarClock className="h-4 w-4 shrink-0 text-slate-400" />
              Qayta qo'ng'iroq kuni
              <input
                type="date"
                value={callbackOn}
                min={today}
                max={addDays(today, 60)}
                onChange={(e) => setCallbackOn(e.target.value)}
                className="ml-auto rounded-xl border border-slate-200 bg-white px-3 py-2 text-[15px]"
              />
            </label>
          )}
          <textarea
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            rows={3}
            maxLength={1000}
            placeholder="Kim bilan gaplashdingiz, nima kelishildi?"
            className="w-full rounded-xl border border-slate-200 bg-white px-3 py-3 text-[15px]"
          />
          {error && <p className="text-[13px] text-red-600">{error}</p>}
          <button
            type="button"
            onClick={save}
            disabled={busy}
            className="w-full rounded-2xl bg-brand-ink py-3.5 text-[16px] font-semibold text-brand disabled:opacity-50"
          >
            {busy ? 'Saqlanmoqda…' : 'Saqlash'}
          </button>
        </div>
      </Card>

      <Card title="Tarix">
        {item.actions.length === 0 ? (
          <p className="px-4 pb-4 text-[14px] text-slate-400">Hali hech kim bog'lanmagan.</p>
        ) : (
          <div className="pt-1">
            {item.actions.map((a) => (
              <div key={a.id} className="border-t border-slate-100 px-4 py-3 first:border-t-0">
                <div className="flex items-center gap-2">
                  <StatusChip status={a.status} />
                  <span className="ml-auto text-[12px] text-slate-400">{dateTime(a.createdAt)}</span>
                </div>
                <p className="mt-1 whitespace-pre-line text-[14px] text-slate-700">{a.comment}</p>
                <p className="text-[12px] text-slate-400">
                  {a.actorName}
                  {a.callbackOn ? ` · qayta: ${dayMonth(a.callbackOn)}` : ''}
                </p>
              </div>
            ))}
          </div>
        )}
      </Card>
      <p className="mx-6 mt-3 text-center text-[12px] text-slate-400">{list.groups.join(', ')}</p>
    </>
  )
}

/* ------------------------------------------------------------------ mayda qismlar */

function StatusChip({ status }) {
  const s = statusOf(status)
  return (
    <span className={'inline-flex shrink-0 items-center gap-1.5 rounded-full px-2.5 py-1 text-[12px] font-medium ' + s.cls}>
      <span className={'h-1.5 w-1.5 rounded-full ' + s.dot} />
      {s.label}
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

function RedIcon() {
  return (
    <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-red-50 text-red-600">
      <Siren className="h-5 w-5" />
    </div>
  )
}
