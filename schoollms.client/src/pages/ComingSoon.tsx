import { Construction } from 'lucide-react'

interface Props {
  title?: string
}

export function ComingSoon({ title }: Props) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 py-24 text-center text-slate-400">
      <Construction className="h-10 w-10" />
      <p className="text-lg font-medium text-slate-600">{title ?? 'Tez orada'}</p>
      <p className="text-sm">Bu bo'lim hozir ishlab chiqilmoqda.</p>
    </div>
  )
}
