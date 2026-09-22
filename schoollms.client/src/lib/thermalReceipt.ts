import type { ReceiptPrint, ReceiptPrintLine } from '@/api/services/cashier'

/* ==========================================================================
   58 mm termal chek — brauzerdan to'g'ridan-to'g'ri chop etish (2026-09-22)

   MIJOZ: maktab 58 mm XPrinter oldi. To'lov qabul qilingach chop etish
   oynasi O'ZI ochilsin (PDF'ni avval saqlamasdan) va chek IKKI nusxada
   chiqsin — biri maktabga, biri ota-onaga.

   QANDAY ISHLAYDI: chek HTML'i yashirin iframe'ga yoziladi va iframe'ning
   o'z `print()` i chaqiriladi — sahifaning qolgan qismi qog'ozga tushmaydi,
   yangi oyna ham ochilmaydi. Ikki nusxa BITTA bosma ishida, orasida sahifa
   uzilishi (kesgich shu yerda kesadi) va uzuq chiziq (kesgichsiz printerda
   qo'lda yirtish joyi).

   MATNNI BU YERDA HECH KIM FORMATLAMAYDI: pul, sana, oy, to'lov turi va
   "to'liq yopildi / qoldi" — hammasi serverdan tayyor satr bo'lib keladi
   (`ReceiptText`, PDF chek bilan bir xil). Bu fayl faqat joylashtiradi.
   Bazadan kelgan har bir satr HTML-escape qilinadi.

   CHOP ETISH OYNASI: brauzer uni HAR DOIM ko'rsatadi, kod buni chetlab
   o'tmaydi. To'liq jim chop etish uchun kassa kompyuterida Chrome
   `--kiosk-printing` bayrog'i bilan ishga tushiriladi (docs/ASSUMPTIONS.md).
   ========================================================================== */

/** Rulon kengligi. */
const PAGE_WIDTH_MM = 58
/** 58 mm kallakning bosiladigan eni (~384 nuqta @ 203 dpi) — matn shu ustunga sig'adi. */
const CONTENT_WIDTH_MM = 48
/** CSS: 96 px = 1 dyuym = 25.4 mm. */
const PX_PER_MM = 96 / 25.4
/** Har nusxa ostida kesgich uchun qo'shimcha qog'oz (mm). */
const FEED_MM = 4

/** Chop etiladigan nusxalar — tartibi qog'ozdagi tartib. */
export const RECEIPT_COPY_LABELS = ['Maktab nusxasi', 'Ota-ona nusxasi'] as const

/**
 * Qog'oz qoidalari. Qora-oq, fonsiz, rangsiz: termal kallak kulrangni
 * nuqtalab chizadi va xira chiqaradi. Shrift o'lchamlari 203 dpi da
 * o'qiladigan chegaradan kichik emas (asosiy matn 12 px ≈ 3.2 mm).
 */
const BASE_CSS = `
*{box-sizing:border-box;margin:0;padding:0}
html,body{background:#fff;color:#000}
body{width:${PAGE_WIDTH_MM}mm;padding:0 ${(PAGE_WIDTH_MM - CONTENT_WIDTH_MM) / 2}mm;
  font-family:Arial,"Helvetica Neue",Helvetica,sans-serif;font-size:12px;line-height:1.3;
  -webkit-print-color-adjust:exact;print-color-adjust:exact}
.copy{width:${CONTENT_WIDTH_MM}mm;padding-top:2mm;padding-bottom:${FEED_MM}mm;break-inside:avoid}
.copy + .copy{break-before:page;page-break-before:always}
.center{text-align:center}
.school{font-size:15px;font-weight:700;line-height:1.2;overflow-wrap:anywhere}
.muted{font-size:11px;overflow-wrap:anywhere}
.copy-label{margin-top:1.5mm;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.5px}
.title{margin-top:1mm;font-size:13px;font-weight:700}
.rule{margin:1.5mm 0;border-top:1px dashed #000}
.row{display:flex;justify-content:space-between;align-items:baseline;gap:2mm}
.row>.k{flex-shrink:0}
.row>.v{text-align:right;overflow-wrap:anywhere}
.amt{white-space:nowrap;font-weight:700;text-align:right}
.field{margin-top:.8mm}
.field .k{font-size:11px}
.field .v{font-weight:700;overflow-wrap:anywhere}
.line{margin-top:1.2mm}
.line .name{font-weight:700;overflow-wrap:anywhere}
.line .sub{font-size:11px}
.total{margin-top:1mm;font-size:15px;font-weight:700}
.stamp{margin:1.5mm 0;padding:1mm;border:2px solid #000;text-align:center}
.stamp b{display:block;font-size:15px;letter-spacing:1px}
.stamp span{font-size:11px}
.foot{margin-top:2mm;font-size:10px;text-align:center}
.cut{margin-top:3mm;border-top:1px dashed #000;padding-top:.5mm;font-size:9px;text-align:center}
`

