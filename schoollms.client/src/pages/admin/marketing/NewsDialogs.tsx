/**
 * Yangiliklar bo'limining tasdiq oynalari (SM-10, §6.2).
 *
 * Nega alohida fayl: e'lon qilish oynasi — bu bo'limdagi YAGONA qaytarib
 * bo'lmaydigan amal (haqiqiy ota-onalarga haqiqiy xabar ketadi), shuning
 * uchun uning matni sahifa mantig'i orasida ko'milib qolmasligi kerak.
 *
 * Yangi dizayn tizimi emas: `Modal` ham, `Button` ham `components/ui/`
 * dan olinadi.
 */
import type { ReactNode } from 'react'
import { AlertTriangle, Send } from 'lucide-react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import type { NewsAdminDto, NewsAudience } from '@/api/services/news'
import { audienceListText, sortAudience } from './newsLabels'

/** Telegram xabari aynan kimga ketadi — §3.3 N6 dagi manbalar, o'zbekcha. */
const RECIPIENT_TEXT: Record<NewsAudience, string> = {
  employee: "botga ulangan xodimlar (o'qituvchi, xodim, administrator)",
  parent: "botga ulangan ota-onalar",
  student: "mini-ilovaga ulangan o'quvchilar",
}

function recipientsText(audience: NewsAudience[]): string {
  const parts = sortAudience(audience).map((a) => RECIPIENT_TEXT[a])
  if (parts.length === 0) return 'hech kim'
  if (parts.length === 1) return parts[0]
  return `${parts.slice(0, -1).join(', ')} va ${parts[parts.length - 1]}`
}

function Notice({ tone, children }: { tone: 'danger' | 'warning'; children: ReactNode }) {
  return (
    <p
      className={
        tone === 'danger'
          ? 'rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700'
          : 'rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800'
      }
    >
      {children}
    </p>
  )
}

interface PublishDialogProps {
  /** E'lon qilinayotgan yangilik; `null` — oyna yopiq. */
  item: NewsAdminDto | null
  sendTelegram: boolean
  onSendTelegramChange: (value: boolean) => void
  busy: boolean
  error: string | null
  onClose: () => void
  onConfirm: () => void
}

/**
 * E'lon qilish tasdig'i.
 *
 * Bu yerda ANIQ SON ko'rsatilmaydi: §5.4 da e'londan OLDIN oluvchilar sonini
 * qaytaradigan endpoint yo'q, xodimlar ro'yxatiga qaraydigan endpoint esa
 * boshqa ruxsat (`messages`) talab qiladi — faqat `marketing` ruxsati bor
 * xodimda u 403 berardi. Shuning uchun oyna KIMGA ketishini so'z bilan
 * aytadi, aniq son esa yuborilgandan keyin javobdan olinib, qatorda
 * ko'rsatiladi (§3.3 N4).
 */
export function NewsPublishDialog({
  item,
  sendTelegram,
  onSendTelegramChange,
  busy,
  error,
  onClose,
  onConfirm,
}: PublishDialogProps) {
  return (
    <Modal
      open={item !== null}
      onClose={busy ? () => {} : onClose}
      title="Yangilikni e'lon qilish"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={onConfirm} disabled={busy}>
            <Send className="h-4 w-4" />
            {busy ? "E'lon qilinmoqda..." : "E'lon qilish"}
          </Button>
        </>
      }
    >
      {item && (
        <div className="space-y-4">
          <div className="rounded-xl bg-slate-50 px-4 py-3">
            <p className="font-semibold text-slate-800">{item.title}</p>
            <p className="mt-0.5 text-sm text-slate-500">
              Kimga: {audienceListText(item.audience)}
            </p>
          </div>

          <div className="space-y-2 text-sm text-slate-600">
            <p className="font-medium text-slate-700">E'lon qilinsa, ikki narsa bo'ladi:</p>
            <ol className="ml-4 list-decimal space-y-1">
              <li>
                Yangilik lentada chiqadi — Telegram mini-ilova va portalda,{' '}
                {audienceListText(item.audience)} uchun.
              </li>
              <li>
                {sendTelegram ? (
                  <>
                    Telegram bot shu odamlarga xabar yuboradi:{' '}
                    {recipientsText(item.audience)}.
                  </>
                ) : (
                  <>Telegram xabari YUBORILMAYDI — yangilik faqat lentada chiqadi.</>
                )}
              </li>
            </ol>
          </div>

          <label className="flex cursor-pointer items-start gap-2 rounded-lg border border-slate-200 px-3 py-2">
            <input
              type="checkbox"
              checked={sendTelegram}
              disabled={busy}
              onChange={(e) => onSendTelegramChange(e.target.checked)}
              className="mt-0.5 h-4 w-4 accent-brand-600"
            />
            <span className="text-sm text-slate-700">
              Telegram orqali ham yuborilsin
              <span className="mt-0.5 block text-xs text-slate-400">
                Xabar darhol ketadi va uni qaytarib olib bo'lmaydi. Nechta odamga
                yetgani yuborilgandan keyin shu yangilik qatorida ko'rinadi.
              </span>
            </span>
          </label>

          {sendTelegram && (
            <Notice tone="warning">
              <AlertTriangle className="mr-1 inline h-4 w-4 align-[-3px]" />
              Xabar haqiqiy odamlarga boradi. Sarlavha va matnni yana bir marta
              o'qib chiqing.
            </Notice>
          )}

          {error && <Notice tone="danger">{error}</Notice>}
        </div>
      )}
    </Modal>
  )
}

interface ConfirmDialogProps {
  open: boolean
  title: string
  /** Tasdiq tugmasidagi matn (masalan "O'chirish"). */
  confirmLabel: string
  busyLabel: string
  tone?: 'primary' | 'danger'
  busy: boolean
  error: string | null
  onClose: () => void
  onConfirm: () => void
  children: ReactNode
}

/** E'londan qaytarish va o'chirish uchun oddiy tasdiq oynasi. */
export function NewsConfirmDialog({
  open,
  title,
  confirmLabel,
  busyLabel,
  tone = 'primary',
  busy,
  error,
  onClose,
  onConfirm,
  children,
}: ConfirmDialogProps) {
  return (
    <Modal
      open={open}
      onClose={busy ? () => {} : onClose}
      title={title}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button variant={tone === 'danger' ? 'danger' : 'primary'} onClick={onConfirm} disabled={busy}>
            {busy ? busyLabel : confirmLabel}
          </Button>
        </>
      }
    >
      <div className="space-y-3 text-sm text-slate-600">
        {children}
        {error && <Notice tone="danger">{error}</Notice>}
      </div>
    </Modal>
  )
}
