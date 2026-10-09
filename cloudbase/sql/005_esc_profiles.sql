-- GGman ESC game configuration backup. Keep separate from ggman_settings_sync.
-- One manually uploaded profile per authenticated CloudBase anonymous subject.
BEGIN;

CREATE TABLE IF NOT EXISTS public.ggman_esc_profiles (
    owner_id TEXT PRIMARY KEY,
    payload JSONB NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ggman_esc_profiles_payload_size CHECK (octet_length(payload::text) <= 400000)
);

ALTER TABLE public.ggman_esc_profiles ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS ggman_esc_profiles_select_own ON public.ggman_esc_profiles;
CREATE POLICY ggman_esc_profiles_select_own ON public.ggman_esc_profiles
    FOR SELECT USING (owner_id = auth.uid()::text);
DROP POLICY IF EXISTS ggman_esc_profiles_insert_own ON public.ggman_esc_profiles;
CREATE POLICY ggman_esc_profiles_insert_own ON public.ggman_esc_profiles
    FOR INSERT WITH CHECK (owner_id = auth.uid()::text);
DROP POLICY IF EXISTS ggman_esc_profiles_update_own ON public.ggman_esc_profiles;
CREATE POLICY ggman_esc_profiles_update_own ON public.ggman_esc_profiles
    FOR UPDATE USING (owner_id = auth.uid()::text) WITH CHECK (owner_id = auth.uid()::text);

REVOKE ALL ON public.ggman_esc_profiles FROM PUBLIC;
GRANT SELECT, INSERT, UPDATE ON public.ggman_esc_profiles TO anon, authenticated;

CREATE OR REPLACE FUNCTION public.ggman_get_esc_profile()
RETURNS JSONB LANGUAGE SQL STABLE SECURITY INVOKER AS $$
    SELECT COALESCE(
        (SELECT jsonb_build_object('payload', payload, 'updated_at', updated_at)
           FROM public.ggman_esc_profiles WHERE owner_id = auth.uid()::text),
        '{}'::jsonb
    );
$$;

CREATE OR REPLACE FUNCTION public.ggman_set_esc_profile(p_payload JSONB)
RETURNS JSONB LANGUAGE plpgsql VOLATILE SECURITY INVOKER AS $$
DECLARE saved_at TIMESTAMPTZ;
BEGIN
    IF p_payload IS NULL OR octet_length(p_payload::text) > 400000 OR
       p_payload->>'SchemaVersion' <> '1' THEN
        RAISE EXCEPTION 'Invalid ESC profile payload';
    END IF;
    INSERT INTO public.ggman_esc_profiles(owner_id, payload, updated_at)
    VALUES (auth.uid()::text, p_payload, now())
    ON CONFLICT (owner_id) DO UPDATE SET
        payload = EXCLUDED.payload, updated_at = EXCLUDED.updated_at
    RETURNING updated_at INTO saved_at;
    RETURN jsonb_build_object('updated_at', saved_at);
END;
$$;

REVOKE ALL ON FUNCTION public.ggman_get_esc_profile() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.ggman_get_esc_profile() TO anon, authenticated;
REVOKE ALL ON FUNCTION public.ggman_set_esc_profile(JSONB) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.ggman_set_esc_profile(JSONB) TO anon, authenticated;

COMMIT;
