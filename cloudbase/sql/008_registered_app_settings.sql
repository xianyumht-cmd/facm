-- Registered GGman portable app preferences. Create only after catalog preflight.
BEGIN;
CREATE TABLE public.ggman_registered_app_settings (
    owner_id TEXT PRIMARY KEY DEFAULT auth.uid(),
    payload JSONB NOT NULL,
    version BIGINT NOT NULL DEFAULT 1 CHECK (version > 0),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ggman_reg_app_size CHECK (octet_length(payload::text) <= 32768),
    CONSTRAINT ggman_reg_app_format CHECK (
        jsonb_typeof(payload) = 'object' AND
        payload->>'SchemaVersion' = '1' AND
        jsonb_typeof(payload->'Settings') = 'object'
    )
);
ALTER TABLE public.ggman_registered_app_settings ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public.ggman_registered_app_settings FROM PUBLIC, anon;
GRANT SELECT, INSERT, UPDATE ON public.ggman_registered_app_settings TO authenticated;
CREATE POLICY ggman_reg_app_owner ON public.ggman_registered_app_settings
 FOR ALL TO authenticated USING (owner_id=auth.uid()) WITH CHECK (owner_id=auth.uid());

CREATE FUNCTION public.ggman_get_registered_app_settings()
RETURNS JSONB LANGUAGE plpgsql STABLE SECURITY INVOKER AS $$
DECLARE v_owner TEXT := auth.uid(); v_row RECORD;
BEGIN
 IF auth.role() IS DISTINCT FROM 'authenticated' OR v_owner IS NULL OR
    COALESCE(auth.jwt()->>'is_anonymous','false') <> 'false' THEN
   RAISE EXCEPTION 'Registered account required' USING ERRCODE='42501';
 END IF;
 SELECT payload,version,updated_at INTO v_row FROM public.ggman_registered_app_settings WHERE owner_id=v_owner;
 IF NOT FOUND THEN RETURN '{}'::jsonb; END IF;
 RETURN jsonb_build_object('payload',v_row.payload,'version',v_row.version,'updated_at',v_row.updated_at);
END;
$$;
CREATE FUNCTION public.ggman_set_registered_app_settings(p_payload JSONB,p_expected_version BIGINT)
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
    jsonb_typeof(p_payload->'Settings') <> 'object' OR
    octet_length(p_payload::text) > 32768 THEN
   RAISE EXCEPTION 'Invalid app preference payload' USING ERRCODE='22023';
 END IF;
 IF p_expected_version=0 THEN
   INSERT INTO public.ggman_registered_app_settings(owner_id,payload)
     VALUES(v_owner,p_payload) ON CONFLICT(owner_id) DO NOTHING
     RETURNING version INTO v_next;
 ELSE
   UPDATE public.ggman_registered_app_settings
     SET payload=p_payload,version=version+1,updated_at=now()
     WHERE owner_id=v_owner AND version=p_expected_version
     RETURNING version INTO v_next;
 END IF;
 IF v_next IS NULL THEN
   RAISE EXCEPTION 'APP_SETTINGS_VERSION_CONFLICT' USING ERRCODE='40001';
 END IF;
 RETURN jsonb_build_object('version',v_next);
END;
$$;
REVOKE ALL ON FUNCTION public.ggman_get_registered_app_settings() FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.ggman_set_registered_app_settings(JSONB,BIGINT) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.ggman_get_registered_app_settings() TO authenticated;
GRANT EXECUTE ON FUNCTION public.ggman_set_registered_app_settings(JSONB,BIGINT) TO authenticated;
COMMIT;
