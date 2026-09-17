-- ===========================================================================
--  `certificate_subjects` ni `certificates.subject_id` dan to'ldirish
--  Migratsiya: StudentsParityP2
--  Manba: docs/modules/students-parity.md §3.3 (Z-3)
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. O'zgartirish
--  kerak bo'lsa — YANGI migratsiya va yangi .sql fayl.
--  (Qoida `billing_guards.sql` dan olingan — P1-05.)
--
--  NIMA QILADI
--  -----------
--  Fani ko'rsatilgan har bir sertifikatga BITTA bog'lanish qatori yozadi.
--  Fansiz sertifikat (`subject_id is null` — "fanga bog'liq emas") qator
--  OLMAYDI: bo'sh bog'lanish yozish "fan noma'lum" ni "fan yo'q" ga
--  aylantirardi.
--
--  ESKI USTUN JOYIDA QOLADI
--  ------------------------
--  `certificates.subject_id` O'CHIRILMAYDI (`Up()` da birorta DROP yo'q) va
--  uni o'qiydigan mavjud kod o'zgarmaydi. Bu jadval uning NUSXASI bo'lib
--  boshlaydi va faqat sertifikat xizmati (Z-slice) ikkinchi fanni yozgach
--  undan kengroq bo'ladi. Ikkalasini birga yuritish — o'sha xizmatning ishi,
--  sxemaniki emas (`class_memberships` bilan bir xil vaziyat, §3.1).
--
--  YO'Q FAN BO'LISHI MUMKIN EMAS
--  -----------------------------
--  `certificates.subject_id` ning o'zi `subjects(id)` ga FK (`on delete set
--  null`, ParityModel.cs), ya'ni null bo'lmagan har bir qiymat mavjud fanni
--  ko'rsatadi. Shuning uchun bu yerda `join` ham, "topilmadi" hisoboti ham
--  kerak emas — yangi jadvalning `on delete restrict` FK'si yiqilmaydi.
--
--  QAYTA YURGIZILSA
--  ----------------
--  `ON CONFLICT DO NOTHING` — fayl IDEMPOTENT: ikkinchi yurishda hech narsa
--  qo'shilmaydi (kalit (certificate_id, subject_id)).
-- ===========================================================================

INSERT INTO certificate_subjects (certificate_id, subject_id)
SELECT c.id, c.subject_id
FROM certificates c
WHERE c.subject_id IS NOT NULL
ON CONFLICT (certificate_id, subject_id) DO NOTHING;

DO $backfill$
DECLARE
    linked  bigint;
    skipped bigint;
BEGIN
    SELECT count(*) INTO linked FROM certificate_subjects;
    SELECT count(*) INTO skipped FROM certificates WHERE subject_id IS NULL;

    RAISE NOTICE '[students_parity_p2_backfill] % ta sertifikat-fan bog''lanishi yozildi; '
                 '% ta sertifikat fansiz (o''zgarishsiz qoldi).', linked, skipped;
END
$backfill$;
