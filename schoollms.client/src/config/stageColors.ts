import type { StageColor } from '@/types'

export interface StageColorClasses {
  /** Kichik nuqta */
  dot: string
  /** Sanoq badge */
  badge: string
  /** Ustun tepasidagi rangli chiziq */
  bar: string
  /** Ustun foni (och) */
  tint: string
  /** Karta tashlanayotganda chegara rangi */
  ring: string
  /** Rang tanlash uchun doira */
  swatch: string
}

/** Tailwind klasslar to'liq (dinamik birlashtirish ishlamaydi) */
export const stageColors: Record<StageColor, StageColorClasses> = {
  slate: {
    dot: 'bg-slate-400',
    badge: 'bg-slate-200 text-slate-700',
    bar: 'bg-slate-400',
    tint: 'bg-slate-100/70',
    ring: 'ring-slate-300',
    swatch: 'bg-slate-400',
  },
  blue: {
    dot: 'bg-blue-500',
    badge: 'bg-blue-100 text-blue-700',
    bar: 'bg-blue-500',
    tint: 'bg-blue-50',
    ring: 'ring-blue-300',
    swatch: 'bg-blue-500',
  },
  emerald: {
    dot: 'bg-emerald-500',
    badge: 'bg-emerald-100 text-emerald-700',
    bar: 'bg-emerald-500',
    tint: 'bg-emerald-50',
    ring: 'ring-emerald-300',
    swatch: 'bg-emerald-500',
  },
  amber: {
    dot: 'bg-amber-500',
    badge: 'bg-amber-100 text-amber-700',
    bar: 'bg-amber-500',
    tint: 'bg-amber-50',
    ring: 'ring-amber-300',
    swatch: 'bg-amber-500',
  },
  violet: {
    dot: 'bg-violet-500',
    badge: 'bg-violet-100 text-violet-700',
    bar: 'bg-violet-500',
    tint: 'bg-violet-50',
    ring: 'ring-violet-300',
    swatch: 'bg-violet-500',
  },
  rose: {
    dot: 'bg-rose-500',
    badge: 'bg-rose-100 text-rose-700',
    bar: 'bg-rose-500',
    tint: 'bg-rose-50',
    ring: 'ring-rose-300',
    swatch: 'bg-rose-500',
  },
  cyan: {
    dot: 'bg-cyan-500',
    badge: 'bg-cyan-100 text-cyan-700',
    bar: 'bg-cyan-500',
    tint: 'bg-cyan-50',
    ring: 'ring-cyan-300',
    swatch: 'bg-cyan-500',
  },
  orange: {
    dot: 'bg-orange-500',
    badge: 'bg-orange-100 text-orange-700',
    bar: 'bg-orange-500',
    tint: 'bg-orange-50',
    ring: 'ring-orange-300',
    swatch: 'bg-orange-500',
  },
}

export const stageColorKeys: StageColor[] = [
  'slate',
  'blue',
  'emerald',
  'amber',
  'violet',
  'rose',
  'cyan',
  'orange',
]
