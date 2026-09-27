/**
 * Bitta topshiriq kartochkasi (eski `components/TaskCard.tsx`).
 *
 *   ▌ Sarlavha                          [holat]
 *   ▌ Muddat: 18:00   ← holat rangida
 *   ▌ tavsif — kulrang ("Batafsil" bilan yig'iladi)
 *   ▌ [ovozli izoh] [qaytarilgan izoh] [belgilar] [KPI progress] [amallar]
 *
 * Chap chekkadagi chiziq, muddat qatori va holat belgisi BITTA rang manbaidan
 * (`accentOf`) — holat bilan rang hech qachon ajralib qolmaydi. Ranglar — bizning
 * palitra (brend sarig'i, slate, emerald, amber, red).
 *
 * - Tasdiqlanmagan (rahbar yangi bergan) topshiriq — SARIQ; muddati o'tgan ochiq
 *   topshiriq esa baribir QIZIL ("kechikdim" signali "yangi" dan muhimroq).
 * - "Tasdiqlash" bajarishga to'siq EMAS — "Bajardim" bilan yonma-yon turadi.
 * - KPI (`numeric`) uchun raqam maydoni kartochkaning O'ZIDA: kuniga bir necha marta
 *   takrorlanadigan amal ikki bosishga cho'zilmasin.
 * - "Bekor qilish" (reopen) faqat bugungi kunda ko'rinadi.
 */
import { useState } from 'react'
import { CalendarDays, Check, Clock, Paperclip, Undo2 } from 'lucide-react'
import { amountToPayload, dueLabel, formatAmount, parseAmountInput, percentOf, pickVoice, toNumber } from '../../lib/hrFormat'
import { hapticNotify } from '../../lib/telegram'
import { Btn, INPUT_CLASS, ProgressBar } from './shared'
import { VoiceNote } from './VoiceNote'
import { t } from './texts'

/** Tavsif shu uzunlikdan oshsa — "Batafsil" bilan yig'iladi. */
const DESCRIPTION_CLAMP = 110

const STATUS_LABEL = {
  open: 'status.open',
  done: 'status.done',
  returned: 'status.returned',
  missed: 'status.missed',
  cancelled: 'status.cancelled',
}

/** Holat rangi: chekka chizig'i, matn, belgi va yumshoq fon. */
const ACCENT = {
  open: { edge: 'bg-slate-300', ink: 'text-slate-600', pill: 'bg-slate-100 text-slate-600', tint: 'bg-slate-50 text-slate-700' },
  done: { edge: 'bg-emerald-500', ink: 'text-emerald-700', pill: 'bg-emerald-50 text-emerald-700', tint: 'bg-emerald-50 text-emerald-800' },
  returned: { edge: 'bg-amber-500', ink: 'text-amber-700', pill: 'bg-amber-50 text-amber-700', tint: 'bg-amber-50 text-amber-800' },
  fresh: { edge: 'bg-brand', ink: 'text-[#8a6d00]', pill: 'bg-brand/30 text-brand-ink', tint: 'bg-brand/20 text-brand-ink' },
  missed: { edge: 'bg-red-500', ink: 'text-red-600', pill: 'bg-red-50 text-red-600', tint: 'bg-red-50 text-red-700' },
  cancelled: { edge: 'bg-slate-400', ink: 'text-slate-500', pill: 'bg-slate-100 text-slate-500', tint: 'bg-slate-50 text-slate-600' },
}

function accentOf(task, { urgent, fresh }) {
  if (urgent) return ACCENT.missed
  if (fresh) return ACCENT.fresh
  return ACCENT[task.status] ?? ACCENT.open
}

function Chip({ tone = 'neutral', children, title }) {
  const tones = {
    neutral: 'bg-slate-100 text-slate-600',
    danger: 'bg-red-50 text-red-600',
    warn: 'bg-amber-50 text-amber-700',
    fresh: 'bg-brand/35 text-brand-ink',
  }
  return (
    <span title={title} className={'inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-[12px] font-semibold ' + tones[tone]}>
      {children}
    </span>
  )
}

