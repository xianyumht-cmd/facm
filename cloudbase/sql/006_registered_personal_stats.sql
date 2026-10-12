-- GGman registered personal statistics. Apply once after inspecting the production schema.
-- Existing ggman_devices, ggman_account_history, ggman_settings_sync and telemetry stay intact.
BEGIN;

CREATE TABLE public.ggman_registered_profiles (
    owner_id TEXT PRIMARY KEY DEFAULT auth.uid(),
    ranking_visible BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE TABLE public.ggman_registered_account_history (
    owner_id TEXT NOT NULL DEFAULT auth.uid(),
    account_key_hash TEXT NOT NULL CHECK (account_key_hash ~ '^[0-9a-f]{64}$'),
    first_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_seen_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (owner_id, account_key_hash)
);
CREATE TABLE public.ggman_registered_active_days (
    owner_id TEXT NOT NULL DEFAULT auth.uid(),
    event_day DATE NOT NULL,
    PRIMARY KEY (owner_id, event_day)
);
CREATE TABLE public.ggman_registered_legacy_imports (
    owner_id TEXT NOT NULL DEFAULT auth.uid(),
    source_key TEXT NOT NULL CHECK (source_key ~ '^[0-9a-f]{64}$'),
    legacy_account_count INTEGER NOT NULL CHECK (legacy_account_count BETWEEN 0 AND 500),
    imported_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (owner_id, source_key)
);

ALTER TABLE public.ggman_registered_profiles ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.ggman_registered_account_history ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.ggman_registered_active_days ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.ggman_registered_legacy_imports ENABLE ROW LEVEL SECURITY;

REVOKE ALL ON public.ggman_registered_profiles, public.ggman_registered_account_history,
    public.ggman_registered_active_days, public.ggman_registered_legacy_imports FROM PUBLIC, anon;
GRANT SELECT, INSERT, UPDATE ON public.ggman_registered_profiles TO authenticated;
GRANT SELECT, INSERT, UPDATE ON public.ggman_registered_account_history TO authenticated;
GRANT SELECT, INSERT ON public.ggman_registered_active_days TO authenticated;
GRANT SELECT, INSERT, UPDATE ON public.ggman_registered_legacy_imports TO authenticated;

CREATE POLICY ggman_registered_profiles_own ON public.ggman_registered_profiles
    FOR ALL TO authenticated USING (owner_id = auth.uid()) WITH CHECK (owner_id = auth.uid());
CREATE POLICY ggman_registered_account_history_own ON public.ggman_registered_account_history
    FOR ALL TO authenticated USING (owner_id = auth.uid()) WITH CHECK (owner_id = auth.uid());
CREATE POLICY ggman_registered_active_days_own ON public.ggman_registered_active_days
    FOR ALL TO authenticated USING (owner_id = auth.uid()) WITH CHECK (owner_id = auth.uid());
CREATE POLICY ggman_registered_legacy_imports_own ON public.ggman_registered_legacy_imports
    FOR ALL TO authenticated USING (owner_id = auth.uid()) WITH CHECK (owner_id = auth.uid());

CREATE OR REPLACE FUNCTION public.ggman_registered_touch(p_ranking_visible BOOLEAN DEFAULT NULL)
RETURNS JSONB LANGUAGE plpgsql VOLATILE SECURITY INVOKER AS $$
DECLARE v_owner TEXT := auth.uid();
DECLARE v_visible BOOLEAN;
BEGIN
    IF auth.role() IS DISTINCT FROM 'authenticated' OR v_owner IS NULL OR
       COALESCE(auth.jwt()->>'is_anonymous', 'false') <> 'false' THEN
        RAISE EXCEPTION 'Registered account required' USING ERRCODE='42501';
    END IF;
    INSERT INTO public.ggman_registered_profiles(owner_id, ranking_visible)
    VALUES (v_owner, COALESCE(p_ranking_visible, TRUE))
    ON CONFLICT (owner_id) DO UPDATE SET
        ranking_visible = CASE WHEN p_ranking_visible IS NULL THEN public.ggman_registered_profiles.ranking_visible
                               ELSE EXCLUDED.ranking_visible END,
        updated_at = now()
    RETURNING ranking_visible INTO v_visible;

    INSERT INTO public.ggman_registered_active_days(owner_id,event_day)
    VALUES (v_owner,(now() AT TIME ZONE 'UTC')::date)
    ON CONFLICT DO NOTHING;
    RETURN jsonb_build_object('ranking_visible',v_visible);
END;
$$;

CREATE OR REPLACE FUNCTION public.ggman_registered_record_account(p_account_key_hash TEXT)
RETURNS JSONB LANGUAGE plpgsql VOLATILE SECURITY INVOKER AS $$
DECLARE v_owner TEXT := auth.uid();
BEGIN
    IF auth.role() IS DISTINCT FROM 'authenticated' OR v_owner IS NULL OR
       COALESCE(auth.jwt()->>'is_anonymous', 'false') <> 'false' THEN
        RAISE EXCEPTION 'Registered account required' USING ERRCODE='42501';
    END IF;
    IF p_account_key_hash IS NULL OR p_account_key_hash !~ '^[0-9a-f]{64}$' THEN
        RAISE EXCEPTION 'Invalid account key hash';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public.ggman_registered_profiles WHERE owner_id = v_owner) THEN
        RAISE EXCEPTION 'Registered profile has not been initialized';
    END IF;
    IF (SELECT COUNT(*) FROM public.ggman_registered_account_history WHERE owner_id = v_owner) >= 500
       AND NOT EXISTS (SELECT 1 FROM public.ggman_registered_account_history
                       WHERE owner_id = v_owner AND account_key_hash = p_account_key_hash) THEN
        RAISE EXCEPTION 'Account record limit reached';
    END IF;
    INSERT INTO public.ggman_registered_account_history(owner_id,account_key_hash)
    VALUES (v_owner,p_account_key_hash)
    ON CONFLICT (owner_id,account_key_hash) DO UPDATE SET last_seen_at=now();
    INSERT INTO public.ggman_registered_active_days(owner_id,event_day)
    VALUES (v_owner,(now() AT TIME ZONE 'UTC')::date)
    ON CONFLICT DO NOTHING;
    RETURN jsonb_build_object('recorded',true);
