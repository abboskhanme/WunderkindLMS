/**
 * Hisobot ekranlarining UCHTA MAJBURIY holati: yuklanmoqda, xato, bo'sh
 * (P1-18). Beshta tab ham shu bitta komponentdan foydalanadi — aks holda
 * har biri "bo'sh" holatni o'zicha chizib, ekranlar bir-biriga o'xshamay
 * qolardi.
 *
 * Xato holati SHUNCHAKI matn emas: unda qayta urinish tugmasi bor, chunki
 * moliya hisoboti odatda tarmoq uzilishidan yiqiladi va sahifani butunlay
 * yangilash direktorning tanlagan davrini yo'qotadi.
 */
import type { ReactNode } from 'react'
import { AlertTriangle, Inbox, RefreshCw } from 'lucide-react'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

interface ReportStateProps {
  loading: boolean
  error: string | null
  /** true bo'lsa — ma'lumot keldi, lekin qator yo'q. */
  isEmpty?: boolean
  emptyTitle?: string
  emptyHint?: string
  onRetry?: () => void
  children: ReactNode
}

export function ReportState({
  loading,
  error,
  isEmpty = false,
  emptyTitle = "Ma'lumot yo'q",
  emptyHint,
  onRetry,
  children,
}: ReportStateProps) {
  if (loading) {
    return (
      <Card>
        <Loader label="Yuklanmoqda..." />
      </Card>
    )
  }

  if (error) {
    return (
      <Card className="border-red-200 bg-red-50/60">
        <div className="flex flex-col items-center gap-3 py-8 text-center">
          <AlertTriangle className="h-8 w-8 text-red-500" />
          <div>
            <p className="font-medium text-red-800">Hisobotni yuklab bo'lmadi</p>
            <p className="mt-1 max-w-lg text-sm text-red-700/80">{error}</p>
          </div>
          {onRetry && (
            <Button variant="secondary" onClick={onRetry}>
              <RefreshCw className="h-4 w-4" /> Qayta urinish
            </Button>
          )}
        </div>
      </Card>
    )
  }

  if (isEmpty) {
    return (
      <Card>
        <div className="flex flex-col items-center gap-2 py-12 text-center">
          <Inbox className="h-8 w-8 text-slate-300" />
          <p className="font-medium text-slate-600">{emptyTitle}</p>
          {emptyHint && <p className="max-w-md text-sm text-slate-400">{emptyHint}</p>}
        </div>
      </Card>
    )
  }

  return <>{children}</>
}
