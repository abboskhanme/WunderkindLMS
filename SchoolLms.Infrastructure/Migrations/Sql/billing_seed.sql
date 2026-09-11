-- ===========================================================================
--  BillingCore — boshlang'ich ma'lumot (SPEC §3.7, §8.1 Q6)
--  Migratsiya: 20260911061113_BillingCore · Vazifa: P1-05
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG — billing_guards.sql boshidagi izohga qarang.
--
--  IDEMPOTENT: `ON CONFLICT ... DO NOTHING`. Migratsiya qayta yurgizilsa ham,
--  bu fayl qo'lda ishlatilsa ham hech narsa o'zgarmaydi va xato chiqmaydi.
--
--  ID'lar BARQAROR (qo'lda yozilgan UUID), `gen_random_uuid()` EMAS. Sabab:
--  toifa id'lari hujjatda, testlarda va nosozlikni tekshirishda uchraydi —
--  har muhitda boshqacha bo'lsa, prod jurnalidagi id'ni lokalda qidirib
--  topib bo'lmaydi. Toifa — ma'lumotnoma, tasodifiy qator emas.
-- ===========================================================================


-- ---------------------------------------------------------------------------
-- 1) Beshta to'lov toifasi (SPEC §3.7)
--    `code` — mashina kaliti, O'ZGARMAYDI. `name` — UI uchun, admin
--    o'zgartirishi mumkin, shuning uchun DO NOTHING (ustiga yozmaymiz).
-- ---------------------------------------------------------------------------
INSERT INTO fee_categories (id, code, name, is_active) VALUES
    ('00000000-0000-0000-0000-0000000000c1', 'tuition',   'O''qish to''lovi', true),
    ('00000000-0000-0000-0000-0000000000c2', 'bus',       'Avtobus',         true),
    ('00000000-0000-0000-0000-0000000000c3', 'dormitory', 'Yotoqxona',       true),
    ('00000000-0000-0000-0000-0000000000c4', 'meals',     'Ovqatlanish',     true),
    ('00000000-0000-0000-0000-0000000000c5', 'other',     'Boshqa',          true)
ON CONFLICT (code) DO NOTHING;


-- ---------------------------------------------------------------------------
-- 2) Moliya sozlamalari — yagona qator (mijoz javobi, SPEC §8.1 Q6)
--
--    To'lov muddati QAT'IY RAQAM EMAS: `payment_due_day` (sukut 10) va
--    `overdue_after_day` (sukut 15). Ularni admin UI'dan o'zgartiradi —
--    bu `UPDATE`, hech qachon migratsiya emas.
--
--    `updated_by` = NULL — birinchi qatorni odam emas, tizim yaratdi.
-- ---------------------------------------------------------------------------
INSERT INTO billing_settings (id, payment_due_day, overdue_after_day, updated_at, updated_by)
VALUES ('00000000-0000-0000-0000-0000000000b1', 10, 15, now(), NULL)
ON CONFLICT (id) DO NOTHING;
