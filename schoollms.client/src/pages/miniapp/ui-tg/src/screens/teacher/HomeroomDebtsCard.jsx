/**
 * SINFIM — sinf rahbarining o'quvchilari va ularning qarzi (mijoz, 2026-10-12: "o'qituvchi sinf
 * rahbar ham bo'lsa ... o'zini rahbarligidagi guruh yoki sinfini ko'radimi asosan qarzdorliklar uchun").
 *
 *   HomeroomDebtsCard  — "Bugun" dagi karta: jami qarz, qarzdorlar soni va eng katta uchtasi.
 *   HomeroomDebtsSheet — butun ekran: qarzdorlar (yoki hamma) ro'yxati — to'lanmagan oylar va
 *                        ota-ona telefoni (eslatish uchun bosib qo'ng'iroq qilinadi). Sinf / guruh
 *                        bo'yicha filtr va Excel (2026-10-12): Telegram ichida fayl bot orqali
 *                        o'qituvchining o'z chatiga keladi, brauzerda — to'g'ridan-to'g'ri yuklanadi.
 *
 * Faqat rahbarga: rahbarligi yo'q o'qituvchida karta umuman chizilmaydi (server ham bo'sh
 * ro'yxat qaytaradi). Faqat ko'rish — to'lov va chegirmalar bu yerda yo'q. Raqamlar admin'dagi
 * "Qarzdorlar" hisoboti bilan bir xil (`GET /api/teacher/homeroom/debts`).
 */
import { useCallback, useEffect, useState } from 'react'
import { ChevronRight, FileSpreadsheet, Phone, Users, Wallet, X } from 'lucide-react'
import { Badge, Card, EmptyState, Hero, Row, Segmented, StatGrid } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { money, monthTitle, sum } from '../../lib/format'
import { haptic, hapticNotify, inTelegram, showBackButton } from '../../lib/telegram'

/** "2026-09-01" → "Sentabr" (shu yil) yoki "Sentabr 2025" (boshqa yil). */
function monthLabel(iso) {
  const [y, m] = String(iso).split('-').map(Number)
  if (!y || !m) return iso
  const label = monthTitle(y, m - 1)
  return y === new Date().getFullYear() ? label.split(' ')[0] : label
}

/** Telefon qo'ng'iroq uchun: faqat raqamlar va boshidagi "+". */
function telHref(phone) {
  const clean = String(phone).replace(/[^\d+]/g, '')
  return clean ? `tel:${clean}` : null
}

export function HomeroomDebtsCard() {
  const q = useAsync(() => teacherApi.homeroomDebts(), [])
  const [open, setOpen] = useState(false)
  const d = q.data

  // Rahbarligi yo'q o'qituvchi — karta yo'q (xato bo'lsa ham "Bugun" ekrani buzilmasin).
  if (q.error || (d && d.groups.length === 0)) return null

  const debtors = d ? d.pupils.filter((p) => p.debt > 0) : []
  const openSheet = () => {
    haptic('light')
    setOpen(true)
  }

  return (
    <>
      <Card
        title={d ? `Sinfim · ${d.groups.join(', ')}` : 'Sinfim'}
        action={
          d && d.pupilCount > 0 ? (
            <button type="button" onClick={openSheet} className="shrink-0 text-[14px] font-semibold text-slate-500">
              Barchasi
            </button>
          ) : null
        }
      >
        <div className="pt-1">
          {!d ? (
            <p className="px-4 pb-4 text-[14px] text-slate-400">Yuklanmoqda…</p>
          ) : debtors.length === 0 ? (
            <Row
              lead={<DebtIcon clear />}
              title="Qarzdor yo'q"
              subtitle={`${d.pupilCount} ta o'quvchi — hammasi to'lagan`}
              right={d.pupilCount > 0 ? <ChevronRight className="h-5 w-5 text-slate-400" /> : null}
              onClick={d.pupilCount > 0 ? openSheet : undefined}
            />
          ) : (
            <>
              <Row
                lead={<DebtIcon />}
                title={money(d.totalDebt)}
                subtitle={`${d.debtorCount} ta qarzdor · ${d.pupilCount} ta o'quvchidan`}
                right={<ChevronRight className="h-5 w-5 text-slate-400" />}
                onClick={openSheet}
              />
              {debtors.slice(0, 3).map((p) => (
                <Row
                  key={p.studentId}
                  lead={<Initial name={p.fullName} />}
                  title={p.fullName}
                  subtitle={monthsLine(p)}
                  right={<span className="shrink-0 text-[15px] font-bold text-red-600">{sum(p.debt)}</span>}
                  onClick={openSheet}
                />
              ))}
            </>
          )}
        </div>
      </Card>

      {open && d && (
        <HomeroomDebtsSheet
          onClose={() => {
            setOpen(false)
            q.reload()
          }}
        />
      )}
    </>
  )
}

