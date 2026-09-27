import { useCallback, useLayoutEffect, useRef, useState } from 'react'
import type { PointerEvent as ReactPointerEvent, ReactNode } from 'react'
import { Card } from '@/components/ui/Card'
import { asCount, asPercent, clamp } from './format'

/* ------------------------------------------------------------------------- *
 * Dars tahlili grafiklari — eski ilovaning `components/charts.tsx` dan ko'chirilgan,
 * bizning tokenlarga (brand / slate) o'tkazilgan. Kutubxonasiz, qo'lda chizilgan SVG.
 *
 *  - barcha belgilar bitta rangda (brand), to'r chiziqlari slate-200, matn slate.
 *    Bo'lim ranglari faqat identifikatsiya belgisi (swatch), belgi rangi emas.
 *  - o'qlar sokin: 1px to'r, ramka/soya/gradient yo'q.
 *  - har bir belgining kamida 8px bosish maydoni va tooltip'i bor (planshet).
 * ------------------------------------------------------------------------- */

export interface ChartDatum {
  /** O'qda yoki ustun yonida chiziladigan qisqa yorliq. */
  label: string
  value: number
  /** Kesilmagan yorliq — `title` va tooltip sarlavhasi uchun. */
  fullLabel?: string
  /** Identifikatsiya belgisi (masalan bo'lim rangi). Hech qachon belgi rangi emas. */
  swatch?: string
  /** Ustun yorlig'i ostidagi ikkinchi qator. */
  note?: string
  /** Tooltip matni. Berilmasa — formatlangan qiymat. */
  rows?: string[]
}

interface TooltipState {
  id: string
  x: number
  y: number
  title: string
  rows: string[]
}

type BarSide = 'right' | 'top'

/** Ustun — faqat uzoq tomonida 4px yumaloq, asosga yopishgan. */
function barPath(x: number, y: number, w: number, h: number, side: BarSide): string {
  if (!(w > 0) || !(h > 0)) return ''
  const r = Math.min(4, w / 2, h / 2)
  if (side === 'right') {
    return `M${x},${y} H${x + w - r} A${r},${r} 0 0 1 ${x + w},${y + r} V${y + h - r} A${r},${r} 0 0 1 ${x + w - r},${y + h} H${x} Z`
  }
  return `M${x},${y + h} V${y + r} A${r},${r} 0 0 1 ${x + r},${y} H${x + w - r} A${r},${r} 0 0 1 ${x + w},${y + r} V${y + h} Z`
}

/** O'q maksimumini butun qadamli o'qiladigan qiymatga yaxlitlaydi. */
function niceMax(value: number, ticks = 4): number {
  if (!Number.isFinite(value) || value <= 0) return ticks
  const rough = value / ticks
  const magnitude = 10 ** Math.floor(Math.log10(rough))
  const normalized = rough / magnitude
  const factor =
    normalized <= 1 ? 1
    : normalized <= 2 ? 2
    : normalized <= 2.5 ? 2.5
    : normalized <= 5 ? 5
    : 10
  return Math.max(1, Math.ceil(factor * magnitude)) * ticks
}

function useMeasuredWidth() {
  const ref = useRef<HTMLDivElement | null>(null)
  const [width, setWidth] = useState(0)

  useLayoutEffect(() => {
    const node = ref.current
    if (!node) return
    // ResizeObserver kuzatuv boshlanganda ham bir marta chaqiriladi — boshlang'ich o'lcham shundan.
    const observer = new ResizeObserver(() => setWidth(node.clientWidth))
    observer.observe(node)
    return () => observer.disconnect()
  }, [])

  return [ref, width] as const
}

/** O'lchangan chizma maydoni + barcha grafiklar uchun umumiy pointer/touch tooltip. */
function useChartFrame() {
  const [boxRef, width] = useMeasuredWidth()
  const [tip, setTip] = useState<TooltipState | null>(null)

  const hide = useCallback(() => setTip(null), [])

  const markProps = useCallback(
    (id: string, title: string, rows: string[]) => {
      const open = (event: ReactPointerEvent<Element>) => {
        const box = boxRef.current?.getBoundingClientRect()
        if (!box) return
        const edge = Math.min(104, box.width / 2)
        setTip({
          id,
          x: clamp(event.clientX - box.left, edge, Math.max(edge, box.width - edge)),
          y: event.clientY - box.top,
          title,
          rows,
        })
      }
      return {
        onPointerEnter: open,
        onPointerMove: open,
        onPointerDown: open,
        onPointerLeave: hide,
        onPointerCancel: hide,
      }
    },
    [boxRef, hide],
  )

  return { boxRef, width, tip, markProps, hide }
}

