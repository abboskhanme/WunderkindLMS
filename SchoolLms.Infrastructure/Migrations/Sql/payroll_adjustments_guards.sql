-- ===========================================================================
--  Bonus / jarima — `app_rw` grantlari
--  Migratsiya: PayrollAdjustments
--  Manba: docs/modules/finance-parity.md §3.2 (B2, B3); SPEC §4.1
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. O'zgartirish
--  kerak bo'lsa — YANGI migratsiya va yangi .sql fayl.
--  (Qoida `students_parity_p1_guards.sql` va `finance_parity_guards.sql`
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
--  IKKI JADVAL, IKKI XIL QOIDA
--  ----------------------------
--  * `adjustment_reasons` (B2) — sabab KATALOGI. Moliyaviy EMAS: ichida
--    summa ham, jurnal yozuvi ham yo'q. To'liq CRUD — o'chirish o'rniga
--    faolsizlantirish (`is_active=false`) XIZMAT QATLAMIDA qaror qilinadi,
--    bazada emas (`students_parity_p1_guards.sql` dagi katalog jadvallari
--    bilan bir xil mantiq).
--  * `payroll_adjustments` (B3) — PUL YOZUVI: xodimning oylik hisob-kitobiga
--    kiradigan summa. FAQAT QO'SHILADI — xato yozuv `reversal_of` bilan
--    qarshi qator qo'shib tuzatiladi, xuddi `cash_handovers` dagidek.
-- ===========================================================================

DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[payroll_adjustments_guards] `app_rw` roli topilmadi — GRANT/REVOKE '
                     'o''tkazib yuborildi. Bu bazada bonus/jarima registri uchun moliyaviy '
                     'o''zgarmaslik YO''Q. deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    -- ---- B2: sabab katalogi — to'liq CRUD ----
    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON public.adjustment_reasons TO app_rw';

    -- ---- B3: bonus/jarima registri — o'qish va yangi qator ----
    EXECUTE 'GRANT SELECT, INSERT ON public.payroll_adjustments TO app_rw';

    -- ---- B3: tahrirlash va o'chirish — YO'Q ----
    -- Xato yozuv `reversal_of` bilan qarshi qator qo'shib tuzatiladi. Bu
    -- summa xodimning oylik hisob-kitobiga qo'shiladigan raqam — uni jimgina
    -- o'zgartirish yoki o'chirish xuddi to'lovni o'zgartirish bilan bir xil
    -- og'irlikdagi harakat (SPEC §4).
    --
    -- TRUNCATE ham — "hamma qatorni o'chirish" ning DELETE'siz yo'li.
    EXECUTE 'REVOKE UPDATE, DELETE, TRUNCATE ON public.payroll_adjustments FROM app_rw';
END
$guards$;
