/**
 * BOSH — bugungi kun bitta ekranda.
 *
 * Ota-ona kuniga bir marta ochsa, aynan shu ekranni ochadi va besh soniya
 * qaraydi. Shuning uchun bu yerda faqat BUGUN javob beradigan savollar bor:
 * bolam maktabdami, bugun nima dars, pul qarzimiz bormi, maktabdan xabar
 * bormi va "kelib oldim" tugmasi.
 *
 * QARZ BO'LSA U YUQORIGA CHIQADI. Pul kartochkasining o'rni qat'iy emas:
 * qarz bo'lsa u darslardan OLDIN, bo'lmasa keyin turadi. Yaxshi xabarni
 * pastga surish mumkin, yomonini — yo'q.
 *
 * BESHTA SO'ROV BIR VAQTDA ketadi va bittasi yiqilsa butun tab xato holatiga
 * o'tadi. Ular bitta serverga boradi: bittasi yiqilgan bo'lsa, qolgani ham
 * ishonchsiz — yarim to'ldirilgan ekran ota-onani chalg'itadi.
 */
import { useEffect, useState } from 'react'
import {
  AlertTriangle, CalendarOff, CheckCircle2, Clock, DoorOpen, HelpCircle,
  Megaphone, Wallet,
} from 'lucide-react'
import { Badge, Card, DateChip, EmptyState, Row, StatGrid } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { dateTime, money, sum } from '../../lib/format'
import { haptic } from '../../lib/telegram'
import {
  getAnnouncements, getAttendance, getBilling, getDashboard, getPickup, requestPickup,
} from '../../lib/parentApi'
import { AsyncBlock, balanceShort, gradeTone, monthLabel } from './shared'
import { NewsCard } from './NewsCard'
import { todayISO } from '../../lib/weeks'

export function HomeTab({ child, onOpenTab, showGrades = true }) {
  const state = useAsync(
    () =>
      Promise.all([
        getDashboard(child.id),
        getBilling(child.id),
        getAttendance(child.id),
        getAnnouncements(child.id),
        getPickup(child.id),
      ]).then(([dashboard, billing, attendance, announcements, pickup]) => ({
        dashboard, billing, attendance, announcements, pickup,
      })),
    [child.id],
  )

  return (
    <AsyncBlock state={state} loadingLabel="Bugungi ma'lumot yuklanmoqda…">
      {({ dashboard, billing, attendance, announcements, pickup }) => {
        const lessons = dashboard.todayLessons ?? []
        const grades = dashboard.todayGrades ?? []
        const today = todayISO()
        const todayRows = (attendance.rows ?? []).filter((r) => r.date === today)
        const owes = billing.debt > 0

        const moneyCard = (
          <MoneyCard billing={billing} onOpenTab={onOpenTab} />
        )

        return (
          <>
            <TodayCard lessons={lessons} grades={grades} rows={todayRows} />

            <StatGrid
              items={[
                { label: 'Bugungi darslar', value: String(lessons.length) },
                // §5.5 — maktab baholarni yopgan bo'lsa "0 ta baho" ko'rsatilmaydi: bu yolg'on bo'lardi.
                showGrades && {
                  label: 'Bugungi baholar',
                  value: String(grades.length),
                  note: grades.length ? grades.map((g) => g.grade).join(', ') : null,
                },
                {
                  label: 'Bajarilmagan vazifa',
                  value: String(dashboard.pendingAssignmentsCount ?? 0),
                },
                balanceShort(billing.debt, billing.credit),
              ].filter(Boolean)}
            />

            {owes && moneyCard}

            <LessonsCard lessons={lessons} grades={grades} rows={todayRows} />

            {!owes && moneyCard}

            <PickupCard childId={child.id} initial={pickup} />

            <AnnouncementsCard items={announcements} />

            {/* School news, under the announcements block (§3.4). It loads
                itself, so it does not join the five requests above. */}
            <NewsCard />
          </>
        )
      }}
    </AsyncBlock>
  )
}

/* ------------------------------------------------------- bugun maktabdami */

