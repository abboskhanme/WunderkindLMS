-- ===========================================================================
--  Kunlik davomat belgisi — `app_rw` grantlari
--  Migratsiya: DailyAttendanceMarks
--  Manba: mijoz, 2026-09-18 — "bitta mas'ul xodim barcha sinflar davomatini
--  qilolsin" (izoh: `SchoolLms.Domain/DailyAttendance.cs`).
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. O'zgartirish
--  kerak bo'lsa — YANGI migratsiya va yangi .sql fayl.
--  (Qoida `billing_guards.sql` dan olingan — P1-05.)
--
--  NEGA MIGRATSIYADA: migratsiyani `schoollms_owner` bajaradi, ilova esa
--  `app_rw` bilan ishlaydi va yangi jadvalga hech qanday huquqi yo'q.
--  `deploy/init-roles.sql` dagi `ALTER DEFAULT PRIVILEGES` faqat o'sha fayl
--  ishga tushirilgan bazada yordam beradi — shuning uchun grant jadval bilan
--  BIRGA keladi (`study_groups_guards.sql` dagi bilan bir xil sabab).
--
--  NEGA `REVOKE` YO'Q: jadval moliyaviy emas — ichida summa ham, jurnal
--  yozuvi ham yo'q, faqat "shu sinfning shu kuni belgilandi" belgisi. SPEC
--  §4.1 ning o'zgarmaslik qoidasi PULGA tegishli.
--
--  DELETE ham beriladi: mas'ul xodim xato kunni belgilab qo'ysa (masalan
--  dam olish kuni), belgini olib tashlash kerak bo'ladi. Davomatning O'ZI
--  bu jadvalda emas — u jurnalda (`journal_entries.reason_id`), ya'ni bu
--  qatorni o'chirish bitta ham yo'qlikni yo'qotmaydi.
--
--  `journal_entries` GA YANGI GRANT KERAK EMAS: u allaqachon `app_rw` uchun
--  ochiq va bu migratsiya unga ustun ham qo'shmaydi.
-- ===========================================================================

DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[daily_attendance_marks_guards] `app_rw` roli topilmadi — GRANT o''tkazib yuborildi. '
                     'deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON public.daily_attendance_marks TO app_rw';
END
$guards$;