/** HTML maxsus belgilarini qochiradi — ismlar va nomlar bazadan keladi. */
export function escapeHtml(value: string | null | undefined): string {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}

function row(label: string, value: string): string {
  return `<div class="row"><span class="k">${escapeHtml(label)}</span><span class="v">${escapeHtml(value)}</span></div>`
}

function lineHtml(line: ReceiptPrintLine): string {
  const sub: string[] = []
  if (line.periodText) sub.push(`<div class="sub">${escapeHtml(line.periodText)}</div>`)
  if (line.statusText) sub.push(`<div class="sub">${escapeHtml(line.statusText)}</div>`)
  return (
    `<div class="line">` +
    `<div class="row"><span class="name">${escapeHtml(line.categoryName)}</span>` +
    `<span class="amt">${escapeHtml(line.amountText)}</span></div>` +
    sub.join('') +
    `</div>`
  )
}

function copyHtml(r: ReceiptPrint, label: string): string {
  const parts: string[] = []

  parts.push(`<div class="center school">${escapeHtml(r.schoolName)}</div>`)
  if (r.schoolAddress) parts.push(`<div class="center muted">${escapeHtml(r.schoolAddress)}</div>`)
  if (r.schoolPhone) parts.push(`<div class="center muted">Tel: ${escapeHtml(r.schoolPhone)}</div>`)
  parts.push(`<div class="center copy-label">${escapeHtml(label)}</div>`)
  parts.push('<div class="rule"></div>')

  parts.push(`<div class="center title">TO'LOV CHEKI № ${escapeHtml(String(r.receiptNo))}</div>`)
  parts.push(`<div class="center muted">${escapeHtml(r.receivedAtText)}</div>`)

  if (r.cancelledStamp) {
    parts.push(
      `<div class="stamp"><b>${escapeHtml(r.cancelledStamp)}</b>` +
        (r.cancelledAtText ? `<span>${escapeHtml(r.cancelledAtText)}</span>` : '') +
        `</div>`,
    )
  }

  parts.push(
    `<div class="field"><div class="k">O'quvchi (F.I.SH)</div>` +
      `<div class="v">${escapeHtml(r.studentName)}</div>` +
      (r.className ? `<div class="k">Sinf: ${escapeHtml(r.className)}</div>` : '') +
      `</div>`,
  )

  parts.push('<div class="rule"></div>')
  parts.push(r.lines.map(lineHtml).join(''))
  parts.push('<div class="rule"></div>')

  parts.push(
    `<div class="row total"><span class="k">JAMI</span><span class="amt">${escapeHtml(r.totalText)}</span></div>`,
  )
  parts.push(row("To'lov turi", r.methodText))
  parts.push(row('Kassir', r.cashierName))

  // "Qoldi" — chop etilgan paytdagi holat, shuning uchun sana chekda turadi.
  parts.push(`<div class="foot">Chop etildi: ${escapeHtml(r.printedAtText)}</div>`)
  parts.push(`<div class="cut">kesish chizig'i</div>`)

  return `<section class="copy">${parts.join('')}</section>`
}

/**
 * Chekning to'liq HTML hujjati. `labels` — nechta nusxa va har birining
 * yorlig'i (sukut: maktab + ota-ona). Ekrandagi oldindan ko'rish ham aynan
 * shu HTML'ni ishlatadi — ko'rgan narsangiz qog'ozga tushadigan narsa.
 */