function ChartEmpty() {
  return <p className="py-14 text-center text-sm text-slate-400">Ma’lumot yo‘q</p>
}

function ChartTooltip({ tip }: { tip: TooltipState | null }) {
  if (!tip) return null
  const below = tip.y < 72
  return (
    <div
      aria-hidden="true"
      className={`pointer-events-none absolute z-20 w-max max-w-[210px] -translate-x-1/2 rounded-xl border border-slate-200 bg-white px-3 py-2 shadow-lg ${
        below ? 'mt-3' : '-mt-3 -translate-y-full'
      }`}
      style={{ left: tip.x, top: tip.y }}
    >
      <p className="text-xs font-semibold leading-snug text-slate-800">{tip.title}</p>
      {tip.rows.map((row, index) => (
        <p key={index} className="mt-0.5 text-xs leading-snug tabular-nums text-slate-600">
          {row}
        </p>
      ))}
    </div>
  )
}

const tooltipRows = (datum: ChartDatum, formatValue: (value: number) => string) =>
  datum.rows && datum.rows.length > 0 ? datum.rows : [formatValue(datum.value)]

/* ----------------------------------- chiziq ---------------------------------- */

export function LineChart({
  data,
  height = 200,
  min = 0,
  max = 100,
  formatValue = asPercent,
  ariaLabel,
}: {
  data: ChartDatum[]
  height?: number
  /** Domen tubi. Chiziq noldan yuqorida tursa ham bo'ladi — o'q buni aytib turadi. */
  min?: number
  max?: number
  formatValue?: (value: number) => string
  ariaLabel?: string
}) {
  const { boxRef, width, tip, markProps, hide } = useChartFrame()

  const padL = 34
  const padR = 12
  const padT = 16
  const padB = 24
  const plotW = Math.max(0, width - padL - padR)
  const plotH = Math.max(0, height - padT - padB)
  const safeMin = min < max ? min : 0
  const safeMax = max > safeMin ? max : safeMin + 1
  const span = safeMax - safeMin
  const lastIndex = data.length - 1

  const px = (index: number) =>
    data.length < 2 ? padL + plotW / 2 : padL + (plotW * index) / lastIndex
  const py = (value: number) =>
    padT + plotH * (1 - (clamp(value, safeMin, safeMax) - safeMin) / span)

  const gridValues = [0, 0.25, 0.5, 0.75, 1].map((step) => safeMin + step * span)
  const labelStep = Math.max(1, Math.ceil(data.length / Math.max(1, Math.floor(plotW / 58))))
  const band = Math.max(8, data.length > 1 ? plotW / lastIndex : Math.max(plotW, 8))
  const linePoints = data.map((datum, index) => `${px(index)},${py(datum.value)}`).join(' ')
  const last = lastIndex >= 0 ? data[lastIndex] : null

  return (
    <div ref={boxRef} className="relative" onPointerLeave={hide}>
      {data.length === 0 ? (
        <ChartEmpty />
      ) : (
        <svg
          width={Math.max(width, 1)}
          height={height}
          viewBox={`0 0 ${Math.max(width, 1)} ${height}`}
          role="img"
          aria-label={ariaLabel}
        >
          {gridValues.map((value) => (
            <g key={value}>
              <line
                x1={padL}
                y1={py(value)}
                x2={Math.max(padL, width - padR)}
                y2={py(value)}
                className="pointer-events-none stroke-slate-200"
              />
              <text
                x={padL - 8}
                y={py(value) + 3.5}
                textAnchor="end"
                className="pointer-events-none fill-slate-400 text-[10px] tabular-nums"
              >
                {formatValue(value)}
              </text>
            </g>
          ))}

          {data.map((datum, index) =>
            (lastIndex - index) % labelStep === 0 ? (
              <text
                key={`x-${index}`}
                x={px(index)}
                y={height - 7}
                textAnchor={index === 0 ? 'start' : index === lastIndex ? 'end' : 'middle'}
                className="pointer-events-none fill-slate-400 text-[10px]"
              >
                {datum.label}
              </text>
            ) : null,
          )}

          {data.length > 1 && (
            <polyline
              points={linePoints}
              fill="none"
              strokeLinecap="round"
              strokeLinejoin="round"
              className="pointer-events-none stroke-brand-500 stroke-2"
            />
          )}

          {data.map((datum, index) => {
            const active = tip?.id === `p${index}`
            if (!active && index !== lastIndex && data.length > 16) return null
            return (
              <circle
                key={`dot-${index}`}
                cx={px(index)}
                cy={py(datum.value)}
                r={active ? 4.5 : 3}
                className="pointer-events-none fill-white stroke-brand-500 stroke-2"
              />
            )
          })}

          {last && (
            <text
              x={px(lastIndex)}
              y={py(last.value) < padT + 20 ? py(last.value) + 17 : py(last.value) - 10}
              textAnchor="end"
              className="pointer-events-none fill-slate-600 text-[11px] font-semibold tabular-nums"
            >
              {formatValue(last.value)}
            </text>
          )}

          {data.map((datum, index) => (
            <rect
              key={`hit-${index}`}
              x={px(index) - band / 2}
              y={padT}
              width={band}
              height={Math.max(plotH, 8)}
              className="fill-transparent"
              {...markProps(`p${index}`, datum.fullLabel ?? datum.label, tooltipRows(datum, formatValue))}
            />
          ))}
        </svg>
      )}
      <ChartTooltip tip={tip} />
    </div>
  )
}