export function TaskCard({ task, timezone, viewedDate, isToday, onComplete, onReopen, onAccept, onAddProgress }) {
  const [expanded, setExpanded] = useState(false)
  const [amount, setAmount] = useState('')
  const [note, setNote] = useState('')
  const [amountError, setAmountError] = useState(null)
  const [adding, setAdding] = useState(false)
  const [reopening, setReopening] = useState(false)
  const [accepting, setAccepting] = useState(false)

  const isOpen = task.status === 'open' || task.status === 'returned'
  const isNumeric = task.kind === 'numeric'
  const canReopen = task.status === 'done' && isToday
  const isUrgent = task.isOverdue === true && isOpen
  // `=== true` ATAYIN: maydon ixtiyoriy, `undefined` — "tugma yo'q".
  const needsAcceptance = task.needsAcceptance === true
  const voice = pickVoice(task)
  const accent = accentOf(task, { urgent: isUrgent, fresh: needsAcceptance })
  const proofs = task.proofs || []

  const description = task.description ?? ''
  const longDescription = description.length > DESCRIPTION_CLAMP
  const shownDescription =
    longDescription && !expanded ? `${description.slice(0, DESCRIPTION_CLAMP).trimEnd()}…` : description

  const due = dueLabel(task.dueAt, timezone, viewedDate)
  const hasInput = isNumeric && isOpen

  const current = toNumber(task.currentAmount)
  const target = toNumber(task.targetAmount)

  const submitAmount = async () => {
    const parsed = parseAmountInput(amount)
    if (parsed === null) {
      setAmountError(t('kpi.invalid_amount'))
      hapticNotify('warning')
      return
    }
    if (parsed === 0) {
      setAmountError(t('kpi.zero_amount'))
      hapticNotify('warning')
      return
    }
    setAmountError(null)
    setAdding(true)
    try {
      await onAddProgress(task, amountToPayload(parsed), note.trim().length > 0 ? note.trim() : null)
      setAmount('')
      setNote('')
    } catch {
      // Xabar ekran darajasida banner bo'lib chiqadi; maydonlar ATAYIN tozalanmaydi.
    } finally {
      setAdding(false)
    }
  }

  const submitReopen = async () => {
    setReopening(true)
    try {
      await onReopen(task)
    } catch {
      /* xabar ekran darajasida */
    } finally {
      setReopening(false)
    }
  }

  const submitAccept = async () => {
    setAccepting(true)
    try {
      await onAccept(task)
    } catch {
      /* xabar ekran darajasida */
    } finally {
      setAccepting(false)
    }
  }

  const progressLabel =
    task.unit === null || task.unit === undefined || task.unit === ''
      ? t('kpi.progress_plain', { current: formatAmount(current), target: formatAmount(target) })
      : t('kpi.progress', { current: formatAmount(current), target: formatAmount(target), unit: task.unit })

  return (
    <article
      className={
        'relative mx-4 mt-3 overflow-hidden rounded-card bg-white py-4 pl-5 pr-4 transition ' +
        // Bosilish effekti FAQAT maydonsiz kartochkada: aks holda raqam yozayotgan barmoq ostida sakraydi.
        (hasInput ? '' : 'active:scale-[0.99]')
      }
    >
      <span aria-hidden="true" className={'absolute inset-y-0 left-0 w-1 ' + accent.edge} />

      <div className="flex items-start gap-2">
        <h3
          className={
            'min-w-0 flex-1 break-words text-[16px] font-semibold leading-snug ' +
            (task.status === 'done' ? 'text-slate-500 line-through decoration-slate-300 decoration-[1.5px]' : '')
          }
        >
          {task.title}
        </h3>
        <span className={'shrink-0 rounded-full px-2.5 py-1 text-[12px] font-semibold ' + accent.pill}>
          {t(STATUS_LABEL[task.status] ?? 'status.open')}
        </span>
      </div>

      <p className={'mt-1.5 flex items-center gap-1.5 text-[13px] font-semibold tabular-nums ' + accent.ink}>
        <CalendarDays className="h-[15px] w-[15px] shrink-0" />
        <span className="min-w-0 break-words">{due === null ? t('tasks.no_due') : t('tasks.due', { value: due })}</span>
      </p>

      {description.length > 0 && (
        <p className="mt-2 whitespace-pre-wrap break-words text-[14px] leading-relaxed text-slate-500">{shownDescription}</p>
      )}
      {longDescription && (
        <button type="button" onClick={() => setExpanded((v) => !v)} className="mt-1 text-[13px] font-semibold text-brand-ink underline decoration-brand decoration-2 underline-offset-2">
          {expanded ? t('tasks.less') : t('tasks.more')}
        </button>
      )}

      {/* Rahbarning ovozli izohi — tavsifdan KEYIN: ikkalasi ham "nima qilish kerak". */}
      {voice && <VoiceNote url={voice.url} durationSeconds={voice.durationSeconds} />}

      {task.status === 'returned' && task.directorComment && (
        <p className={'mt-2.5 flex items-start gap-1.5 rounded-xl px-3 py-2 text-[13px] leading-snug ' + accent.tint}>
          <Undo2 className="mt-px h-[15px] w-[15px] shrink-0" />
          <span className="min-w-0 break-words">{t('tasks.returned_note', { comment: task.directorComment })}</span>
        </p>
      )}
      {task.status === 'done' && task.employeeComment && (
        <p className="mt-2.5 break-words rounded-xl bg-slate-50 px-3 py-2 text-[13px] leading-snug text-slate-600">
          {t('tasks.my_comment', { comment: task.employeeComment })}
        </p>
      )}

      {(needsAcceptance || isUrgent || task.isLate || task.source === 'self' || proofs.length > 0) && (
        <div className="mt-2.5 flex flex-wrap gap-1.5">
          {needsAcceptance && (
            <Chip tone="fresh" title={t('tasks.new_task')}>
              {t('tasks.new_badge')}
            </Chip>
          )}
          {isUrgent && (
            <Chip tone="danger">
              <Clock className="h-[13px] w-[13px]" />
              {t('tasks.overdue')}
            </Chip>
          )}
          {task.isLate && <Chip tone="warn">{t('tasks.late')}</Chip>}
          {task.source === 'self' && <Chip>{t('tasks.self')}</Chip>}
          {proofs.length > 0 && (
            <Chip>
              <Paperclip className="h-[13px] w-[13px]" />
              {t('tasks.proof_count', { count: proofs.length })}
            </Chip>
          )}
        </div>
      )}

      {isNumeric && (
        <ProgressBar percent={percentOf(current, target)} reached={target > 0 && current >= target} label={progressLabel} />
      )}

      {hasInput && (
        <>
          <div className="mt-3 flex gap-2">
            <input
              inputMode="decimal"
              enterKeyHint="done"
              placeholder={t('kpi.amount_placeholder')}
              aria-label={t('kpi.amount_placeholder')}
              value={amount}
              onChange={(e) => {
                setAmount(e.target.value)
                setAmountError(null)
              }}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault()
                  if (amount.trim()) void submitAmount()
                }
              }}
              className={INPUT_CLASS + ' flex-1' + (amountError ? ' ring-2 ring-red-400' : '')}
            />
            <Btn
              variant="primary"
              size="sm"
              loading={adding}
              disabled={amount.trim().length === 0}
              onClick={() => void submitAmount()}
              // Bosilganda maydon fokusni yo'qotmasin — klaviatura yopilib, tugma siljimasin.
              onPointerDown={(e) => e.preventDefault()}
              onMouseDown={(e) => e.preventDefault()}
              className="shrink-0"
            >
              {adding ? t('kpi.adding') : t('kpi.add')}
            </Btn>
          </div>
          {amount.trim().length > 0 && (
            <input
              placeholder={t('kpi.note_placeholder', { optional: t('common.optional') })}
              aria-label={t('kpi.note_placeholder', { optional: t('common.optional') })}
              value={note}
              maxLength={200}
              onChange={(e) => setNote(e.target.value)}
              className={INPUT_CLASS + ' mt-2'}
            />
          )}
          {amountError && <p className="mt-1.5 text-[13px] font-medium text-red-600">{amountError}</p>}
        </>
      )}

      {(isOpen || canReopen || needsAcceptance) && (
        <div className="mt-3.5 flex flex-wrap gap-2">
          {needsAcceptance && (
            <Btn variant="dark" grow loading={accepting} onClick={() => void submitAccept()}>
              {accepting ? t('tasks.accepting') : (
                <span className="inline-flex items-center gap-1.5">
                  <Check className="h-[17px] w-[17px]" />
                  {t('tasks.accept')}
                </span>
              )}
            </Btn>
          )}
          {isOpen && (
            <Btn variant="primary" grow onClick={() => onComplete(task)}>
              {t('tasks.complete')}
            </Btn>
          )}
          {canReopen && (
            <Btn variant="danger" size="sm" grow loading={reopening} onClick={() => void submitReopen()}>
              {reopening ? t('tasks.reopening') : t('tasks.reopen')}
            </Btn>
          )}
        </div>
      )}
    </article>
  )
}
