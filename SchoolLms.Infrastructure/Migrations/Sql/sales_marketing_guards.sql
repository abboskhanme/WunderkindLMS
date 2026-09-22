-- ===========================================================================
--  Savdo va marketing (ariza formasi + yangiliklar) — `app_rw` grantlari
--  Migratsiya: SalesAndMarketing
--  Manba: docs/modules/sales-marketing.md §4.5
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. O'zgartirish
--  kerak bo'lsa — YANGI migratsiya va yangi .sql fayl.
--  (Qoida `students_parity_p1_guards.sql` va `transaction_types_guards.sql`
--  dan olingan.)
--
--  NEGA GRANT MIGRATSIYANING O'ZIDA
--  --------------------------------
--  Migratsiyani `schoollms_owner` bajaradi, ilova esa `app_rw` bilan ulanadi
--  va u hech narsaga EGA emas. Grant unutilsa ilova muammosiz ishga tushadi va
--  faqat BIRINCHI so'rovda SQLSTATE 42501 bilan yiqiladi — ya'ni xato ishlab
--  chiqishda emas, foydalanuvchida chiqadi. Bu modulda esa o'sha "birinchi
--  so'rov" ANONIM bo'lishi mumkin: Instagram bio'sidagi havolani ochgan
--  ota-ona. Shuning uchun grant jadval bilan birga keladi. Rol hali yo'q
--  bo'lsa (test harness'i) — jim o'tib ketamiz.
--
--  NEGA TO'LIQ CRUD VA BIRORTA `REVOKE` YO'Q — ATAYLAB
--  ----------------------------------------------------
--  Uchala jadval ham moliyaviy EMAS: ichlarida summa ham, jurnal (ledger)
--  yozuvi ham, hisob-faktura, kvitansiya yoki kassa smenasiga havola ham yo'q.
--  `surveys` — forma sozlamasi, `survey_submissions` — ota-ona yozgan matn,
--  `news` — e'lon matni. `docs/SPEC.md` §4.1 ning o'zgarmaslik qoidasi PULGA
--  tegishli; uni "muhim ko'ringan hamma narsaga" cho'zish qimmatga tushadi.
--  Shu sababdan bu uch nom `deploy/init-roles.sql` §5 ro'yxatiga ham
--  QO'SHILMAYDI (`existing-module-gaps.md` §8 buni o'z so'zlari bilan aytadi:
--  "None of the tables proposed in this file is financial; do not copy the
--  append-only REVOKE pattern onto them").
--
--  `leads` — YANGI USTUNLAR, GRANT O'ZGARMAYDI
--  ---------------------------------------------
--  Shu migratsiya `leads` ga uchta ustun qo'shadi (`source`, `survey_id`,
--  `created_at`), lekin ustun darajasida grant yo'q: `GRANT ... ON <jadval>`
--  butun jadvalga beriladi, ya'ni yangi ustun avtomatik mavjud huquq ostiga
--  tushadi. `leads` ning o'zi `init-roles.sql` 4-qadamidagi bazaviy
--  to'liq CRUD ostida — bu yerda qayta yozish shart emas.
-- ===========================================================================

DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[sales_marketing_guards] `app_rw` roli topilmadi — GRANT o''tkazib '
                     'yuborildi. deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON '
            'public.surveys, public.survey_submissions, public.news '
            'TO app_rw';
END
$guards$;
