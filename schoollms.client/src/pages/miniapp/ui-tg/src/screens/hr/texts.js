/**
 * Xodim ekranlari matnlari — eski `hrapp/texts.ts` dan AYNAN (apostrof `‘`/`’`).
 *
 * Kirish (login), bot va Telegram Login Widget matnlari ko'chirilmagan: bizning
 * sessiya allaqachon Telegram imzosi bilan kiradi (hr-tasks.md §1).
 * `{name}` o'rinlari `t(key, { name })` bilan to'ldiriladi.
 */

const MESSAGES = {
  // ── Umumiy
  'common.loading': 'Yuklanmoqda…',
  'common.retry': 'Qayta urinish',
  'common.close': 'Yopish',
  'common.cancel': 'Bekor qilish',
  'common.sending': 'Yuborilmoqda…',
  'common.optional': 'ixtiyoriy',

  // ── Pastki menyu
  'nav.tasks': 'Topshiriqlar',
  'nav.summary': 'Natijam',

  // ── Sana tanlagich
  'date.today': 'Bugun',
  'date.yesterday': 'Kecha',
  'date.tomorrow': 'Ertaga',
  'date.back_to_today': 'Bugunga qaytish',
  'date.prev_week': 'Oldingi hafta',
  'date.next_week': 'Keyingi hafta',

  // ── Topshiriqlar ekrani
  'tasks.title': 'Topshiriqlarim',
  'tasks.summary': 'Bugun: {total} dan {done} tasi bajarildi',
  'tasks.summary_past': '{date}: {total} dan {done} tasi bajarildi',
  'tasks.summary_future': '{date}: {total} ta topshiriq rejalashtirilgan',
  'tasks.empty_today': 'Bugunga topshiriq yo‘q. Xohlasangiz o‘zingizga qo‘shing.',
  'tasks.empty_past': 'Bu kunda topshiriq bo‘lmagan.',
  'tasks.empty_future': 'Bu kunga hozircha topshiriq yo‘q.',
  'tasks.add': 'Topshiriq qo‘shish',
  'tasks.filter': 'Ko‘rinish',
  'tasks.filter_all': 'Barchasi',
  'tasks.filter_open': 'Ochiq',
  'tasks.filter_done': 'Bajarilgan',
  'tasks.empty_filter': 'Bu ko‘rinishda topshiriq yo‘q.',
  'tasks.loading_list': 'Topshiriqlar yuklanmoqda…',
  'tasks.more': 'Batafsil',
  'tasks.less': 'Yig‘ish',
  'tasks.due': 'Muddat: {value}',
  'tasks.no_due': 'Muddat belgilanmagan',
  'tasks.overdue': 'Muddati o‘tgan',
  'tasks.late': 'Kechikib bajarilgan',
  'tasks.self': 'O‘zim qo‘shganman',
  'tasks.returned_note': 'Direktor qaytardi: {comment}',
  'tasks.my_comment': 'Izohim: {comment}',
  'tasks.proof_count': '{count} ta isbot',
  'tasks.complete': 'Bajardim',
  'tasks.reopen': 'Bekor qilish',
  'tasks.reopening': 'Bekor qilinmoqda…',
  'tasks.accept': 'Tasdiqlash',
  'tasks.accepting': 'Tasdiqlanmoqda…',
  'tasks.accepted': 'Tasdiqlandi',
  'tasks.new_badge': 'Yangi',
  'tasks.new_task': 'Yangi topshiriq — hali tasdiqlanmagan',

  // ── Rahbarning ovozli izohi
  'tasks.voice': 'Ovozli izoh',
  'tasks.voice_open': '🎧 Ovozli izoh',
  'tasks.voice_open_with': '🎧 Ovozli izoh · {duration}',
  'tasks.voice_hide': 'Ovozni yashirish',
  'tasks.voice_failed': 'Ovozni bu yerda ijro etib bo‘lmadi.',
  'tasks.voice_download': 'Ovozni yuklab olish',

  // ── Status belgilari
  'status.open': 'Ochiq',
  'status.done': 'Bajarildi',
  'status.returned': 'Qaytarilgan',
  'status.missed': 'Bajarilmadi',
  'status.cancelled': 'Bekor qilingan',

  // ── KPI
  'kpi.progress': '{current} / {target} {unit}',
  'kpi.progress_plain': '{current} / {target}',
  'kpi.amount_placeholder': 'Qancha?',
  'kpi.note_placeholder': 'Izoh ({optional})',
  'kpi.add': 'Qo‘shish',
  'kpi.adding': 'Qo‘shilmoqda…',
  'kpi.invalid_amount': 'Raqam kiriting (masalan 12 yoki 12.5).',
  'kpi.zero_amount': 'Nol qo‘shishning ma‘nosi yo‘q.',
  'kpi.auto_done': 'Maqsadga yetdingiz — topshiriq avtomatik bajarildi deb belgilandi.',

  // ── Belgilash oynasi
  'complete.title': 'Bajarildi deb belgilash',
  'complete.comment_label': 'Nima qildingiz?',
  'complete.comment_placeholder': 'Qisqacha yozing — fayl biriktirmasangiz shu izohning o‘zi isbot bo‘ladi.',
  'complete.proof_label': 'Fayl biriktirish',
  'complete.proof_hint':
    'Istalgan turdagi fayl: rasm, hujjat, jadval, arxiv, ovoz, video… Ko‘pi bilan {max} ta, har biri {size} MB gacha.',
  'complete.proof_rule': 'Fayl biriktiring yoki nima qilganingizni yozing.',
  'complete.already_attached': 'Bu topshiriqda allaqachon {count} ta isbot bor.',
  'complete.take_photo': 'Suratga olish',
  'complete.pick_file': 'Fayl tanlash',
  'complete.remove_file': 'O‘chirish',
  'complete.uploading': 'Yuklanmoqda… {done} / {total}',
  'complete.submit': 'Bajardim',
  'complete.no_proof': 'Isbot kerak: fayl biriktiring yoki nima qilganingizni izohda yozing.',
  'complete.too_many': 'Ko‘pi bilan {max} ta fayl yuklash mumkin.',

  // ── Ovozli izoh yozish (belgilash oynasida — hr-tasks.md §8)
  'voice.record': 'Ovozli izoh',
  'voice.stop': 'To‘xtatish',
  'voice.recording': 'Yozilmoqda… {time} / {max}',
  'voice.file_name': 'Ovozli izoh · {duration}',
  'voice.too_long': 'Ovozli izoh {max} daqiqadan oshmasin.',
  'voice.denied':
    'Mikrofonga ruxsat berilmadi. Telegram sozlamalarida mikrofonni yoqing yoki ovozni fayl sifatida biriktiring.',
  'voice.stop_first': 'Avval ovoz yozishni to‘xtating.',

  // ── Yangi topshiriq oynasi
  'new.title': 'O‘zimga topshiriq',
  'new.title_label': 'Sarlavha',
  'new.title_placeholder': 'Masalan: Hisobotni tayyorlash',
  'new.title_required': 'Sarlavhani yozing.',
  'new.title_too_long': 'Sarlavha 200 belgidan oshmasin.',
  'new.description_label': 'Tavsif',
  'new.description_placeholder': 'Qo‘shimcha izoh ({optional})',
  'new.due_label': 'Muddat',
  'new.due_pick': 'Sana…',
  'new.due_time_label': 'Soat',
  'new.due_time_hint': 'Soatni o‘chirib qo‘ysangiz muddat kun oxiri (23:59) bo‘ladi.',
  'new.due_past': 'O‘tgan sanaga topshiriq qo‘yib bo‘lmaydi.',
  'new.due_past_time': 'Bu vaqt allaqachon o‘tib ketdi — kechroq soatni tanlang.',
  'new.submit': 'Qo‘shish',

  // ── Natijam ekrani
  'summary.title': 'Natijam',
  'summary.period': '{from} — {to}',
  'summary.percent': 'Bajarilish',
  'summary.total': 'Jami',
  'summary.done': 'Bajarilgan',
  'summary.missed': 'Bajarilmagan',
  'summary.open': 'Ochiq',
  'summary.late': 'Kechikkan',
  'summary.kpi_title': 'Raqamli topshiriqlar',
  'summary.kpi_empty': 'Bu oyda raqamli topshiriq yo‘q.',
  'summary.empty': 'Bu oyda hali topshiriq yo‘q.',

  // ── Xatolar
  'error.offline_detail': 'Internet aloqasi yo‘q. Tarmoqni tekshirib, qayta urinib ko‘ring.',
}

/** Matn. `{name}` o'rinlari `params` dan to'ldiriladi. */
export function t(key, params) {
  const text = MESSAGES[key] ?? key
  if (!params) return text
  return text.replace(/\{(\w+)\}/g, (whole, name) => (params[name] === undefined ? whole : String(params[name])))
}