export function thermalReceiptHtml(
  receipt: ReceiptPrint,
  labels: readonly string[] = RECEIPT_COPY_LABELS,
): string {
  const copies = labels.map((label) => copyHtml(receipt, label)).join('')
  return (
    `<!doctype html><html lang="uz"><head><meta charset="utf-8">` +
    `<title>Chek № ${escapeHtml(String(receipt.receiptNo))}</title>` +
    `<style>${BASE_CSS}</style>` +
    // Vaqtinchalik o'lcham; chop etishdan oldin `fitPageToCopy` aniq balandlikni qo'yadi.
    `<style id="page-size">@page{size:${PAGE_WIDTH_MM}mm 297mm;margin:0}</style>` +
    `</head><body>${copies}</body></html>`
  )
}

/**
 * Sahifa balandligini nusxaning haqiqiy balandligiga tenglaydi: bir sahifa =
 * bir nusxa, ya'ni kesgich aynan nusxalar orasida kesadi va rulon bo'sh
 * aylanmaydi.
 *
 * Nega `size: 58mm auto` emas: CSS Paged Media'da `auto` faqat YAKKA qiymat
 * sifatida ruxsat etilgan — `58mm auto` noto'g'ri deklaratsiya va Chrome uni
 * butunlay tashlab yuboradi (varaq A4 bo'lib qoladi). Shuning uchun balandlik
 * o'lchanadi va aniq mm bilan yoziladi.
 */
function fitPageToCopy(doc: Document): void {
  const copies = Array.from(doc.querySelectorAll<HTMLElement>('.copy'))
  const tallestPx = Math.max(0, ...copies.map((el) => el.getBoundingClientRect().height))
  if (tallestPx <= 0) return
  // +2 mm — yaxlitlash nusxani ikkinchi sahifaga toshirib yubormasin.
  const heightMm = Math.ceil(tallestPx / PX_PER_MM) + 2
  const style = doc.getElementById('page-size')
  if (style) style.textContent = `@page{size:${PAGE_WIDTH_MM}mm ${heightMm}mm;margin:0}`
}

/** Oxirgi bosma iframe'i — keyingi bosmada olib tashlanadi (sahifada bittadan ortiq qolmaydi). */
let activeFrame: HTMLIFrameElement | null = null

/**
 * Chekni IKKI nusxada chop etadi: yashirin iframe + uning `print()` i.
 * Brauzerning chop etish oynasi ochiladi (uni kod chetlab o'tmaydi).
 *
 * Iframe `display:none` EMAS: ba'zi brauzerlar ko'rinmas (joylashuvsiz)
 * hujjatni bo'sh varaq qilib chop etadi. U ekrandan tashqariga suriladi.
 */
export function printThermalReceipt(
  receipt: ReceiptPrint,
  labels: readonly string[] = RECEIPT_COPY_LABELS,
): Promise<void> {
  return new Promise((resolve, reject) => {
    activeFrame?.remove()

    const frame = document.createElement('iframe')
    frame.setAttribute('aria-hidden', 'true')
    frame.setAttribute('title', 'Chek (chop etish)')
    frame.tabIndex = -1
    frame.style.cssText = `position:fixed;left:-10000px;top:0;width:${PAGE_WIDTH_MM}mm;height:20px;border:0;opacity:0;pointer-events:none`
    activeFrame = frame

    frame.onload = () => {
      const win = frame.contentWindow
      const doc = frame.contentDocument
      if (!win || !doc) {
        reject(new Error("Chekni chop etish oynasini ochib bo'lmadi."))
        return
      }
      try {
        fitPageToCopy(doc)
        // Chrome'da `print()` oyna yopilguncha kutadi, Safari/Firefox'da esa
        // darhol qaytadi — shuning uchun iframe bu yerda O'CHIRILMAYDI
        // (chop etish bekor bo'lib qolardi); keyingi bosma uni almashtiradi.
        win.focus()
        win.print()
        resolve()
      } catch (err) {
        reject(err instanceof Error ? err : new Error(String(err)))
      }
    }

    frame.srcdoc = thermalReceiptHtml(receipt, labels)
    document.body.appendChild(frame)
  })
}
