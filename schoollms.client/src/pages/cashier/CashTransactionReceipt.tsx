import { useEffect, useState } from 'react'
import { Printer } from 'lucide-react'
import type { CashBoxTransactionRow } from '@/api/services/cashBoxes'
import { getSchoolName } from '@/api/services/settings'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { formatDateTime, formatSum, kindLabel, methodLabels, statusLabel } from './format'

/* ==========================================================================
   Kassa tranzaksiyasi cheki

   EduSchool kassa ekranida har bir kassa harakati uchun chek bosiladi
   (2026-09-18 da O'QIB o'rganildi — ularning sahifasida yashirin `.check`
   bloki bor: maktab nomi, kassir, yaratilgan sana, kassa, to'lov turi,
   tranzaksiya turi, izoh, summa). Bizda chek FAQAT o'quvchi to'lovi uchun
   bor edi (`ReceiptPreview` — server chizadigan PDF va Telegram), ya'ni
   oddiy kirim/chiqimni qo'lga berib bo'lmasdi.

   NIMA KO'CHIRILDI: FUNKSIYA — "har qanday kassa harakati uchun bosiladigan
   chek". Ko'rinish o'zimizniki (CLAUDE.md: "EduSchool'ning funksiyasini
   qayta quramiz, tashqi ko'rinishini emas"): oq qog'oz, bizning shrift va
   ranglarimiz, EduSchool'ning shiori va QR kodi YO'Q.

   NEGA PDF EMAS, BRAUZER BOSMASI: o'quvchi to'lovi cheki RASMIY hujjat —
   uni server QuestPDF bilan chizadi, chunki uning nusxasi ota-onada
   qoladi. Bu esa ICHKI qog'oz: kassir kirimni yozdi va tasdiq sifatida
   bosib beradi. Buning uchun yangi endpoint, shrift va PDF quvuri ochish —
   ortiqcha; brauzerning o'z bosmasi yetadi.
   ========================================================================== */

interface Props {
  row: CashBoxTransactionRow
  /** Chek qaysi kassaniki — jadval qatorida kassa nomi yo'q. */
  boxName: string
  onClose: () => void
}

export function CashTransactionReceipt({ row, boxName, onClose }: Props) {
  const [schoolName, setSchoolName] = useState('')

  useEffect(() => {
    getSchoolName()
      .then(setSchoolName)
      .catch(() => undefined)
  }, [])

  return (
    <Modal
      open
      onClose={onClose}
      title="Chek"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
          <Button onClick={() => window.print()}>
            <Printer className="h-4 w-4" /> Chop etish
          </Button>
        </>
      }
    >
      {/*
        `cash-receipt` — `index.css` dagi bosma qoidasi: qog'ozga FAQAT shu
        blok tushadi (modal ramkasi, tugmalar va sahifaning qolgani emas).
      */}
      <div className="cash-receipt mx-auto max-w-sm rounded-xl border border-slate-200 p-5">
        <div className="text-center">
          <p className="text-base font-semibold text-slate-800">
            {schoolName || 'Wunderkind School'}
          </p>
          <p className="mt-0.5 text-xs uppercase tracking-wide text-slate-400">
            {kindLabel(row.kind)} — chek
          </p>
        </div>

        <div className="my-4 border-t border-dashed border-slate-200" />

        <dl className="space-y-2 text-sm">
          <Line label="№" value={`#${row.no}`} />
          <Line label="Sana" value={formatDateTime(row.createdAt)} />
          <Line label="Kassa" value={boxName} />
          <Line label="Kassir" value={row.who} />
          <Line label="Tranzaksiya turi" value={row.transactionTypeName ?? '—'} />
          <Line label="To'lov usuli" value={methodLabels[row.method] ?? row.method} />
          {row.contractNo && <Line label="Shartnoma raqami" value={row.contractNo} />}
          {row.note && <Line label="Izoh" value={row.note} />}
          {row.status !== 'posted' && (
            <Line label="Holati" value={statusLabel(row.status)} />
          )}
          {row.cancelReason && <Line label="Sabab" value={row.cancelReason} />}
        </dl>

        <div className="my-4 border-t border-dashed border-slate-200" />

        <div className="flex items-baseline justify-between">
          <span className="text-sm font-medium text-slate-500">Summa</span>
          <span className="text-lg font-semibold tabular-nums text-slate-900">
            {formatSum(row.amount)} so'm
          </span>
        </div>

        <p className="mt-4 text-center text-[11px] text-slate-400">
          Chek {formatDateTime(new Date().toISOString())} da bosildi.
        </p>
      </div>
    </Modal>
  )
}

function Line({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-4">
      <dt className="shrink-0 text-slate-400">{label}</dt>
      <dd className="text-right font-medium text-slate-700">{value}</dd>
    </div>
  )
}
