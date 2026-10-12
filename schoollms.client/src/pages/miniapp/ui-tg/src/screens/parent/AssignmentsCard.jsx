/**
 * VAZIFALAR — o'qituvchi bergan topshiriqlar, o'quvchi va ota-ona tomoni (mijoz, 2026-10-12).
 *
 *   AssignmentsCard  — "Bosh" dagi karta: bajarilmagan vazifalar (eng yaqin muddatlisi tepada).
 *   AssignmentsSheet — butun ekran: ro'yxat, keyin bitta vazifa — tavsif, materiallar va javob.
 *
 * TOPSHIRISH — FAQAT O'QUVCHINING O'Z LOGIN'I (server: `student` roli, o'zi uchun). Ota-ona holat va
 * ballni ko'radi; aka-uka profili tanlangan bo'lsa ham faqat ko'rish. Test bir marta topshiriladi
 * (ball darrov hisoblanadi); yozma, fayl va video muddat ichida qayta topshirilishi mumkin. Muddat
 * o'tgan bo'lsa — o'qituvchi "muddatdan keyin ham qabul qilinsin" degandagina.
 */
import { useCallback, useEffect, useRef, useState } from 'react'
import { BookOpenCheck, Check, ChevronLeft, ChevronRight, Paperclip, X } from 'lucide-react'
import { Badge, Card, EmptyState, Hero, Row } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { getAssignment, getAssignments, submitAssignment, uploadAnswer } from '../../lib/parentApi'
import { dateTime, fullDate } from '../../lib/format'
import { haptic, hapticNotify, showBackButton } from '../../lib/telegram'
import { daysLeft, dueInfo, fileNameOf, formatOf, isLink, isoDay, safeHref } from '../../lib/assignments'

/** Bajarilmaganlar — eng yaqin muddat tepada, muddatsizlar oxirida. */
function pendingFirst(items) {
  const rank = (a) => daysLeft(a.dueDate) ?? Number.MAX_SAFE_INTEGER
  return items.filter((a) => !a.completed).sort((a, b) => rank(a) - rank(b))
}

export function AssignmentsCard({ child, canSubmit }) {
  const q = useAsync(() => getAssignments(child.id), [child.id])
  const [open, setOpen] = useState(false)
  const items = q.data ?? []
  const pending = pendingFirst(items)

  const openSheet = () => {
    haptic('light')
    setOpen(true)
  }

  return (
    <>
      <Card
        title="Vazifalar"
        action={
          items.length > 0 ? (
            <button type="button" onClick={openSheet} className="text-[14px] font-semibold text-slate-500">
              Barchasi
            </button>
          ) : null
        }
      >
        <div className="pt-1">
          {q.loading && !q.data ? (
            <p className="px-4 pb-4 text-[14px] text-slate-400">Yuklanmoqda…</p>
          ) : q.error ? (
            <p className="px-4 pb-4 text-[14px] text-red-600">{q.error}</p>
          ) : pending.length === 0 ? (
            <Row
              lead={<VazifaIcon done />}
              title={items.length ? 'Hamma vazifa topshirilgan' : "Hozircha vazifa yo'q"}
              subtitle={items.length ? `${items.length} ta vazifa` : "O'qituvchi vazifa bersa, shu yerda ko'rinadi"}
              right={items.length ? <ChevronRight className="h-5 w-5 text-slate-400" /> : null}
              onClick={items.length ? openSheet : undefined}
            />
          ) : (
            pending.slice(0, 3).map((a) => (
              <AssignmentRow key={a.id} a={a} onClick={openSheet} />
            ))
          )}
        </div>
      </Card>

      {open && (
        <AssignmentsSheet
          child={child}
          canSubmit={canSubmit}
          onClose={() => {
            setOpen(false)
            q.reload()
          }}
        />
      )}
    </>
  )
}

function VazifaIcon({ done = false, format }) {
  const Icon = format ? formatOf(format).icon : BookOpenCheck
  return (
    <div
      className={
        'flex h-11 w-11 shrink-0 items-center justify-center rounded-xl ' +
        (done ? 'bg-emerald-50 text-emerald-700' : 'bg-brand/25 text-brand-ink')
      }
    >
      {done ? <Check className="h-5 w-5" /> : <Icon className="h-5 w-5" />}
    </div>
  )
}

function AssignmentRow({ a, onClick }) {
  const due = dueInfo(a.dueDate)
  return (
    <Row
      lead={<VazifaIcon format={a.format} done={a.completed} />}
      title={a.title}
      subtitle={[a.subjectName, formatOf(a.format).label].filter(Boolean).join(' · ')}
      right={
        a.completed ? (
          a.score != null ? (
            <Badge tone="success">
              {a.score}/{a.maxScore}
            </Badge>
          ) : (
            <Badge tone="success">Topshirildi</Badge>
          )
        ) : (
          <Badge tone={due.tone}>{due.text}</Badge>
        )
      }
      onClick={onClick}
    />
  )
}

