/** "Kim, qaysi kuni, nima sababdan" ro'yxatining filtri — `AttendanceDetails.tsx`. */

/** `excused`/`unexcused` — kun bo'yi kelmaganlarning sababiga qarab. */
export type EventFilter = 'all' | 'absent' | 'excused' | 'unexcused' | 'partial' | 'late' | 'unchecked'

export interface EventSelection {
  filter: EventFilter
  reasonId: string | null
  studentId: string | null
}

export const allEvents: EventSelection = { filter: 'all', reasonId: null, studentId: null }
