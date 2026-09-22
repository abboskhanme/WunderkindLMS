-- ===========================================================================
--  notification_states — foydalanuvchi bildirishnomani qachon o'qigani / o'chirgani
--  (NotificationStates migratsiyasi). Moliyaviy EMAS: to'liq CRUD, birorta REVOKE
--  yo'q (lead_conversions_guards.sql bilan bir xil shakl). Qatorni o'chirish hech
--  qanday voqeani yo'qotmaydi — bildirishnoma o'zi mavjud ma'lumotdan yig'iladi.
-- ===========================================================================

DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[notification_states_guards] `app_rw` roli topilmadi — GRANT o''tkazib '
                     'yuborildi. deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON public.notification_states TO app_rw';
END
$guards$;
