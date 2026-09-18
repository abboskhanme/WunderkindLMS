-- ===========================================================================
--  Tranzaksiya turi katalogi (Kirim/Chiqim) — `app_rw` grantlari
--  Migratsiya: TransactionTypes
--  Manba: mijoz yuborgan EduSchool kassa kirim shakli va moliya sozlamalari
--  ekrani (2026-09-18); docs/modules/existing-module-gaps.md §3.4 ("Editable
--  transaction-type tree" — declined, Accounts.cs yopiq).
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. O'zgartirish
--  kerak bo'lsa — YANGI migratsiya va yangi .sql fayl.
--  (Qoida `expense_templates_guards.sql` va `students_parity_p1_guards.sql`
--  dan olingan.)
--
--  NEGA GRANT MIGRATSIYANING O'ZIDA
--  --------------------------------
--  Migratsiyani `schoollms_owner` bajaradi, ilova esa `app_rw` bilan ulanadi
--  va u hech narsaga EGA emas. Grant unutilsa ilova muammosiz ishga tushadi va
--  faqat BIRINCHI so'rovda SQLSTATE 42501 bilan yiqiladi — ya'ni xato ishlab
--  chiqishda emas, foydalanuvchida chiqadi. Shuning uchun grant jadval bilan
--  birga keladi. Rol hali yo'q bo'lsa (test harness'i) — jim o'tib ketamiz.
--
--  NEGA TO'LIQ CRUD — MOLIYAVIY EMAS (SPEC §4.1 BU YERGA TEGISHLI EMAS)
--  ------------------------------------------------------------------------
--  `transaction_types` ichida summa yo'q, faqat nom/tartib/faollik — kassa
--  tranzaksiyasiga (`cash_box_transactions.transaction_type_id`) yopishtiriladigan
--  YORLIQ. Hisobot AKKAUNT (`Accounts.cs`) bo'yicha yig'iladi, bu jadval
--  ORQALI EMAS — `TransactionTypes.cs` boshidagi izoh. Shuning uchun
--  `AdjustmentReason`/`ExpenseTemplate` kabi oddiy kataloq: to'liq CRUD,
--  o'zgarmaslik talab qilinmaydi. "Seed qilingan turni o'chirib bo'lmaydi"
--  qoidasi (faqat rename) XIZMAT QATLAMIDA (`TransactionTypeService`),
--  bazada emas — `is_seeded` ustuni oddiy `boolean`, DB DELETE huquqi
--  baribir kerak (admin qo'shgan yangi turni o'chirish uchun).
--
--  `cash_box_transactions` — YANGI USTUN, GRANT O'ZGARMAYDI
--  ------------------------------------------------------------
--  `transaction_type_id` shu migratsiya qo'shgan yangi ustun, lekin
--  `cash_box_transactions` jadvalining o'zi `cash_boxes_guards.sql` da
--  ALLAQACHON faqat SELECT+INSERT (SPEC §4.1). Ustun darajasida grant yo'q
--  (`GRANT ... ON <jadval>` butun jadvalga beriladi) — ya'ni yangi ustun
--  avtomatik o'sha huquq ostiga tushadi, bu yerda qayta yozish shart emas.
-- ===========================================================================

DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[transaction_types_guards] `app_rw` roli topilmadi — GRANT o''tkazib '
                     'yuborildi. deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON public.transaction_types TO app_rw';
END
$guards$;
