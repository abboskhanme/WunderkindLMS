/**
 * PROFIL — "men kimman va kimlarim bor".
 *
 * Tab emas, butun ekranni yopadigan varaq: pastki panelda beshta tab turibdi,
 * oltinchisi 320 px telefonda yozuvlarni siqib qo'yardi. Varaq sariq
 * sarlavhadagi avatar tugmasidan ochiladi, Telegram'ning "orqaga" tugmasi va
 * yuqoridagi × uni yopadi.
 *
 * Farzand qatoriga bosilsa — o'sha farzand tanlanadi va varaq yopiladi: profil
 * farzandlar ro'yxatini ko'rsatadigan yagona joy, undan to'g'ridan-to'g'ri
 * o'tish tabiiy.
 *
 * Rasm ATAYLAB yo'q: `TgChildDto.photoUrl` hozir tug'ilganlik guvohnomasining
 * fayli — uni avatar qilib qo'yish hujjatni ekranga chiqarish bo'lardi.
 */
import { useEffect } from 'react'
import { ChevronRight, MessageSquareText, X } from 'lucide-react'
import { Badge, Card, EmptyState, Hero, Row } from '../../components/ui'
import { LogoutButton } from '../../components/LogoutButton'
import { AccountsCard } from '../../components/AccountSwitcher'
import { useAsync } from '../../lib/useAsync'
import { getProfile } from '../../lib/parentApi'
import { shortSum } from '../../lib/format'
import { haptic, showBackButton } from '../../lib/telegram'
import { AsyncBlock } from './shared'

const RELATION = {
  parent: 'Ota-ona',
  father: 'Otasi',
  mother: 'Onasi',
  grandparent: 'Buvisi / bobosi',
  trustee: 'Vasiy',
  other: 'Qarindosh',
}

const ROLE = { parent: 'Ota-ona', student: "O'quvchi" }

/** "Abdullayev Anvar" → "AA". */
export function initials(fullName) {
  const parts = String(fullName ?? '').trim().split(/\s+/).filter(Boolean)
  return parts.slice(0, 2).map((p) => p.charAt(0).toUpperCase()).join('') || '?'
}

/** Sariq sarlavhadagi avatar — profilni ochadi. */
export function ProfileButton({ fullName, onClick }) {
  return (
    <button
      type="button"
      onClick={() => {
        haptic('light')
        onClick()
      }}
      aria-label="Profil"
      className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-brand-ink text-[14px] font-bold text-brand"
    >
      {initials(fullName)}
    </button>
  )
}

/**
 * @param user            sessiyadagi foydalanuvchi (javob kelguncha sarlavha uchun)
 * @param panelChildren   qobiqdagi farzandlar ro'yxati — o'quvchi hisobida
 *                        `/tg/me` farzand qaytarmaydi, u holda shu ko'rsatiladi
 */
