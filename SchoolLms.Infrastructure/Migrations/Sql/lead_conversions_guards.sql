-- ===========================================================================
--  lead_conversions — lid → o'quvchi statistikasi (LeadConversions migratsiyasi).
--  Moliyaviy EMAS: to'liq CRUD, birorta REVOKE yo'q (sales_marketing_guards.sql
--  bilan bir xil shakl). Jadvalda shaxsiy ma'lumot yo'q — faqat son.
-- ===========================================================================

DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[lead_conversions_guards] `app_rw` roli topilmadi — GRANT o''tkazib '
                     'yuborildi. deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON public.lead_conversions TO app_rw';
END
$guards$;