export function AssignmentsSheet({ child, canSubmit, onClose }) {
  const list = useAsync(() => getAssignments(child.id), [child.id])
  const [current, setCurrent] = useState(null)

  const back = useCallback(() => (current ? setCurrent(null) : onClose()), [current, onClose])
  useEffect(() => showBackButton(back), [back])

  const items = list.data ?? []
  const pending = pendingFirst(items)
  const done = items.filter((a) => a.completed)

  return (
    <div role="dialog" aria-label="Vazifalar" className="fixed inset-0 z-40 overflow-y-auto overscroll-contain bg-ground">
      <div className="pb-[calc(24px+var(--tg-safe-bottom))]">
        {current ? (
          <AssignmentDetail
            child={child}
            item={current}
            canSubmit={canSubmit}
            onBack={() => setCurrent(null)}
            onSubmitted={list.reload}
          />
        ) : (
          <>
            <Hero
              title="Vazifalar"
              subtitle={child.fullName}
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
            {list.loading && !list.data ? (
              <p className="px-4 py-6 text-center text-[14px] text-slate-500">Yuklanmoqda…</p>
            ) : list.error ? (
              <p className="px-4 py-6 text-center text-[14px] text-red-600">{list.error}</p>
            ) : items.length === 0 ? (
              <Card>
                <EmptyState icon={<BookOpenCheck className="h-9 w-9" />} title="Hozircha vazifa yo'q" note="O'qituvchi vazifa bersa, shu yerda ko'rinadi." />
              </Card>
            ) : (
              <>
                {pending.length > 0 && (
                  <Card title={`Bajarilmagan · ${pending.length}`}>
                    <div className="pt-1">
                      {pending.map((a) => (
                        <AssignmentRow key={a.id} a={a} onClick={() => setCurrent(a)} />
                      ))}
                    </div>
                  </Card>
                )}
                {done.length > 0 && (
                  <Card title={`Topshirilgan · ${done.length}`}>
                    <div className="pt-1">
                      {done.map((a) => (
                        <AssignmentRow key={a.id} a={a} onClick={() => setCurrent(a)} />
                      ))}
                    </div>
                  </Card>
                )}
              </>
            )}
          </>
        )}
      </div>
    </div>
  )
}

const INPUT =
  'w-full rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5 text-[15px] outline-none focus:border-brand focus:bg-white'

function AssignmentDetail({ child, item, canSubmit, onBack, onSubmitted }) {
  const q = useAsync(() => getAssignment(child.id, item.id), [child.id, item.id])
  const [result, setResult] = useState(null)
  const [again, setAgain] = useState(false)
  const fmt = formatOf(item.format)

  const back = (
    <button
      type="button"
      onClick={onBack}
      aria-label="Orqaga"
      className="flex h-9 items-center gap-1 rounded-full bg-brand-ink px-3 text-[13px] font-semibold text-brand"
    >
      <ChevronLeft className="h-4 w-4" />
      Vazifalar
    </button>
  )

  const d = q.data
  const due = dueInfo(item.dueDate)
  const late = (daysLeft(item.dueDate) ?? 0) < 0
  const closed = late && !item.lateAccept
  const isTest = item.format === 'test'
  const showForm = canSubmit && d && !closed && (!d.completed || (again && !isTest))

  return (
    <>
      <Hero title={item.title} subtitle={`${fmt.label} · ${item.subjectName}`} right={back} />

      <Card>
        <div className="space-y-2 px-4 py-3 text-[14px]">
          <div className="flex flex-wrap gap-2">
            <Badge tone={due.tone}>{item.dueDate ? `${fullDate(isoDay(item.dueDate))} · ${due.text}` : due.text}</Badge>
            <Badge>Maks. {item.maxScore} ball</Badge>
            {isTest && <Badge>{item.questionCount} ta savol</Badge>}
          </div>
          {item.description && <p className="whitespace-pre-wrap break-words text-slate-700">{item.description}</p>}
          {(item.materials || []).map((m, i) => (
            <a
              key={`${m.url}-${i}`}
              href={safeHref(m.url) ?? undefined}
              target="_blank"
              rel="noreferrer"
              className="flex items-center gap-2 rounded-xl bg-slate-50 px-3 py-2 text-[13px] font-semibold text-slate-700"
            >
              <Paperclip className="h-4 w-4 shrink-0 text-slate-400" />
              <span className="min-w-0 flex-1 truncate">{m.name || fileNameOf(m.url)}</span>
            </a>
          ))}
        </div>
      </Card>

      {q.loading && !d ? (
        <p className="px-4 py-6 text-center text-[14px] text-slate-500">Yuklanmoqda…</p>
      ) : q.error ? (
        <p className="px-4 py-6 text-center text-[14px] text-red-600">{q.error}</p>
      ) : (
        <>
          {result && (
            <Card className="bg-emerald-50">
              <div className="flex items-center gap-3 px-4 py-4 text-emerald-800">
                <Check className="h-6 w-6 shrink-0" />
                <p className="text-[15px] font-semibold">
                  {result.total != null
                    ? `Topshirildi: ${result.correctCount}/${result.total} to'g'ri · ${result.score} ball`
                    : "Topshirildi. O'qituvchi tekshirib baho qo'yadi."}
                </p>
              </div>
            </Card>
          )}

          {d.completed && !showForm && <Submitted d={d} isTest={isTest} />}

          {showForm ? (
            <SubmitForm
              item={item}
              detail={d}
              onDone={(res) => {
                setResult(res)
                setAgain(false)
                hapticNotify('success')
                q.reload()
                onSubmitted()
              }}
            />
          ) : (
            !d.completed && (
              <Card>
                <p className="px-4 py-4 text-[14px] text-slate-500">
                  {!canSubmit
                    ? "Vazifani o'quvchi o'z login'i bilan Mini App'da topshiradi. Bu yerda holati va bali ko'rinadi."
                    : "Muddat o'tgan — o'qituvchi kechikib topshirishga ruxsat bermagan."}
                </p>
              </Card>
            )
          )}

          {canSubmit && d.completed && !isTest && !closed && !showForm && (
            <div className="mx-4 mt-3">
              <button
                type="button"
                onClick={() => setAgain(true)}
                className="w-full rounded-2xl bg-white py-3 text-[15px] font-semibold text-slate-700"
              >
                Qayta topshirish
              </button>
            </div>
          )}
        </>
      )}
    </>
  )
}

