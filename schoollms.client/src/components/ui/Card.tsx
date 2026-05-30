import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

interface CardProps {
  className?: string
  children: ReactNode
}

export function Card({ className, children }: CardProps) {
  return (
    <div
      className={cn(
        'rounded-2xl border border-slate-200/80 bg-white p-5 shadow-sm',
        className,
      )}
    >
      {children}
    </div>
  )
}
