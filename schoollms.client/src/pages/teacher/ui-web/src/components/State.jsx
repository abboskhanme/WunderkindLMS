import { Loader2, WifiOff, RotateCw } from 'lucide-react'
import AppButton from './AppButton'

// Markazlashgan yuklash spinneri (ekran ichida).
export function Loading({ label = 'Yuklanmoqda…' }) {
  return (
    <div className="flex-1 flex flex-col items-center justify-center gap-3 p-10">
      <Loader2 size={28} className="text-primary animate-spin" />
      <p className="text-[13px] text-muted">{label}</p>
    </div>
  )
}

// Xatolik holati + "Qayta urinish" tugmasi.
export function ErrorState({ error, onRetry }) {
  const msg = (error && error.message) || 'Xatolik yuz berdi'
  return (
    <div className="flex-1 flex flex-col items-center justify-center gap-3 p-10 text-center">
      <div className="w-16 h-16 rounded-6xl bg-surface3 flex items-center justify-center text-faint">
        <WifiOff size={26} />
      </div>
      <p className="text-[15px] font-bold text-text">{msg}</p>
      {onRetry && (
        <AppButton label="Qayta urinish" style="soft" leadingIcon={<RotateCw size={16} />} onClick={onRetry} />
      )}
    </div>
  )
}

// useFetch natijasini bitta joyda hal qilish: loading → error → bo'sh → kontent.
export function AsyncView({ query, children, empty = null, loadingLabel }) {
  if (query.loading && !query.data) return <Loading label={loadingLabel} />
  if (query.error) return <ErrorState error={query.error} onRetry={query.reload} />
  if (empty && query.data && Array.isArray(query.data) && query.data.length === 0) return empty
  return children
}