END;
$$;

CREATE OR REPLACE FUNCTION public.ggman_registered_import_legacy(
    p_source_key TEXT, p_legacy_account_count INTEGER, p_active_days JSONB)
RETURNS JSONB LANGUAGE plpgsql VOLATILE SECURITY INVOKER AS $$
DECLARE v_owner TEXT := auth.uid();
DECLARE v_day TEXT;
BEGIN
    IF auth.role() IS DISTINCT FROM 'authenticated' OR v_owner IS NULL OR
       COALESCE(auth.jwt()->>'is_anonymous', 'false') <> 'false' THEN
        RAISE EXCEPTION 'Registered account required' USING ERRCODE='42501';
    END IF;
    IF p_source_key IS NULL OR p_source_key !~ '^[0-9a-f]{64}$' OR
       p_legacy_account_count IS NULL OR p_legacy_account_count NOT BETWEEN 0 AND 500 OR
       p_active_days IS NULL OR jsonb_typeof(p_active_days) <> 'array' OR
       jsonb_array_length(p_active_days) > 2000 THEN
        RAISE EXCEPTION 'Invalid legacy import';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public.ggman_registered_profiles WHERE owner_id = v_owner) THEN
        RAISE EXCEPTION 'Registered profile has not been initialized';
    END IF;
    INSERT INTO public.ggman_registered_legacy_imports(owner_id,source_key,legacy_account_count)
    VALUES (v_owner,p_source_key,p_legacy_account_count)
    ON CONFLICT (owner_id,source_key) DO UPDATE
      SET legacy_account_count=GREATEST(public.ggman_registered_legacy_imports.legacy_account_count,
                                       EXCLUDED.legacy_account_count),
          imported_at=now();

    FOR v_day IN SELECT jsonb_array_elements_text(p_active_days)
    LOOP
        IF v_day !~ '^20[0-9]{2}-[0-9]{2}-[0-9]{2}$' OR
           v_day::date > (now() AT TIME ZONE 'UTC')::date THEN
            RAISE EXCEPTION 'Invalid activity date';
        END IF;
        INSERT INTO public.ggman_registered_active_days(owner_id,event_day)
        VALUES (v_owner,v_day::date)
        ON CONFLICT DO NOTHING;
    END LOOP;
    RETURN jsonb_build_object('imported',true);
