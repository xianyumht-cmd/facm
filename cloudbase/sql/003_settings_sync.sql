-- GGman cloud settings sync migration.
-- Run after the CloudBase identity schema is present.
-- Idempotent: safe to execute again.

BEGIN;

CREATE TABLE IF NOT EXISTS public.ggman_settings (
    owner_id TEXT PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    settings_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

ALTER TABLE public.ggman_settings ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS ggman_settings_select_own ON public.ggman_settings;
CREATE POLICY ggman_settings_select_own
    ON public.ggman_settings
    FOR SELECT
    USING (owner_id = auth.uid());

DROP POLICY IF EXISTS ggman_settings_insert_own ON public.ggman_settings;
CREATE POLICY ggman_settings_insert_own
    ON public.ggman_settings
    FOR INSERT
    WITH CHECK (owner_id = auth.uid());

DROP POLICY IF EXISTS ggman_settings_update_own ON public.ggman_settings;
CREATE POLICY ggman_settings_update_own
    ON public.ggman_settings
    FOR UPDATE
    USING (owner_id = auth.uid())
    WITH CHECK (owner_id = auth.uid());

REVOKE ALL ON TABLE public.ggman_settings FROM PUBLIC;
GRANT SELECT, INSERT, UPDATE ON TABLE public.ggman_settings TO anon, authenticated;

CREATE OR REPLACE FUNCTION public.ggman_get_settings()
RETURNS JSONB
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
    SELECT COALESCE(
        (
            SELECT settings_json
            FROM public.ggman_settings
            WHERE owner_id = auth.uid()
        ),
        '{}'::jsonb
    );
$$;

CREATE OR REPLACE FUNCTION public.ggman_set_settings(p_settings JSONB)
RETURNS VOID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_owner TEXT := auth.uid();
BEGIN
    IF v_owner IS NULL OR length(v_owner) = 0 THEN
        RAISE EXCEPTION 'authentication required';
    END IF;

    IF p_settings IS NULL OR jsonb_typeof(p_settings) <> 'object' THEN
        RAISE EXCEPTION 'settings object required';
    END IF;

    INSERT INTO public.ggman_settings (owner_id, settings_json, updated_at)
    VALUES (v_owner, p_settings, now())
    ON CONFLICT (owner_id)
    DO UPDATE SET
        settings_json = EXCLUDED.settings_json,
        updated_at = EXCLUDED.updated_at;
END;
$$;

REVOKE ALL ON FUNCTION public.ggman_get_settings() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.ggman_get_settings() TO anon, authenticated;

REVOKE ALL ON FUNCTION public.ggman_set_settings(JSONB) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.ggman_set_settings(JSONB) TO anon, authenticated;

COMMIT;