function Submitted({ d, isTest }) {
  const href = safeHref(d.fileUrl)
  return (
    <Card title="Topshirildi">
      <div className="space-y-2 px-4 pb-4 pt-1 text-[14px]">
        <p className="text-slate-500">{d.submittedAt ? dateTime(d.submittedAt) : ''}</p>
        <p className="text-[16px] font-bold">
          {d.score != null ? `${d.score} / ${d.maxScore} ball` : "Baho hali qo'yilmagan"}
        </p>
        {isTest && <p className="text-slate-500">Test bir marta topshiriladi.</p>}
        {d.answerText && <p className="whitespace-pre-wrap break-words rounded-xl bg-slate-50 px-3 py-2 text-slate-700">{d.answerText}</p>}
        {d.fileUrl &&
          (href ? (
            <a href={href} target="_blank" rel="noreferrer" className="flex items-center gap-2 font-semibold text-brand-ink underline">
              <Paperclip className="h-4 w-4 shrink-0" />
              <span className="min-w-0 truncate">{isLink(d.fileUrl) ? d.fileUrl : fileNameOf(d.fileUrl)}</span>
            </a>
          ) : (
            <p className="break-all text-slate-600">{d.fileUrl}</p>
          ))}
      </div>
    </Card>
  )
}

function SubmitForm({ item, detail, onDone }) {
  const isTest = item.format === 'test'
  const [answers, setAnswers] = useState({})
  const [text, setText] = useState(detail.answerText ?? '')
  const [fileUrl, setFileUrl] = useState(detail.fileUrl ?? '')
  const [fileName, setFileName] = useState(detail.fileUrl && !isLink(detail.fileUrl) ? fileNameOf(detail.fileUrl) : '')
  const [link, setLink] = useState(detail.fileUrl && isLink(detail.fileUrl) ? detail.fileUrl : '')
  const [uploading, setUploading] = useState(false)
  const [sending, setSending] = useState(false)
  const [error, setError] = useState(null)
  const fileRef = useRef(null)
  const questions = detail.questions ?? []

  const onPickFile = async (e) => {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    setUploading(true)
    setError(null)
    try {
      const up = await uploadAnswer(file)
      setFileUrl(up.url)
      setFileName(up.name)
      setLink('')
    } catch (err) {
      setError(err?.message || "Faylni yuklab bo'lmadi")
    } finally {
      setUploading(false)
    }
  }

  const submit = async () => {
    let body
    if (isTest) {
      const missing = questions.filter((x) => answers[x.id] === undefined).length
      if (missing > 0) {
        setError(`${missing} ta savol javobsiz — hammasiga javob bering.`)
        hapticNotify('error')
        return
      }
      body = { answers: questions.map((x) => ({ questionId: x.id, selectedIndex: answers[x.id] })) }
    } else {
      const url = item.format === 'video' && link.trim() ? link.trim() : fileUrl
      if (item.format === 'video' && link.trim() && !isLink(link.trim())) {
        setError('Havola http:// yoki https:// bilan boshlansin.')
        return
      }
      if (!text.trim() && !url) {
        setError(item.format === 'written' ? 'Javobni yozing.' : 'Fayl yuklang yoki javob yozing.')
        hapticNotify('error')
        return
      }
      body = { answerText: text.trim() || null, fileUrl: url || null }
    }
    setSending(true)
    setError(null)
    try {
      onDone(await submitAssignment(item.id, body))
    } catch (e) {
      setError(e?.message || "Topshirib bo'lmadi")
      setSending(false)
    }
  }

  return (
    <>
      {isTest ? (
        questions.map((x, i) => (
          <Card key={x.id}>
            <div className="px-4 py-3">
              <p className="text-[15px] font-semibold">
                {i + 1}. {x.text}
              </p>
              <div className="mt-2 space-y-1.5">
                {x.options.map((o, k) => {
                  const on = answers[x.id] === k
                  return (
                    <button
                      key={k}
                      type="button"
                      aria-pressed={on}
                      onClick={() => {
                        haptic('light')
                        setError(null)
                        setAnswers((prev) => ({ ...prev, [x.id]: k }))
                      }}
                      className={
                        'flex w-full items-center gap-2.5 rounded-xl border px-3 py-2.5 text-left text-[14px] ' +
                        (on ? 'border-brand-ink bg-brand/25 font-semibold' : 'border-slate-200 bg-white')
                      }
                    >
                      <span
                        className={
                          'flex h-6 w-6 shrink-0 items-center justify-center rounded-full border-2 ' +
                          (on ? 'border-brand-ink bg-brand-ink text-brand' : 'border-slate-300')
                        }
                      >
                        {on && <Check className="h-3.5 w-3.5" />}
                      </span>
                      {o}
                    </button>
                  )
                })}
              </div>
            </div>
          </Card>
        ))
      ) : (
        <Card title="Javob">
          <div className="space-y-3 px-4 pb-4 pt-2">
            <textarea
              value={text}
              rows={item.format === 'written' ? 6 : 3}
              maxLength={4000}
              onChange={(e) => {
                setText(e.target.value)
                setError(null)
              }}
              placeholder={item.format === 'written' ? 'Javobingizni yozing…' : 'Izoh (ixtiyoriy)'}
              className={INPUT + ' resize-none'}
            />
            {item.format !== 'written' && (
              <>
                {fileUrl && !link && (
                  <div className="flex items-center gap-2 rounded-xl bg-slate-50 px-3 py-2 text-[14px] font-semibold">
                    <Paperclip className="h-4 w-4 shrink-0 text-slate-400" />
                    <span className="min-w-0 flex-1 truncate">{fileName || fileNameOf(fileUrl)}</span>
                    <button type="button" aria-label="Olib tashlash" onClick={() => setFileUrl('')} className="text-red-600">
                      <X className="h-4 w-4" />
                    </button>
                  </div>
                )}
                <button
                  type="button"
                  disabled={uploading}
                  onClick={() => fileRef.current?.click()}
                  className="flex w-full items-center justify-center gap-2 rounded-xl border border-dashed border-slate-300 py-2.5 text-[14px] font-semibold text-slate-600 disabled:opacity-50"
                >
                  <Paperclip className="h-4 w-4" />
                  {uploading ? 'Yuklanmoqda…' : item.format === 'video' ? 'Video yuklash' : 'Fayl yuklash (rasm, PDF…)'}
                </button>
                <input
                  ref={fileRef}
                  type="file"
                  hidden
                  accept={item.format === 'video' ? 'video/*' : undefined}
                  onChange={onPickFile}
                />
                {item.format === 'video' && (
                  <input
                    type="url"
                    value={link}
                    onChange={(e) => {
                      setLink(e.target.value)
                      setError(null)
                    }}
                    placeholder="yoki havola: https://…"
                    className={INPUT}
                  />
                )}
              </>
            )}
          </div>
        </Card>
      )}

      {error && <p className="mx-6 mt-3 text-[13px] font-medium text-red-600">{error}</p>}
      <div className="mx-4 mt-3">
        <button
          type="button"
          onClick={submit}
          disabled={sending || uploading}
          className="w-full rounded-2xl bg-brand py-3.5 text-[16px] font-bold text-brand-ink disabled:opacity-50"
        >
          {sending ? 'Yuborilmoqda…' : 'Topshirish'}
        </button>
      </div>
    </>
  )
}
