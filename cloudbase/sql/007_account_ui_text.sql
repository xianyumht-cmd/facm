-- GGman email-account UI wording overrides. Create once, after inspecting existing schema.
BEGIN;

CREATE TABLE public.ggman_ui_text_profiles (
    owner_id TEXT PRIMARY KEY DEFAULT auth.uid(),
    payload JSONB NOT NULL,
    version BIGINT NOT NULL DEFAULT 1 CHECK (version > 0),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ggman_ui_text_payload_size CHECK (octet_length(payload::text) <= 184320),
    CONSTRAINT ggman_ui_text_payload_format CHECK (
        jsonb_typeof(payload) = 'object' AND
        payload->>'SchemaVersion' = '1' AND
        jsonb_typeof(payload->'Text') = 'object' AND
        jsonb_typeof(payload->'Replace') = 'object'
    )
);

ALTER TABLE public.ggman_ui_text_profiles ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.ggman_ui_text_profiles FROM PUBLIC, anon;
GRANT SELECT, INSERT, UPDATE ON public.ggman_ui_text_profiles TO authenticated;
CREATE POLICY ggman_ui_text_profile_owner ON public.ggman_ui_text_profiles
    FOR ALL TO authenticated USING (owner_id = auth.uid()) WITH CHECK (owner_id = auth.uid());

CREATE OR REPLACE FUNCTION public.ggman_get_ui_text_profile()
RETURNS JSONB LANGUAGE plpgsql STABLE SECURITY INVOKER AS $$
DECLARE v_owner TEXT := auth.uid();
DECLARE row_data RECORD;
BEGIN
    IF auth.role() IS DISTINCT FROM 'authenticated' OR v_owner IS NULL OR
       COALESCE(auth.jwt()->>'is_anonymous','false') <> 'false' THEN
        RAISE EXCEPTION 'Registered account required' USING ERRCODE='42501';
    END IF;
    SELECT payload, version, updated_at INTO row_data
        FROM public.ggman_ui_text_profiles WHERE owner_id = v_owner;
    IF NOT FOUND THEN RETURN '{}'::jsonb; END IF;
    RETURN jsonb_build_object('payload',row_data.payload,'version',row_data.version,
                              'updated_at',row_data.updated_at);
END;
$$;

CREATE OR REPLACE FUNCTION public.ggman_set_ui_text_profile(
    p_payload JSONB, p_expected_version BIGINT
) RETURNS JSONB LANGUAGE plpgsql VOLATILE SECURITY INVOKER AS $$
DECLARE v_owner TEXT := auth.uid();
DECLARE v_old BIGINT;
DECLARE v_new BIGINT;
BEGIN
    IF auth.role() IS DISTINCT FROM 'authenticated' OR v_owner IS NULL OR
       COALESCE(auth.jwt()->>'is_anonymous','false') <> 'false' THEN
        RAISE EXCEPTION 'Registered account required' USING ERRCODE='42501';
    END IF;
    IF p_expected_version IS NULL OR p_expected_version < 0 OR
       p_payload IS NULL OR jsonb_typeof(p_payload) <> 'object' OR
       p_payload->>'SchemaVersion' <> '1' OR
       jsonb_typeof(p_payload->'Text') <> 'object' OR
       jsonb_typeof(p_payload->'Replace') <> 'object' OR
       octet_length(p_payload::text) > 184320 OR
       (SELECT count(*) FROM jsonb_each(p_payload->'Text')) > 1000 OR
       (SELECT count(*) FROM jsonb_each(p_payload->'Replace')) > 80
    THEN
        RAISE EXCEPTION 'Invalid UI text payload' USING ERRCODE='22023';
    END IF;
    SELECT version INTO v_old FROM public.ggman_ui_text_profiles
        WHERE owner_id=v_owner FOR UPDATE;
    IF NOT FOUND THEN
        IF p_expected_version <> 0 THEN
            RAISE EXCEPTION 'UI_TEXT_VERSION_CONFLICT' USING ERRCODE='40001';
        END IF;
        INSERT INTO public.ggman_ui_text_profiles(owner_id,payload)
            VALUES(v_owner,p_payload) RETURNING version INTO v_new;
    ELSE
        IF v_old <> p_expected_version THEN
            RAISE EXCEPTION 'UI_TEXT_VERSION_CONFLICT' USING ERRCODE='40001';
        END IF;
        UPDATE public.ggman_ui_text_profiles
            SET payload=p_payload, version=version+1, updated_at=now()
            WHERE owner_id=v_owner RETURNING version INTO v_new;
    END IF;
    RETURN jsonb_build_object('version',v_new);
END;
$$;

REVOKE ALL ON FUNCTION public.ggman_get_ui_text_profile() FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.ggman_set_ui_text_profile(JSONB,BIGINT) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.ggman_get_ui_text_profile() TO authenticated;
GRANT EXECUTE ON FUNCTION public.ggman_set_ui_text_profile(JSONB,BIGINT) TO authenticated;

COMMIT;