export function HomeroomDebtsSheet({ onClose }) {
  const q = useAsync(() => teacherApi.homeroomDebts(), [])
  const [filter, setFilter] = useState('debtors')
  /** Sinf yoki guruh nomi; '' — hammasi. */
  const [group, setGroup] = useState('')
  const [excel, setExcel] = useState({ busy: false, note: null, error: null })

  const back = useCallback(() => onClose(), [onClose])
  useEffect(() => showBackButton(back), [back])

  const d = q.data
  const manyGroups = (d?.groups.length ?? 0) > 1
  // Hamma raqam tanlangan sinf/guruh bo'yicha — ekran va Excel bir xil narsani ko'rsatsin.
  const inGroup = d ? d.pupils.filter((p) => !group || p.group === group) : []
  const debtors = inGroup.filter((p) => p.debt > 0)
  const shown = filter === 'debtors' ? debtors : inGroup
  const total = debtors.reduce((acc, p) => acc + p.debt, 0)

  const pickGroup = (g) => {
    haptic('light')
    setGroup(g)
    setExcel({ busy: false, note: null, error: null })
  }

  const exportExcel = async () => {
    if (excel.busy) return
    setExcel({ busy: true, note: null, error: null })
    const params = { group: group || undefined, all: filter === 'all' }
    try {
      if (inTelegram) {
        await teacherApi.homeroomDebtsToTelegram(params)
        hapticNotify('success')
        setExcel({ busy: false, note: "Excel fayl Telegram'dagi bot chatingizga yuborildi.", error: null })
      } else {
        await teacherApi.downloadHomeroomDebts(params)
        setExcel({ busy: false, note: null, error: null })
      }
    } catch (e) {
      hapticNotify('error')
      setExcel({ busy: false, note: null, error: e?.message || "Excel faylni olib bo'lmadi" })
    }
  }

  return (
    <div role="dialog" aria-label="Sinfim" className="fixed inset-0 z-40 overflow-y-auto overscroll-contain bg-ground">
      <div className="pb-[calc(24px+var(--tg-safe-bottom))]">
        <Hero
          title="Sinfim"
          subtitle={d ? `${d.groups.join(', ')} · ${d.pupilCount} ta o'quvchi` : undefined}
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
        >
          {/* O'tkagich sariq fon uchun chizilgan (Segmented) — shuning uchun Hero ichida. */}
          {d && (
            <Segmented
              value={filter}
              onChange={setFilter}
              options={[
                { value: 'debtors', label: `Qarzdorlar · ${debtors.length}` },
                { value: 'all', label: `Hammasi · ${inGroup.length}` },
              ]}
            />
          )}
        </Hero>

        {q.loading && !d ? (
          <p className="px-4 py-6 text-center text-[14px] text-slate-500">Yuklanmoqda…</p>
        ) : q.error ? (
          <p className="px-4 py-6 text-center text-[14px] text-red-600">{q.error}</p>
        ) : (
          <>
            {/* Sinf / guruh filtri — bir nechta rahbarlik bo'lsagina (2026-10-12). */}
            {manyGroups && (
              <div className="mt-3 flex gap-2 overflow-x-auto px-4">
                {['', ...d.groups].map((g) => {
                  const count = d.pupils.filter((p) => (!g || p.group === g) && p.debt > 0).length
                  const active = group === g
                  return (
                    <button
                      key={g || 'all'}
                      type="button"
                      onClick={() => pickGroup(g)}
                      className={
                        'shrink-0 rounded-full px-3.5 py-2 text-[14px] font-semibold transition ' +
                        (active ? 'bg-brand-ink text-brand' : 'bg-white text-slate-600')
                      }
                    >
                      {g || 'Barchasi'} · {count}
                    </button>
                  )
                })}
              </div>
            )}

            <StatGrid
              items={[
                { label: "Jami qarz, so'm", value: sum(total) },
                { label: 'Qarzdorlar', value: `${debtors.length}/${inGroup.length}`, note: "o'quvchidan" },
              ]}
            />

            <Card>
              {shown.length === 0 ? (
                <EmptyState icon={<Users className="h-9 w-9" />} title="Qarzdor yo'q" note="Hamma o'quvchi to'lagan." />
              ) : (
                shown.map((p) => <PupilRow key={p.studentId} p={p} showGroup={manyGroups} />)
              )}
            </Card>

            {/* Excel — ekrandagi filtr bilan (sinf/guruh, qarzdorlar yoki hammasi). */}
            <div className="mx-4 mt-3">
              <button
                type="button"
                onClick={exportExcel}
                disabled={excel.busy || shown.length === 0}
                className="flex w-full items-center justify-center gap-2 rounded-card bg-white px-4 py-3.5 text-[15px] font-semibold text-slate-800 disabled:opacity-50"
              >
                <FileSpreadsheet className="h-5 w-5 text-emerald-600" />
                {excel.busy ? 'Tayyorlanmoqda…' : inTelegram ? "Excel'ni Telegram'ga yuborish" : 'Excel yuklab olish'}
              </button>
              {excel.note && <p className="mt-2 text-center text-[13px] text-emerald-700">{excel.note}</p>}
              {excel.error && <p className="mt-2 text-center text-[13px] text-red-600">{excel.error}</p>}
            </div>

            <p className="mx-6 mt-3 text-center text-[12px] text-slate-400">
              Qarz buxgalteriyadagi bilan bir xil. To'lov faqat kassada qabul qilinadi.
            </p>
          </>
        )}
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------ qatorlar */

function PupilRow({ p, showGroup }) {
  const tel = p.parentPhone ? telHref(p.parentPhone) : null
  return (
    <div className="flex w-full items-start gap-3 border-t border-slate-100 px-4 py-3 first:border-t-0">
      <Initial name={p.fullName} />
      <div className="min-w-0 flex-1">
        <p className="break-words text-[15px] font-semibold leading-snug">{p.fullName}</p>
        {showGroup && <p className="text-[12px] text-slate-400">{p.group}</p>}
        {p.unpaidMonths.length > 0 && (
          <div className="mt-1.5 flex flex-wrap gap-1.5">
            {p.unpaidMonths.map((m) => (
              <span key={m.month} className="rounded-lg bg-red-50 px-2 py-0.5 text-[12px] text-red-700">
                {monthLabel(m.month)}: {sum(m.amount)}
              </span>
            ))}
          </div>
        )}
        {tel && (
          <a href={tel} className="mt-1.5 inline-flex items-center gap-1.5 text-[13px] font-medium text-slate-600">
            <Phone className="h-3.5 w-3.5" />
            {p.parentPhone}
          </a>
        )}
      </div>
      {p.debt > 0 ? (
        <div className="shrink-0 text-right">
          <p className="text-[15px] font-bold text-red-600">{sum(p.debt)}</p>
          {p.daysOverdue > 0 && <p className="text-[11px] text-red-400">{p.daysOverdue} kun kechikkan</p>}
        </div>
      ) : (
        <Badge tone="success">Qarzi yo'q</Badge>
      )}
    </div>
  )
}

function monthsLine(p) {
  const months = p.unpaidMonths.map((m) => monthLabel(m.month))
  return months.length > 0 ? `To'lanmagan: ${months.join(', ')}` : 'Qarzi bor'
}

function Initial({ name }) {
  return (
    <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-slate-100 text-[16px] font-bold text-slate-600">
      {String(name || '?').trim().charAt(0).toUpperCase()}
    </div>
  )
}

function DebtIcon({ clear = false }) {
  return (
    <div
      className={
        'flex h-11 w-11 shrink-0 items-center justify-center rounded-xl ' +
        (clear ? 'bg-emerald-50 text-emerald-700' : 'bg-red-50 text-red-600')
      }
    >
      <Wallet className="h-5 w-5" />
    </div>
  )
}
