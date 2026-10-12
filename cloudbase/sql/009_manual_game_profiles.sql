-- Account-owned game presets: only an explicit client publish may invoke set.
-- Create-once migration: do not execute on an existing table.
BEGIN;

CREATE TABLE public.ggman_game_profiles (
    owner_id TEXT PRIMARY KEY DEFAULT auth.uid(),
    version BIGINT NOT NULL DEFAULT 1 CHECK (version > 0),
    payload JSONB NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ggman_game_profile_size CHECK (octet_length(payload::text) <= 510000),
    CONSTRAINT ggman_game_profile_structure CHECK (
        jsonb_typeof(payload) = 'object' AND
        payload->>'SchemaVersion' = '1' AND
        jsonb_typeof(payload->'Esc') = 'object' AND
        jsonb_typeof(payload->'Lobby') = 'array')
);
ALTER TABLE public.ggman_game_profiles ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.ggman_game_profiles FROM PUBLIC, anon;
GRANT SELECT, INSERT, UPDATE ON public.ggman_game_profiles TO authenticated;
CREATE POLICY ggman_game_profile_own ON public.ggman_game_profiles
    FOR ALL TO authenticated USING (owner_id = auth.uid())
    WITH CHECK (owner_id = auth.uid());

CREATE FUNCTION public.ggman_get_game_profile()
RETURNS JSONB LANGUAGE plpgsql STABLE SECURITY INVOKER AS $$
DECLARE v_owner TEXT := auth.uid(); v_row RECORD;
BEGIN
    IF auth.role() IS DISTINCT FROM 'authenticated' OR v_owner IS NULL OR
        COALESCE(auth.jwt()->>'is_anonymous','false') <> 'false' THEN
        RAISE EXCEPTION 'Registered account required' USING ERRCODE='42501';
    END IF;
    SELECT payload, version, updated_at INTO v_row
        FROM public.ggman_game_profiles WHERE owner_id = v_owner;
    IF NOT FOUND THEN RETURN '{}'::jsonb; END IF;
    RETURN jsonb_build_object('payload', v_row.payload, 'version', v_row.version,
        'updated_at', v_row.updated_at);
END;
$$;

CREATE FUNCTION public.ggman_set_game_profile(p_payload JSONB, p_expected_version BIGINT)
RETURNS JSONB LANGUAGE plpgsql VOLATILE SECURITY INVOKER AS $$
DECLARE v_owner TEXT := auth.uid(); v_next BIGINT;
BEGIN
    IF auth.role() IS DISTINCT FROM 'authenticated' OR v_owner IS NULL OR
        COALESCE(auth.jwt()->>'is_anonymous','false') <> 'false' THEN
        RAISE EXCEPTION 'Registered account required' USING ERRCODE='42501';
    END IF;
    IF p_expected_version IS NULL OR p_expected_version < 0 OR
        p_expected_version >= 9223372036854775806 OR
        p_payload IS NULL OR jsonb_typeof(p_payload) <> 'object' OR
        p_payload->>'SchemaVersion' <> '1' OR
        jsonb_typeof(p_payload->'Esc') <> 'object' OR
        jsonb_typeof(p_payload->'Lobby') <> 'array' OR
        jsonb_array_length(p_payload->'Lobby') NOT BETWEEN 1 AND 9 OR
        octet_length(p_payload::text) > 510000 THEN
        RAISE EXCEPTION 'Invalid game profile payload' USING ERRCODE='22023';
    END IF;
    IF p_expected_version = 0 THEN
        INSERT INTO public.ggman_game_profiles(owner_id, payload)
            VALUES(v_owner, p_payload)
            ON CONFLICT(owner_id) DO NOTHING RETURNING version INTO v_next;
    ELSE
        UPDATE public.ggman_game_profiles SET payload=p_payload, version=version+1,
            updated_at=now() WHERE owner_id=v_owner AND version=p_expected_version
            RETURNING version INTO v_next;
    END IF;
    IF v_next IS NULL THEN
        RAISE EXCEPTION 'GAME_PROFILE_VERSION_CONFLICT' USING ERRCODE='40001';
    END IF;
    RETURN jsonb_build_object('version', v_next);
END;
$$;
REVOKE ALL ON FUNCTION public.ggman_get_game_profile() FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.ggman_set_game_profile(JSONB, BIGINT) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.ggman_get_game_profile() TO authenticated;
GRANT EXECUTE ON FUNCTION public.ggman_set_game_profile(JSONB, BIGINT) TO authenticated;
COMMIT;
