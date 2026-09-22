-- ===========================================================================
--  boarding_attendance — kechki dars va yotoqxona davomati (BoardingAttendance migratsiyasi).
--  Moliyaviy EMAS: to'liq CRUD, birorta REVOKE yo'q (notification_states_guards.sql bilan
--  bir xil shakl). study_groups.is_track — mavjud jadvalga ustun, grantlari o'zgarmaydi.
-- ===========================================================================

DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[boarding_attendance_guards] `app_rw` roli topilmadi — GRANT o''tkazib '
                     'yuborildi. deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON public.boarding_attendance TO app_rw';
END
$guards$;
