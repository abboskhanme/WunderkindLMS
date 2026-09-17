-- ===========================================================================
--  O'quv guruhlari sxemasi — `app_rw` grantlari
--  Migratsiya: StudyGroupsAndMemberships
--  Manba: docs/modules/students-parity.md §3.1
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. O'zgartirish
--  kerak bo'lsa — YANGI migratsiya va yangi .sql fayl.
--  (Qoida `billing_guards.sql` dan aynan olingan — P1-05.)
--
--  NEGA MIGRATSIYADA, `deploy/init-roles.sql` KUTILMAYDI
--  ----------------------------------------------------
--  Migratsiyani `schoollms_owner` bajaradi, ilova esa `app_rw` bilan ishlaydi
--  va u hech narsaga EGA emas. `init-roles.sql` dagi `ALTER DEFAULT PRIVILEGES`
--  yangi jadvalga huquq beradi — LEKIN faqat o'sha fayl ishga tushirilgan
--  bazada. Grant unutilsa ilova ishga tushadi va faqat BIRINCHI so'rovda
--  SQLSTATE 42501 bilan yiqiladi. Shuning uchun grant jadval bilan birga, shu
--  migratsiyaning o'zida keladi.
--
--  ROL HALI YO'Q BO'LSA — jim o'tib ketamiz (test harness'i migratsiyani
--  `app_rw` siz ham qo'llay olishi kerak, docs/TESTING.md).
--
--  ==========================================================================
--  NEGA BU YERDA BIRORTA `REVOKE` YO'Q — ATAYLAB
--  ==========================================================================
--  Beshta jadvalning HECH BIRI moliyaviy emas: ichlarida summa ham, jurnal
--  (ledger) yozuvi ham, hisob-faktura yoki kassa smenasiga havola ham yo'q.
--  SPEC §4.1 ning o'zgarmaslik qoidasi PULGA tegishli va uni "muhim ko'ringan
--  hamma narsaga" cho'zish qimmatga tushadi.
--
--  `study_group_members` va `class_memberships` — TARIX, lekin himoyasi
--  grantda emas, XATTI-HARAKATDA: xizmat qatorni o'chirmaydi, `left_on`
--  qo'yadi (§2.1.4 "Membership has dates, not deletes"). DELETE baribir
--  kerak: o'quvchi butunlay o'chirilganda FK `ON DELETE CASCADE` ishlaydi
--  (u jadval EGASI nomidan yuradi, ya'ni grantga bog'liq emas), noto'g'ri
--  kiritilgan a'zolikni esa administrator o'sha kuni o'chira olishi kerak —
--  aks holda u "chiqdi" deb yopilgan soxta tarix bo'lib qolardi.
--
--  Mavjud jadvallarga (`subjects`, `school_meta`, `schedule_templates`,
--  `week_assignments`, `journal_entries`, `lesson_notes`, `quarter_grades`)
--  YANGI GRANT KERAK EMAS: PostgreSQL'da huquq JADVAL darajasida beriladi va
--  yangi ustun avtomatik ravishda o'sha huquq ostiga tushadi.
-- ===========================================================================

DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[study_groups_guards] `app_rw` roli topilmadi — GRANT o''tkazib yuborildi. '
                     'deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON '
            'public.study_groups, public.study_group_classes, '
            'public.study_group_teachers, public.study_group_members, '
            'public.class_memberships '
            'TO app_rw';
END
$guards$;
