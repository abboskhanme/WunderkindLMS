import { useCallback, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import type { HrReportRange } from '@/types/hrTasks'
import { hrIsValidRange, hrParsePeriod, hrPresetRange, hrToday, type HrPeriodPreset } from '@/components/hr-tasks/lib'

/**
 * The period lives in the URL (`?period=7d` or `?dateFrom=…&dateTo=…`), as in the old app:
 * a link opened tomorrow still shows "the last week", not frozen dates. Explicit dates win
 * over the preset name.
 */
export function usePeriodRange() {
  const [params, setParams] = useSearchParams()
  // Read once — the range must not shift at midnight and loop the requests.
  const [today] = useState(() => hrToday())

  const rawFrom = params.get('dateFrom') ?? ''
  const rawTo = params.get('dateTo') ?? ''
  const isCustom = params.get('period') === 'custom' || Boolean(rawFrom || rawTo)
  const preset = hrParsePeriod(params.get('period'))

  const range = useMemo<HrReportRange>(
    () => (isCustom ? { dateFrom: rawFrom, dateTo: rawTo } : hrPresetRange(preset, today)),
    [isCustom, rawFrom, rawTo, preset, today],
  )

  const update = useCallback(
    (patch: Record<string, string | readonly string[] | null>) => {
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev)
          for (const [key, value] of Object.entries(patch)) {
            next.delete(key)
            // Array — a multi-valued param (`?status=open&status=returned`).
            if (Array.isArray(value)) for (const item of value) next.append(key, item)
            else if (typeof value === 'string' && value) next.set(key, value)
          }
          return next
        },
        { replace: true },
      )
    },
    [setParams],
  )

  return {
    today,
    range,
    preset,
    isCustom,
    valid: hrIsValidRange(range),
    /** Preset button — clears manually entered dates. */
    setPreset: (next: HrPeriodPreset) => update({ period: next === 'today' ? null : next, dateFrom: null, dateTo: null }),
    setFrom: (value: string) => update({ period: 'custom', dateFrom: value, dateTo: rawTo || range.dateTo }),
    setTo: (value: string) => update({ period: 'custom', dateFrom: rawFrom || range.dateFrom, dateTo: value }),
    params,
    update,
  }
}
