-- GGman ESC snapshots: registered CloudBase accounts only.
-- Do not deploy until production auth/role and RLS tests pass.
-- This migration replaces the UNUSED anonymous-device draft 005, not an installed schema.
BEGIN;

CREATE TABLE IF NOT EXISTS public.ggman_esc_profiles (
    owner_id TEXT PRIMARY KEY DEFAULT auth.uid(),
    version BIGINT NOT NULL DEFAULT 1 CHECK (version > 0),
    payload JSONB NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ggman_esc_profiles_payload_size CHECK (octet_length(payload::text) <= 400000)
);

ALTER TABLE public.ggman_esc_profiles ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.ggman_esc_profiles FROM PUBLIC, anon;
GRANT SELECT, INSERT, UPDATE ON public.ggman_esc_profiles TO authenticated;

DROP POLICY IF EXISTS ggman_esc_profiles_select_own ON public.ggman_esc_profiles;
CREATE POLICY ggman_esc_profiles_select_own ON public.ggman_esc_profiles
    FOR SELECT TO authenticated USING (owner_id = auth.uid());

DROP POLICY IF EXISTS ggman_esc_profiles_insert_own ON public.ggman_esc_profiles;
CREATE POLICY ggman_esc_profiles_insert_own ON public.ggman_esc_profiles
    FOR INSERT TO authenticated WITH CHECK (owner_id = auth.uid());

DROP POLICY IF EXISTS ggman_esc_profiles_update_own ON public.ggman_esc_profiles;
CREATE POLICY ggman_esc_profiles_update_own ON public.ggman_esc_profiles
    FOR UPDATE TO authenticated USING (owner_id = auth.uid())
    WITH CHECK (owner_id = auth.uid());

CREATE OR REPLACE FUNCTION public.ggman_get_esc_profile()
RETURNS JSONB LANGUAGE plpgsql STABLE SECURITY INVOKER AS $$
DECLARE result JSONB;
BEGIN
    IF auth.role() IS DISTINCT FROM 'authenticated' OR auth.uid() IS NULL OR
       COALESCE(auth.jwt()->>'is_anonymous', 'false') <> 'false' THEN
        RAISE EXCEPTION 'Registered GGman account required' USING ERRCODE = '42501';
    END IF;
    SELECT jsonb_build_object(
        'version', version, 'payload', payload, 'updated_at', updated_at)
    INTO result FROM public.ggman_esc_profiles WHERE owner_id = auth.uid();
    RETURN COALESCE(result, '{}'::jsonb);
END;
$$;

CREATE OR REPLACE FUNCTION public.ggman_set_esc_profile(
    p_payload JSONB, p_expected_version BIGINT)
RETURNS JSONB LANGUAGE plpgsql VOLATILE SECURITY INVOKER AS $$
DECLARE saved_version BIGINT;
DECLARE saved_at TIMESTAMPTZ;
BEGIN
    IF auth.role() IS DISTINCT FROM 'authenticated' OR auth.uid() IS NULL OR
       COALESCE(auth.jwt()->>'is_anonymous', 'false') <> 'false' THEN
        RAISE EXCEPTION 'Registered GGman account required' USING ERRCODE = '42501';
    END IF;
    IF p_expected_version IS NULL OR p_expected_version < 0 OR
       p_expected_version >= 9223372036854775806 THEN
        RAISE EXCEPTION 'Invalid ESC profile revision';
    END IF;
    IF p_payload IS NULL OR jsonb_typeof(p_payload) <> 'object' OR
       octet_length(p_payload::text) > 400000 OR
       p_payload->>'SchemaVersion' <> '1' THEN
        RAISE EXCEPTION 'Invalid ESC profile payload';
    END IF;
    IF jsonb_typeof(p_payload->'Files') IS DISTINCT FROM 'array' THEN
        RAISE EXCEPTION 'Invalid ESC file manifest';
    END IF;
    IF jsonb_array_length(p_payload->'Files') NOT BETWEEN 1 AND 3 THEN
        RAISE EXCEPTION 'Invalid ESC file count';
    END IF;

    IF p_expected_version = 0 THEN
        INSERT INTO public.ggman_esc_profiles(owner_id, version, payload, updated_at)
        VALUES (auth.uid(), 1, p_payload, now())
        ON CONFLICT (owner_id) DO NOTHING
        RETURNING version, updated_at INTO saved_version, saved_at;
    ELSE
        UPDATE public.ggman_esc_profiles SET
            version = version + 1, payload = p_payload, updated_at = now()
        WHERE owner_id = auth.uid() AND version = p_expected_version
        RETURNING version, updated_at INTO saved_version, saved_at;
    END IF;
    IF saved_version IS NULL THEN
        RAISE EXCEPTION 'ESC_VERSION_CONFLICT' USING ERRCODE = '40001';
    END IF;
    RETURN jsonb_build_object('version', saved_version, 'updated_at', saved_at);
END;
$$;

REVOKE ALL ON FUNCTION public.ggman_get_esc_profile() FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.ggman_set_esc_profile(JSONB, BIGINT) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.ggman_get_esc_profile() TO authenticated;
GRANT EXECUTE ON FUNCTION public.ggman_set_esc_profile(JSONB, BIGINT) TO authenticated;

COMMIT;
