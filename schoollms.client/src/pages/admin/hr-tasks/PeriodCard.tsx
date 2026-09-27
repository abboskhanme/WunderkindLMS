import { Card } from '@/components/ui/Card'
import { DateRangeField, PeriodTabs, SectionTitle } from '@/components/hr-tasks/ui'
import type { usePeriodRange } from './period'

/**
 * The "Davr" card — the explicit date range is primary, preset buttons sit below it (the
 * old owner's 2026-09-03 decision). Dashboard and Reports look the same.
 */
export function PeriodCard({ period }: { period: ReturnType<typeof usePeriodRange> }) {
  return (
    <Card className="space-y-4 p-4">
      <SectionTitle hint="Sana oralig‘ini kiriting yoki tez tugmadan tanlang">Davr</SectionTitle>
      <DateRangeField
        label="Sana oralig‘i"
        className="max-w-xl"
        dateFrom={period.range.dateFrom}
        dateTo={period.range.dateTo}
        onFrom={period.setFrom}
        onTo={period.setTo}
      />
      <PeriodTabs value={period.preset} active={!period.isCustom} onChange={period.setPreset} />
      {!period.valid && (
        <p className="text-xs text-red-600" role="alert">
          Ikkala sanani tanlang; boshlanish sanasi tugashdan keyin bo‘lmasin.
        </p>
      )}
    </Card>
  )
}