/* -------------------------------- gorizontal -------------------------------- */

export function BarChart({
  data,
  max = 100,
  formatValue = asPercent,
  ariaLabel,
}: {
  data: ChartDatum[]
  max?: number
  formatValue?: (value: number) => string
  ariaLabel?: string
}) {
  const { boxRef, width, tip, markProps, hide } = useChartFrame()

  const safeMax = max > 0 ? max : 1
  const barH = 6
  // To'g'ridan-to'g'ri qiymatlar sakkiz ustungacha o'qiladi; undan ko'pida tooltip ko'rsatadi.
  const showValues = data.length <= 8

  return (
    <div ref={boxRef} className="relative" onPointerLeave={hide}>
      {data.length === 0 ? (
        <ChartEmpty />
      ) : (
        <ul aria-label={ariaLabel}>
          {data.map((datum, index) => (
            <li
              key={index}
              className="-mx-2 rounded-lg px-2 py-2.5 transition-colors hover:bg-slate-50"
              {...markProps(`b${index}`, datum.fullLabel ?? datum.label, tooltipRows(datum, formatValue))}
            >
              <div className="flex items-baseline gap-2">
                {datum.swatch && (
                  <svg width={3} height={11} className="shrink-0" aria-hidden="true">
                    <rect width={3} height={11} rx={1.5} fill={datum.swatch} />
                  </svg>
                )}
                <p
                  className="min-w-0 flex-1 truncate text-sm leading-tight text-slate-700"
                  title={datum.fullLabel ?? datum.label}
                >
                  {datum.label}
                </p>
                {showValues && (
                  <span className="shrink-0 text-[13px] font-semibold leading-tight tabular-nums text-slate-800">
                    {formatValue(datum.value)}
                  </span>
                )}
              </div>

              {datum.note && (
                <p className="mt-0.5 truncate text-[11.5px] leading-tight text-slate-400">{datum.note}</p>
              )}

              <svg
                width={Math.max(width, 1)}
                height={barH}
                viewBox={`0 0 ${Math.max(width, 1)} ${barH}`}
                className="mt-1.5"
                aria-hidden="true"
              >
                <rect x={0} y={0} width={Math.max(width, 0)} height={barH} rx={barH / 2} className="fill-slate-100" />
                <path
                  d={barPath(0, 0, (width * clamp(datum.value, 0, safeMax)) / safeMax, barH, 'right')}
                  className="fill-brand-500"
                />
              </svg>
            </li>
          ))}
        </ul>
      )}
      <ChartTooltip tip={tip} />
    </div>
  )
}

/* --------------------------------- vertikal --------------------------------- */

