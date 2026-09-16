-- ===========================================================================
--  Ikkinchi to'lqin sxemasi — boshlang'ich katalog qatorlari
--  Migratsiya: ParityWave2Schema
--  Manba: docs/modules/existing-module-gaps.md §2.2, §3.5
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG — `parity_wave2_guards.sql` boshidagi izohga qarang.
--
--  IDEMPOTENT: `ON CONFLICT (name) DO NOTHING`. Migratsiya qayta yurgizilsa
--  ham, bu fayl qo'lda ishlatilsa ham hech narsa o'zgarmaydi va xato chiqmaydi.
--  Ikkala jadvalda ham `name` unikal (ParityModel.cs), ya'ni mojaro maqsadi
--  aynan shu ustun.
--
--  ID'lar BARQAROR (qo'lda yozilgan UUID), `gen_random_uuid()` EMAS —
--  `billing_seed.sql` dagi bilan bir xil sabab: katalog id'si hujjatda,
--  testda va nosozlikni tekshirishda uchraydi.
--
--  ADMIN BULARNI O'ZGARTIRA OLADI. Nomi ham, rangi ham, tartibi ham — oddiy
--  `UPDATE`. Shuning uchun `DO NOTHING`: migratsiya qayta yurganda maktabning
--  tahririni USTIGA YOZMAYDI.
-- ===========================================================================


-- ---------------------------------------------------------------------------
-- 1) Qarzdor bilan ishlash holatlari (§3.5)
--
--    §3.5 bu katalogni "user-defined" deydi va shunday ham qoladi — bu yerdagi
--    to'rtta qator FAQAT boshlang'ich nuqta. Ularsiz "Amal qo'shish" oynasi
--    birinchi kuni bo'sh ochilardi va administrator holatlarni o'ylab
--    topmaguncha ishlatib bo'lmasdi.
--
--    Ranglar: kulrang (boshlandi) → sariq (va'da bor) → qizil (aloqa yo'q) →
--    yashil (yopildi). Bu ketma-ketlik `position` da ham takrorlanadi.
-- ---------------------------------------------------------------------------
INSERT INTO debtor_statuses (id, name, color, hint, position, is_active) VALUES
    ('00000000-0000-0000-0000-0000000000d1', 'Bog''lanildi',
     '#8E8E93', 'Ota-ona bilan bog''lanildi, javob kutilmoqda.', 1, true),
    ('00000000-0000-0000-0000-0000000000d2', 'To''lash va''da qilindi',
     '#FF9500', 'Yangi to''lov sanasi kelishildi — sanani "Va''da" maydoniga yozing.', 2, true),
    ('00000000-0000-0000-0000-0000000000d3', 'Javob bermayapti',
     '#FF3B30', 'Bir necha marta urinildi, aloqa yo''q.', 3, true),
    ('00000000-0000-0000-0000-0000000000d4', 'To''lov qilindi',
     '#34C759', 'Qarz yopildi — tasdiqlash uchun to''lovlar tarixiga qarang.', 4, true)
ON CONFLICT (name) DO NOTHING;


-- ---------------------------------------------------------------------------
-- 2) Arxivlash sabablari (§2.2)
--
--    "Boshqa" ATAYLAB ro'yxatda va ATAYLAB oxirgi (`position = 99`): §2.2
--    tanlovni erkin matn bilan birga ishlatishni talab qiladi — katalog qatori
--    guruhlash uchun, yonidagi `students.archive_reason` matni tafsilot uchun.
--    "Boshqa" siz administrator eng yaqin, lekin noto'g'ri sababni tanlab
--    qo'yardi va hisobot jimgina buzilardi.
-- ---------------------------------------------------------------------------
INSERT INTO student_archive_reasons (id, name, is_active, position) VALUES
    ('00000000-0000-0000-0000-0000000000a1', 'Maktabni bitirdi',            true,  1),
    ('00000000-0000-0000-0000-0000000000a2', 'Boshqa maktabga o''tdi',      true,  2),
    ('00000000-0000-0000-0000-0000000000a3', 'Boshqa shaharga ko''chdi',    true,  3),
    ('00000000-0000-0000-0000-0000000000a4', 'Oilaviy sabablarga ko''ra',   true,  4),
    ('00000000-0000-0000-0000-0000000000a5', 'To''lov qiyinchiligi',        true,  5),
    ('00000000-0000-0000-0000-0000000000a6', 'O''qishdan chetlashtirildi',  true,  6),
    ('00000000-0000-0000-0000-0000000000a9', 'Boshqa',                      true, 99)
ON CONFLICT (name) DO NOTHING;


-- ---------------------------------------------------------------------------
-- 3) `certificate_types` — ATAYLAB BO'SH
--
--    §2.3 turlarni Sozlamalarda boshqariladigan ro'yxat deb belgilaydi va
--    birorta ham nom TAKLIF QILMAYDI. "IELTS" yoki "SAT" ni bu yerga yozish —
--    maktab qaysi imtihonlarni o'tkazishini bizning taxminimiz bo'lardi, va
--    noto'g'ri taxmin `is_scored` bayrog'ini ham noto'g'ri qo'yardi (ballli
--    tur bilan ballsiz tur butunlay boshqacha ekran beradi). Birinchi turni
--    maktab o'zi kiritadi.
-- ---------------------------------------------------------------------------
