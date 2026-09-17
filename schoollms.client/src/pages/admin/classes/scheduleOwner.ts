import type { LessonOwnerKind } from '@/types'
import { getClasses } from '@/api/services/classes'
import { getGroup } from '@/api/services/groups'

/**
 * Jadval sahifasidagi `:id` — EGAning id'si: sinf yoki o'quv guruhi
 * (`docs/modules/students-parity.md` §2.1.4).
 *
 * <p>
 * Guruh darsi mavjud `class_id` ustunida guruh id'sini saqlaydi, shuning
 * uchun jadval yo'llari ham (`/admin/schedule/manage/:id`) ikkala ega uchun
 * BIR XIL. Sahifa faqat id kimniki ekanini bilishi kerak — sarlavhani
 * to'g'ri yozish va guruhda 1/2-bo'linishni ko'rsatmaslik uchun.
 * </p>
 */
export interface ScheduleOwner {
  id: string
  name: string
  kind: LessonOwnerKind
  /** Sarlavha ostidagi kichik matn: sinf tili/xonasi yoki guruh fani */
  subtitle: string
}

/**
 * Id'ni egaga aylantiradi: avval sinflar, topilmasa o'quv guruhlari
 * (arxivlanganlari bilan birga — eski jadvalni ochib ko'rish mumkin bo'lsin).
 * Topilmasa null.
 */
export async function resolveScheduleOwner(id: string): Promise<ScheduleOwner | null> {
  if (!id) return null

  const classes = await getClasses()
  const cls = classes.find((c) => c.id === id)
  if (cls) {
    return {
      id: cls.id,
      name: cls.name,
      kind: 'class',
      subtitle: `${cls.name}-sinf`,
    }
  }

  // Sinf emas — o'quv guruhi bo'lishi mumkin. 404 (yoki har qanday xato) =
  // bunday ega yo'q; sahifa "topilmadi" deb ko'rsatadi.
  try {
    const grp = await getGroup(id)
    return {
      id: grp.id,
      name: grp.name,
      kind: 'group',
      subtitle: `O'quv guruhi · ${grp.subjectName}${grp.isArchived ? ' · arxivda' : ''}`,
    }
  } catch {
    return null
  }
}