/**
 * "Bolam maktabdami" degan savolga eng aniq javob — jurnal. Davomat DARS
 * bo'yicha belgilanadi, shuning uchun uch xil holat bor va ular
 * ARALASHTIRILMAYDI: qoldirgan, kech qolgan, hali belgilanmagan.
 * "Belgilanmagan" ni "keldi" deb ko'rsatish — ota-onani aldash.
 */
function todayStatus(lessons, grades, rows) {
  const absent = rows.filter((r) => !r.isLate)
  const late = rows.filter((r) => r.isLate)

  if (absent.length > 0) {
    const reasons = [...new Set(absent.map((r) => r.reasonName).filter(Boolean))].join(', ')
    return {
      tone: 'danger',
      icon: AlertTriangle,
      title: `Bugun ${absent.length} ta darsda yo'q`,
      note: reasons ? `Sabab: ${reasons}` : 'Sabab ko\'rsatilmagan.',
    }
  }
  if (late.length > 0) {
    return {
      tone: 'brand',
      icon: Clock,
      title: 'Bugun darsga kech qoldi',
      note: `${late.length} ta darsda kechikish belgilangan.`,
    }
  }
  if (lessons.length === 0) {
    return {
      tone: 'neutral',
      icon: CalendarOff,
      title: "Bugun dars yo'q",
      note: "Jadvalda bugunga dars belgilanmagan — dam olish kuni yoki ta'til.",
    }
  }
  if (grades.length > 0) {
    return {
      tone: 'success',
      icon: CheckCircle2,
      title: 'Bugun maktabda',
      note: `Darslarda qatnashdi, ${grades.length} ta baho oldi.`,
    }
  }
  return {
    tone: 'neutral',
    icon: HelpCircle,
    title: 'Davomat hali belgilanmagan',
    note: "O'qituvchi jurnalni to'ldirgach shu yerda ko'rinadi.",
  }
}

const TONE_BG = {
  danger: 'bg-red-50 text-red-600',
  success: 'bg-emerald-50 text-emerald-600',
  brand: 'bg-brand/25 text-brand-ink',
  neutral: 'bg-slate-100 text-slate-500',
}

function TodayCard({ lessons, grades, rows }) {
  const s = todayStatus(lessons, grades, rows)
  const Icon = s.icon
  const first = lessons[0]
  const last = lessons[lessons.length - 1]

  return (
    <Card>
      <div className="flex items-start gap-3 px-4 pb-4 pt-4">
        <div className={'flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl ' + TONE_BG[s.tone]}>
          <Icon className="h-6 w-6" />
        </div>
        <div className="min-w-0 flex-1">
          <p className="text-[17px] font-bold leading-snug">{s.title}</p>
          <p className="mt-0.5 text-[13px] leading-snug text-slate-500">{s.note}</p>
          {first && last && (
            <p className="mt-2 text-[13px] font-medium text-slate-600">
              Darslar: {first.startTime ?? '—'} – {last.endTime ?? '—'}
            </p>
          )}
        </div>
      </div>
    </Card>
  )
}

/* --------------------------------------------------------- bugungi darslar */

function LessonsCard({ lessons, grades, rows }) {
  if (lessons.length === 0) {
    return (
      <Card title="Bugungi darslar">
        <EmptyState
          title="Bugun dars yo'q"
          note="Jadvalga bugungi kunga dars qo'yilmagan. Butun haftani «Jadval» bo'limida ko'rishingiz mumkin."
        />
      </Card>
    )
  }

  const gradeOf = new Map(grades.map((g) => [g.period, g]))
  const rowOf = new Map(rows.map((r) => [r.period, r]))

  return (
    <Card title="Bugungi darslar">
      {/* Guruh darsi sinf jadvalida yo'q — nomini ko'rsatmasak, ota-ona
          "bu dars qayerdan chiqdi?" degan savolga javob topolmaydi (G-18). */}
      {lessons.map((l) => {
        const g = gradeOf.get(l.period)
        const absence = rowOf.get(l.period)
        return (
          <Row
            key={`${l.ownerKind ?? 'class'}-${l.period}-${l.subjectId}`}
            lead={<DateChip day={l.period} month="dars" tone={absence ? 'danger' : 'neutral'} />}
            title={l.subjectName}
            subtitle={[
              l.startTime && l.endTime ? `${l.startTime}–${l.endTime}` : null,
              l.teacherName,
              l.ownerKind === 'group' && l.ownerName ? `Guruh: ${l.ownerName}` : null,
            ].filter(Boolean).join(' · ')}
            right={
              absence ? (
                <Badge tone={absence.isLate ? 'brand' : 'danger'}>{absence.reasonName}</Badge>
              ) : g?.grade ? (
                <Badge tone={gradeTone(g.grade)}>Baho {g.grade}</Badge>
              ) : null
            }
          />
        )
      })}
    </Card>
  )
}