export function ColumnChart({
  data,
  max,
  height = 190,
  formatValue = asCount,
  ariaLabel,
}: {
  data: ChartDatum[]
  max?: number
  height?: number
  formatValue?: (value: number) => string
  ariaLabel?: string
}) {
  const { boxRef, width, tip, markProps, hide } = useChartFrame()

  const padL = 32
  const padR = 10
  const padT = 20
  const padB = 26
  const minSlot = 46
  const safeMax = max && max > 0 ? max : niceMax(Math.max(0, ...data.map((d) => d.value)))
  // Keng grafik o'z konteyneri ichida suriladi — sahifa hech qachon yonga surilmaydi.
  const innerW = Math.max(width, padL + padR + data.length * minSlot)
  const plotW = Math.max(0, innerW - padL - padR)
  const plotH = Math.max(0, height - padT - padB)
  const slot = data.length > 0 ? plotW / data.length : plotW
  const barW = Math.max(8, Math.min(56, slot - 2))
  const showValues = data.length <= 8
  const gridValues = [0, 0.25, 0.5, 0.75, 1].map((step) => step * safeMax)
  const py = (value: number) => padT + plotH * (1 - clamp(value, 0, safeMax) / safeMax)

  return (
    <div ref={boxRef} className="relative" onPointerLeave={hide}>
      {data.length === 0 ? (
        <ChartEmpty />
      ) : (
        <div className="overflow-x-auto">
          <svg width={innerW} height={height} viewBox={`0 0 ${innerW} ${height}`} role="img" aria-label={ariaLabel}>
            {gridValues.map((value) => (
              <g key={value}>
                <line
                  x1={padL}
                  y1={py(value)}
                  x2={innerW - padR}
                  y2={py(value)}
                  className="pointer-events-none stroke-slate-200"
                />
                <text
                  x={padL - 8}
                  y={py(value) + 3.5}
                  textAnchor="end"
                  className="pointer-events-none fill-slate-400 text-[10px] tabular-nums"
                >
                  {formatValue(value)}
                </text>
              </g>
            ))}

            {data.map((datum, index) => {
              const top = py(datum.value)
              const barHeight = padT + plotH - top
              const center = padL + slot * index + slot / 2
              const active = tip?.id === `c${index}`
              return (
                <g key={index}>
                  <path
                    d={barPath(center - barW / 2, top, barW, barHeight, 'top')}
                    className={`pointer-events-none fill-brand-500 ${active ? 'opacity-80' : ''}`}
                  />
                  {showValues && (
                    <text
                      x={center}
                      y={Math.max(padT - 7, top - 7)}
                      textAnchor="middle"
                      className="pointer-events-none fill-slate-600 text-[11px] font-semibold tabular-nums"
                    >
                      {formatValue(datum.value)}
                    </text>
                  )}
                  <text
                    x={center}
                    y={height - 8}
                    textAnchor="middle"
                    className="pointer-events-none fill-slate-400 text-[10.5px]"
                  >
                    {datum.label}
                  </text>
                  <rect
                    x={padL + slot * index}
                    y={padT}
                    width={Math.max(slot, 8)}
                    height={Math.max(plotH, 8)}
                    className="fill-transparent"
                    {...markProps(`c${index}`, datum.fullLabel ?? datum.label, tooltipRows(datum, formatValue))}
                  />
                </g>
              )
            })}
          </svg>
        </div>
      )}
      <ChartTooltip tip={tip} />
    </div>
  )
}

/* ---------------------------------- o'lchagich ---------------------------------- */

/** Bitta nisbat `max` ga nisbatan — yupqa yo'lak va to'ldirish, o'qsiz. */
export function Meter({
  label,
  value,
  note,
  max = 100,
  formatValue = asPercent,
}: {
  label: string
  value: number
  note?: string
  max?: number
  formatValue?: (value: number) => string
}) {
  const [boxRef, width] = useMeasuredWidth()
  const safeMax = max > 0 ? max : 1
  const trackH = 6

  return (
    <div ref={boxRef}>
      <div className="flex items-baseline justify-between gap-3">
        <p className="truncate text-[13px] text-slate-600">{label}</p>
        <p className="shrink-0 text-[15px] font-semibold tabular-nums text-slate-800">{formatValue(value)}</p>
      </div>
      <svg
        width={Math.max(width, 1)}
        height={trackH}
        viewBox={`0 0 ${Math.max(width, 1)} ${trackH}`}
        className="mt-2"
        aria-hidden="true"
      >
        <rect x={0} y={0} width={Math.max(width, 0)} height={trackH} rx={trackH / 2} className="fill-slate-100" />
        <path
          d={barPath(0, 0, (width * clamp(value, 0, safeMax)) / safeMax, trackH, 'right')}
          className="fill-brand-500"
        />
      </svg>
      {note && <p className="mt-1.5 text-xs tabular-nums text-slate-400">{note}</p>}
    </div>
  )
}

/* ---------------------------------- ramka ---------------------------------- */

export function ChartCard({
  title,
  caption,
  aside,
  children,
}: {
  title: string
  caption: string
  aside?: ReactNode
  children: ReactNode
}) {
  return (
    <Card className="min-w-0">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="font-semibold text-slate-800">{title}</h2>
          <p className="mt-0.5 text-xs text-slate-400">{caption}</p>
        </div>
        {aside && <div className="shrink-0">{aside}</div>}
      </div>
      <div className="mt-4">{children}</div>
    </Card>
  )
}
