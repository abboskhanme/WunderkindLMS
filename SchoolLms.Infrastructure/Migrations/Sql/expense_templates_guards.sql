-- ===========================================================================
--  Rejalashtirilgan chiqim shabloni (F6.01) — `app_rw` grantlari
--  Migratsiya: ExpenseTemplates
--  Manba: docs/modules/finance-parity.md §2.6.3 (F6.01), §3.3 (C7)
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. O'zgartirish
--  kerak bo'lsa — YANGI migratsiya va yangi .sql fayl.
--  (Qoida `students_parity_p1_guards.sql` dan olingan.)
--
--  NEGA GRANT MIGRATSIYANING O'ZIDA
--  --------------------------------
--  Migratsiyani `schoollms_owner` bajaradi, ilova esa `app_rw` bilan ulanadi
--  va u hech narsaga EGA emas. Grant unutilsa ilova muammosiz ishga tushadi va
--  faqat BIRINCHI so'rovda SQLSTATE 42501 bilan yiqiladi — ya'ni xato ishlab
--  chiqishda emas, foydalanuvchida chiqadi. Shuning uchun grant jadval bilan
--  birga keladi. Rol hali yo'q bo'lsa (test harness'i) — jim o'tib ketamiz.
--
--  NEGA TO'LIQ CRUD, `students_parity_p1_guards.sql` KABI — VA FINANCE
--  PARITY (§4.1) FAQAT-INSERT QOIDASI EMAS
--  --------------------------------------------------------------------
--  `expense_templates` moliyaviy EMAS: ichida summa bor, lekin bu summa
--  hech qachon jurnalga (`ledger_entries`) o'z-o'zidan tushmaydi — u faqat
--  "kutilgan xarajat" ta'rifi (ExpenseTemplates.cs boshidagi izoh). Haqiqiy
--  pul harakati hamon `expenses` + `ledger_entries` orqali, SPEC §4.1/§4.5
--  ning to'liq nazorati bilan yoziladi. Shablonni tahrirlash yoki o'chirish
--  hech qanday pulni o'zgartirmaydi — shuning uchun `AdjustmentReason` yoki
--  `FeeCategory` kabi oddiy kataloq: to'liq CRUD, o'zgarmaslik talab qilinmaydi.
--
--  Ustun darajasida grant ham yo'q: `GRANT ... ON <jadval>` butun jadvalga
--  beriladi, ya'ni keyin qo'shiladigan ustun avtomatik o'sha huquq ostiga
--  tushadi.
-- ===========================================================================

DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[expense_templates_guards] `app_rw` roli topilmadi — GRANT o''tkazib '
                     'yuborildi. deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON public.expense_templates TO app_rw';
END
$guards$;
