-- ===========================================================================
--  O'quvchi kartochkasi (P1) — `app_rw` grantlari
--  Migratsiya: StudentsParityP1
--  Manba: docs/modules/students-parity.md §3.2
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. O'zgartirish
--  kerak bo'lsa — YANGI migratsiya va yangi .sql fayl.
--  (Qoida `billing_guards.sql` dan olingan — P1-05.)
--
--  NEGA GRANT MIGRATSIYANING O'ZIDA
--  --------------------------------
--  Migratsiyani `schoollms_owner` bajaradi, ilova esa `app_rw` bilan ulanadi
--  va u hech narsaga EGA emas. Grant unutilsa ilova muammosiz ishga tushadi va
--  faqat BIRINCHI so'rovda SQLSTATE 42501 bilan yiqiladi — ya'ni xato ishlab
--  chiqishda emas, foydalanuvchida chiqadi. Shuning uchun grant jadval bilan
--  birga keladi. Rol hali yo'q bo'lsa (test harness'i) — jim o'tib ketamiz.
--
--  NEGA BIRORTA `REVOKE` YO'Q — ATAYLAB
--  ------------------------------------
--  To'rtala jadval ham moliyaviy EMAS: ichlarida summa ham, jurnal (ledger)
--  yozuvi ham, hisob-faktura yoki kassa smenasiga havola ham yo'q.
--  `student_contracts` shartnoma HUJJATINI qayd qiladi (raqami, sanasi, fayli),
--  pul harakatini emas — pul `invoices` va `payments` da, ular esa o'z
--  qulflari ostida. SPEC §4.1 ning o'zgarmaslik qoidasi PULGA tegishli;
--  uni "muhim ko'ringan hamma narsaga" cho'zish qimmatga tushadi: shartnoma
--  raqamidagi xatoni tuzatib bo'lmaydigan qilib qo'yardi.
--
--  Ustun darajasida grant ham yo'q: `GRANT ... ON <jadval>` butun jadvalga
--  beriladi, ya'ni keyin qo'shiladigan ustun avtomatik o'sha huquq ostiga
--  tushadi.
-- ===========================================================================

DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[students_parity_p1_guards] `app_rw` roli topilmadi — GRANT o''tkazib '
                     'yuborildi. deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON '
            'public.student_statuses, public.student_comments, '
            'public.student_contracts, public.rooms '
            'TO app_rw';
END
$guards$;