/* -------------------------------------------------------------------- pul */

/**
 * Qarz bo'lsa — QANCHA, QAYSI OY, QAYSI TOIFA va nima qilish kerak.
 * "Qarzingiz bor" degan yolg'iz jumla ota-onani kassaga olib bormaydi.
 */
function MoneyCard({ billing, onOpenTab }) {
  if (billing.debt > 0) {
    const lines = billing.debtLines ?? []
    const overdue = lines.some((l) => l.isOverdue)
    return (
      <Card>
        <div className="px-4 pb-4 pt-4">
          <div className="flex items-center gap-3">
            <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-red-50 text-red-600">
              <Wallet className="h-6 w-6" />
            </div>
            <div className="min-w-0 flex-1">
              <p className="text-[13px] text-slate-500">Qarz</p>
              <p className="text-[24px] font-extrabold leading-tight text-red-600">
                {money(billing.debt)}
              </p>
            </div>
            {overdue && <Badge tone="danger">Muddati o'tgan</Badge>}
          </div>

          <div className="mt-3 space-y-1.5">
            {lines.slice(0, 3).map((l) => (
              <div key={`${l.periodMonth}-${l.categoryCode}`} className="flex items-baseline gap-2">
                <span className="min-w-0 flex-1 truncate text-[14px] text-slate-600">
                  {monthLabel(l.periodMonth)} · {l.categoryName}
                </span>
                <span className="shrink-0 text-[14px] font-semibold">{sum(l.remaining)}</span>
              </div>
            ))}
            {lines.length > 3 && (
              <p className="text-[13px] text-slate-400">va yana {lines.length - 3} ta qator</p>
            )}
          </div>

          <button
            type="button"
            onClick={() => {
              haptic('medium')
              onOpenTab('finance')
            }}
            className="mt-4 w-full rounded-2xl bg-brand py-3.5 text-[15px] font-bold text-brand-ink"
          >
            To'lovni ko'rish
          </button>
          <p className="mt-2 text-center text-[12px] text-slate-400">
            To'lov maktab kassasida qabul qilinadi, chek shu ilovada saqlanadi.
          </p>
        </div>
      </Card>
    )
  }

  if (billing.credit > 0) {
    return (
      <Card>
        <Row
          lead={
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-emerald-50 text-emerald-600">
              <Wallet className="h-5 w-5" />
            </div>
          }
          title={`Avans: ${money(billing.credit)}`}
          subtitle="Ortiqcha to'langan pul keyingi oy hisobiga o'tadi."
          right={<Badge tone="success">Qarz yo'q</Badge>}
        />
      </Card>
    )
  }

  // Qarz ham, avans ham nolga teng bo'lishining IKKI sababi bor va ular bir
  // xil emas: hammasi to'langan yoki hali hisob-faktura chiqarilmagan.
  const billed = (billing.months ?? []).length > 0

  return (
    <Card>
      <Row
        lead={
          <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-emerald-50 text-emerald-600">
            <CheckCircle2 className="h-5 w-5" />
          </div>
        }
        title={billed ? "Qarz yo'q" : "Hisob-faktura yo'q"}
        subtitle={
          billed
            ? "Barcha hisob-fakturalar to'langan."
            : "Bu farzand uchun hali oylik hisob-faktura chiqarilmagan."
        }
        right={billed ? <Badge tone="success">Toza</Badge> : null}
      />
    </Card>
  )
}

/* --------------------------------------------- farzandni olib ketish (pickup) */

/**
 * "Farzandimni olib ketaman" AYNAN SHU YERDA turadi: ota-ona maktab
 * darvozasida turib ilovani ochadi va bitta bosishda sinf rahbariga xabar
 * beradi. Uni «Jadval» yoki «To'lov» ichiga yashirish — o'sha lahzada
 * ilovani foydasiz qilish.
 *
 * So'rov KUNLIK: server bugungi "pending" so'rov bo'lsa yangisini yaratmaydi,
 * shuning uchun tugmani ikki marta bosish xavfsiz.
 */
function PickupCard({ childId, initial }) {
  const [pickup, setPickup] = useState(initial)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  // Tab qayta yuklansa — serverning javobi ustun.
  useEffect(() => setPickup(initial), [initial])

  const send = async () => {
    setBusy(true)
    setError(null)
    try {
      const res = await requestPickup(childId)
      haptic('medium')
      setPickup(res)
    } catch (e) {
      setError(e.message)
    } finally {
      setBusy(false)
    }
  }

  const accepted = pickup?.status === 'accepted'
  const pending = pickup?.status === 'pending'

  return (
    <Card title="Farzandni olib ketish">
      <div className="px-4 pb-4 pt-1">
        {accepted && (
          <div className="flex items-start gap-3 rounded-2xl bg-emerald-50 p-3">
            <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-emerald-600" />
            <div>
              <p className="text-[14px] font-semibold text-emerald-800">Farzandingiz topshirildi</p>
              <p className="text-[13px] text-emerald-700">
                {pickup.acceptedByName ? `${pickup.acceptedByName} · ` : ''}
                {dateTime(pickup.acceptedAt)}
              </p>
            </div>
          </div>
        )}

        {pending && (
          <div className="flex items-start gap-3 rounded-2xl bg-brand/20 p-3">
            <Clock className="mt-0.5 h-5 w-5 shrink-0 text-brand-ink" />
            <div>
              <p className="text-[14px] font-semibold">So'rov yuborildi</p>
              <p className="text-[13px] text-slate-600">
                {dateTime(pickup.createdAt)} · sinf rahbari tasdiqlashini kuting.
              </p>
            </div>
          </div>
        )}

        {!accepted && !pending && (
          <>
            <p className="text-[13px] leading-relaxed text-slate-500">
              Maktabga yetib kelganingizda bosing — sinf rahbariga darhol bildirishnoma boradi.
            </p>
            <button
              type="button"
              onClick={send}
              disabled={busy}
              className="mt-3 flex w-full items-center justify-center gap-2 rounded-2xl bg-brand py-3.5 text-[15px] font-bold text-brand-ink disabled:opacity-50"
            >
              <DoorOpen className="h-5 w-5" />
              {busy ? 'Yuborilmoqda…' : 'Farzandimni olib ketaman'}
            </button>
          </>
        )}

        {error && <p className="mt-2 text-[13px] text-red-600">{error}</p>}
      </div>
    </Card>
  )
}

/* ---------------------------------------------------------------- e'lonlar */

function AnnouncementsCard({ items }) {
  return (
    <Card title="Maktab e'lonlari">
      {items.length === 0 ? (
        <EmptyState
          icon={<Megaphone className="h-8 w-8" />}
          title="Hozircha e'lon yo'q"
          note="Sinf rahbari yoki ma'muriyat xabar yuborsa, shu yerda ko'rinadi."
        />
      ) : (
        items.slice(0, 3).map((m) => (
          <Row
            key={m.id}
            lead={
              <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
                <Megaphone className="h-5 w-5" />
              </div>
            }
            title={m.text}
            subtitle={`${m.senderName} · ${dateTime(m.createdAt)}`}
          />
        ))
      )}
    </Card>
  )
}
