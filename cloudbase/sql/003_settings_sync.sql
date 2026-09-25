-- GGman cloud settings sync.
-- Stores only portable user preferences. Machine-local paths and window coordinates stay local.

BEGIN;

CREATE TABLE IF NOT EXISTS public.ggman_settings_sync (
    owner_id TEXT PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    settings_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

ALTER TABLE public.ggman_settings_sync ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS ggman_settings_sync_select_own ON public.ggman_settings_sync;
CREATE POLICY ggman_settings_sync_select_own
    ON public.ggman_settings_sync
    FOR SELECT
    USING (owner_id = auth.uid());

DROP POLICY IF EXISTS ggman_settings_sync_insert_own ON public.ggman_settings_sync;
CREATE POLICY ggman_settings_sync_insert_own
    ON public.ggman_settings_sync
    FOR INSERT
    WITH CHECK (owner_id = auth.uid());

DROP POLICY IF EXISTS ggman_settings_sync_update_own ON public.ggman_settings_sync;
CREATE POLICY ggman_settings_sync_update_own
    ON public.ggman_settings_sync
    FOR UPDATE
    USING (owner_id = auth.uid())
    WITH CHECK (owner_id = auth.uid());

DROP POLICY IF EXISTS ggman_settings_sync_delete_own ON public.ggman_settings_sync;
CREATE POLICY ggman_settings_sync_delete_own
    ON public.ggman_settings_sync
    FOR DELETE
    USING (owner_id = auth.uid());

REVOKE ALL ON TABLE public.ggman_settings_sync FROM PUBLIC;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.ggman_settings_sync TO anon, authenticated;

CREATE OR REPLACE FUNCTION public.ggman_get_settings_sync()
RETURNS JSONB
LANGUAGE SQL
STABLE
SECURITY INVOKER
AS $$
    SELECT COALESCE(
        (
            SELECT jsonb_build_object(
                'settings', settings_json,
                'updated_at', updated_at
            )
            FROM public.ggman_settings_sync
            WHERE owner_id = auth.uid()
        ),
        '{}'::jsonb
    );
$$;

CREATE OR REPLACE FUNCTION public.ggman_set_settings_sync(p_settings JSONB)
RETURNS JSONB
LANGUAGE SQL
VOLATILE
SECURITY INVOKER
AS $$
    INSERT INTO public.ggman_settings_sync (owner_id, settings_json, updated_at)
    VALUES (auth.uid(), p_settings, now())
    ON CONFLICT (owner_id)
    DO UPDATE SET
        settings_json = EXCLUDED.settings_json,
        updated_at = EXCLUDED.updated_at
    RETURNING jsonb_build_object('updated_at', updated_at);
$$;

REVOKE ALL ON FUNCTION public.ggman_get_settings_sync() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.ggman_get_settings_sync() TO anon, authenticated;

REVOKE ALL ON FUNCTION public.ggman_set_settings_sync(JSONB) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.ggman_set_settings_sync(JSONB) TO anon, authenticated;

COMMIT;