END;
$$;

-- Aggregate ranking is the only SECURITY DEFINER endpoint; never return other owners' rows.
CREATE OR REPLACE FUNCTION public.ggman_get_registered_personal_stats()
RETURNS JSONB LANGUAGE plpgsql STABLE SECURITY DEFINER
SET search_path=public,pg_temp AS $$
DECLARE v_owner TEXT := auth.uid();
DECLARE v_visible BOOLEAN;
DECLARE v_score BIGINT;
DECLARE v_population BIGINT;
DECLARE v_ahead BIGINT;
DECLARE v_behind BIGINT;
DECLARE v_days BIGINT;
BEGIN
    IF auth.role() IS DISTINCT FROM 'authenticated' OR v_owner IS NULL OR
       COALESCE(auth.jwt()->>'is_anonymous', 'false') <> 'false' THEN
        RAISE EXCEPTION 'Registered account required' USING ERRCODE='42501';
    END IF;
    SELECT ranking_visible INTO v_visible FROM public.ggman_registered_profiles WHERE owner_id=v_owner;
    IF v_visible IS NULL THEN RETURN '{}'::jsonb; END IF;

    WITH scores AS (
        SELECT p.owner_id, p.ranking_visible,
               GREATEST(
                   (SELECT COUNT(*) FROM public.ggman_registered_account_history h WHERE h.owner_id=p.owner_id),
                   COALESCE((SELECT MAX(i.legacy_account_count) FROM public.ggman_registered_legacy_imports i
                             WHERE i.owner_id=p.owner_id),0)
               )::BIGINT AS score
        FROM public.ggman_registered_profiles p
    )
    SELECT mine.score,
           (SELECT COUNT(*) FROM scores WHERE ranking_visible=true),
           (SELECT COUNT(*) FROM scores WHERE ranking_visible=true AND score>mine.score),
           (SELECT COUNT(*) FROM scores WHERE ranking_visible=true AND score<mine.score)
    INTO v_score,v_population,v_ahead,v_behind
    FROM scores mine WHERE mine.owner_id=v_owner;
    SELECT COUNT(*) INTO v_days FROM public.ggman_registered_active_days WHERE owner_id=v_owner;
    RETURN jsonb_build_object(
        'played_accounts',v_score, 'active_days',v_days, 'ranking_visible',v_visible,
        'rank',CASE WHEN v_visible THEN v_ahead + 1 ELSE 0 END,
        'total_ranked_users',CASE WHEN v_visible THEN v_population ELSE 0 END,
        'percentile',CASE WHEN v_visible AND v_population>0
                     THEN ROUND(v_behind::numeric*100/v_population,1) ELSE 0 END,
        'legacy_count_is_lower_bound',true
    );
END;
$$;

REVOKE ALL ON FUNCTION public.ggman_registered_touch(BOOLEAN) FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.ggman_registered_record_account(TEXT) FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.ggman_registered_import_legacy(TEXT,INTEGER,JSONB) FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.ggman_get_registered_personal_stats() FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.ggman_registered_touch(BOOLEAN) TO authenticated;
GRANT EXECUTE ON FUNCTION public.ggman_registered_record_account(TEXT) TO authenticated;
GRANT EXECUTE ON FUNCTION public.ggman_registered_import_legacy(TEXT,INTEGER,JSONB) TO authenticated;
GRANT EXECUTE ON FUNCTION public.ggman_get_registered_personal_stats() TO authenticated;

COMMIT;