export function ProfileSheet({ user, panelChildren, activeChildId, onPickChild, onClose, onOpenFeedback }) {
  const state = useAsync(() => getProfile(), [])

  useEffect(() => showBackButton(onClose), [onClose])

  return (
    // overscroll-contain — varaq oxirigacha surilganda orqadagi sahifa qimirlamasin.
    <div role="dialog" aria-label="Profil" className="fixed inset-0 z-40 overflow-y-auto overscroll-contain bg-ground">
      <div className="pb-[calc(24px+var(--tg-safe-bottom))]">
        <Hero
          title="Profil"
          subtitle={ROLE[user.role] ?? user.role}
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

        <AsyncBlock state={state} loadingLabel="Profil yuklanmoqda…">
          {(me) => {
            const children = (me.children ?? []).length
              ? me.children.map((c) => ({ ...c, id: c.studentId }))
              : panelChildren.map((c) => ({ ...c, relation: null, isPrimary: false, debt: null }))
            // Umumiy qarz ATAYLAB yo'q: pulni frontend qo'shmaydi (FinanceTab izohi),
            // har farzandning qarzi o'z qatorida serverdan tayyor keladi.
            // Bog'langan Telegram ko'rsatilmaydi (mijoz, 2026-09-29).

            return (
              <>
                <IdentityCard me={me} />

                {/* No "Akkaunt qo'shish" here (client, 2026-10-08): the family is in the switcher at the
                    top. The list shows only with several accounts — a teacher back to their own. */}
                <AccountsCard onSwitched={onClose} canAdd={false} />

                <Card title="Ma'lumotlar">
                  {me.login && <InfoRow label="Login" value={me.login} />}
                  <InfoRow label="Maktab" value={me.schoolName || '—'} />
                  <InfoRow label="Farzandlar soni" value={String(children.length)} />
                </Card>

                <Card
                  title="Farzandlarim"
                  action={<Badge tone="brand">{children.length} ta</Badge>}
                >
                  {children.length === 0 ? (
                    <EmptyState
                      title="Farzand biriktirilmagan"
                      note="Maktab kotibiyatiga murojaat qiling — farzandingiz hisobingizga biriktiriladi."
                    />
                  ) : (
                    children.map((c) => (
                      <ChildRow
                        key={c.id}
                        child={c}
                        active={c.id === activeChildId}
                        onClick={() => {
                          haptic('light')
                          onPickChild(c.id)
                        }}
                      />
                    ))
                  )}
                </Card>
              </>
            )
          }}
        </AsyncBlock>

        {onOpenFeedback && (
          <Card>
            <Row
              lead={
                <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
                  <MessageSquareText className="h-5 w-5" />
                </div>
              }
              title="Taklif va shikoyatlar"
              subtitle="Murojaat yuborish va holatini ko'rish"
              right={<ChevronRight className="h-5 w-5 text-slate-400" />}
              onClick={() => {
                haptic('light')
                onOpenFeedback()
              }}
            />
          </Card>
        )}

        <LogoutButton variant="block" />
      </div>
    </div>
  )
}

function IdentityCard({ me }) {
  return (
    <Card>
      <div className="flex items-center gap-4 px-4 py-5">
        <div className="flex h-16 w-16 shrink-0 items-center justify-center rounded-full bg-brand text-[22px] font-extrabold text-brand-ink">
          {initials(me.fullName)}
        </div>
        <div className="min-w-0 flex-1">
          <p className="break-words text-[19px] font-bold leading-tight">{me.fullName}</p>
          <p className="mt-1 text-[14px] text-slate-500">{ROLE[me.role] ?? me.role}</p>
        </div>
      </div>
    </Card>
  )
}

/** Yorliq chapda, qiymat o'ngda; uzun qiymat (login) o'raladi. */
function InfoRow({ label, value }) {
  return (
    <div className="flex items-baseline gap-3 border-t border-slate-100 px-4 py-3 first:border-t-0">
      <span className="shrink-0 text-[14px] text-slate-500">{label}</span>
      <span className="min-w-0 flex-1 break-words text-right text-[15px] font-semibold">{value}</span>
    </div>
  )
}

function ChildRow({ child, active, onClick }) {
  const relation = child.relation ? RELATION[child.relation] ?? RELATION.other : null
  const subtitle = [
    child.className ? `${child.className} sinf` : null,
    relation ? `${relation}${child.isPrimary ? ' (asosiy)' : ''}` : null,
  ]
    .filter(Boolean)
    .join(' · ')

  return (
    <Row
      onClick={onClick}
      lead={
        <div
          className={
            'flex h-11 w-11 shrink-0 items-center justify-center rounded-full text-[14px] font-bold ' +
            (active ? 'bg-brand text-brand-ink' : 'bg-slate-100 text-slate-600')
          }
        >
          {initials(child.fullName)}
        </div>
      }
      title={child.fullName}
      subtitle={subtitle}
      right={
        <div className="flex shrink-0 items-center gap-1.5">
          {child.debt > 0 ? (
            <Badge tone="danger">Qarz {shortSum(child.debt)}</Badge>
          ) : child.debt === 0 ? (
            <Badge tone="success">Qarz yo'q</Badge>
          ) : null}
          <ChevronRight className="h-4 w-4 text-slate-300" />
        </div>
      }
    />
  )
}
