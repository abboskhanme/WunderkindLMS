import { Loader2 } from 'lucide-react'
import { cn } from '@/lib/utils'

interface LoaderProps {
  className?: string
  label?: string
}

export function Loader({ className, label }: LoaderProps) {
  return (
    <div
      className={cn(
        'flex items-center justify-center gap-2 py-12 text-slate-400',
        className,
      )}
    >
      <Loader2 className="h-5 w-5 animate-spin" />
      {label && <span className="text-sm">{label}</span>}
    </div>
  )
}
