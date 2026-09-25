-- GGman personal stats / anonymous ranking migration.
-- Run in the CloudBase PostgreSQL SQL editor after the P1 schema is present.
-- Idempotent: safe to execute again.

BEGIN;

ALTER TABLE public.ggman_devices
    ADD COLUMN IF NOT EXISTS ranking_opt_in BOOLEAN NOT NULL DEFAULT FALSE;

CREATE INDEX IF NOT EXISTS idx_ggman_devices_ranking_opt_in
    ON public.ggman_devices (ranking_opt_in)
    WHERE ranking_opt_in = TRUE;

-- Remove the early four-argument draft signature if this migration was tested manually.
DROP FUNCTION IF EXISTS public.ggman_record_account(TEXT, TEXT, TEXT, TEXT);

CREATE OR REPLACE FUNCTION public.ggman_record_account(
    p_account_key_hash TEXT,
    p_region TEXT DEFAULT NULL,
    p_first_seen_at TEXT DEFAULT NULL,
    p_last_seen_at TEXT DEFAULT NULL,
    p_seen_count INTEGER DEFAULT 1
)
RETURNS VOID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    v_owner TEXT := auth.uid();
    v_first TIMESTAMPTZ;
    v_last TIMESTAMPTZ;
BEGIN
    IF v_owner IS NULL OR length(v_owner) = 0 THEN
        RAISE EXCEPTION 'authentication required';
    END IF;

    IF p_account_key_hash IS NULL OR p_account_key_hash !~ '^[0-9a-fA-F]{64}$' THEN
        RAISE EXCEPTION 'invalid account hash';
    END IF;

    BEGIN
        v_first := NULLIF(p_first_seen_at, '')::timestamptz;
    EXCEPTION WHEN others THEN
        v_first := NULL;
    END;

    BEGIN
        v_last := NULLIF(p_last_seen_at, '')::timestamptz;
    EXCEPTION WHEN others THEN
        v_last := NULL;
    END;

    v_first := COALESCE(v_first, now());
    v_last := COALESCE(v_last, v_first);

    INSERT INTO public.ggman_account_history (
        owner_id,
        account_key_hash,
        region,
        first_seen_at,
        last_seen_at,
        login_count
    )
    VALUES (
        v_owner,
        lower(p_account_key_hash),
        NULLIF(left(COALESCE(p_region, ''), 32), ''),
        v_first,
        v_last,
        GREATEST(COALESCE(p_seen_count, 1), 1)
    )
    ON CONFLICT (owner_id, account_key_hash)
    DO UPDATE SET
        region = COALESCE(EXCLUDED.region, public.ggman_account_history.region),
        first_seen_at = LEAST(public.ggman_account_history.first_seen_at, EXCLUDED.first_seen_at),
        last_seen_at = GREATEST(public.ggman_account_history.last_seen_at, EXCLUDED.last_seen_at),
        login_count = GREATEST(public.ggman_account_history.login_count, EXCLUDED.login_count);
END;
$$;

CREATE OR REPLACE FUNCTION public.ggman_get_personal_stats()
RETURNS TABLE (
    played_accounts BIGINT,
    rank BIGINT,
    total_ranked_users BIGINT,
    percentile DOUBLE PRECISION
)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
    WITH current_owner AS (
        SELECT auth.uid() AS owner_id
    ),
    eligible AS (
        SELECT d.owner_id
        FROM public.ggman_devices d
        JOIN current_owner c ON TRUE
        WHERE d.ranking_opt_in = TRUE
          AND c.owner_id IS NOT NULL
    ),
    counts AS (
        SELECT
            e.owner_id,
            COUNT(h.account_key_hash)::BIGINT AS played_accounts
        FROM eligible e
        LEFT JOIN public.ggman_account_history h
          ON h.owner_id = e.owner_id
        GROUP BY e.owner_id
    ),
    mine AS (
        SELECT counts.played_accounts
        FROM counts
        JOIN current_owner c
          ON counts.owner_id = c.owner_id
    ),
    population AS (
        SELECT
            COUNT(*)::BIGINT AS total_users,
            COUNT(*) FILTER (
                WHERE counts.played_accounts > (SELECT mine.played_accounts FROM mine)
            )::BIGINT AS users_ahead,
            COUNT(*) FILTER (
                WHERE counts.played_accounts < (SELECT mine.played_accounts FROM mine)
            )::BIGINT AS users_behind
        FROM counts
    )
    SELECT
        mine.played_accounts,
        (population.users_ahead + 1)::BIGINT AS rank,
        population.total_users AS total_ranked_users,
        CASE
            WHEN population.total_users <= 0 THEN 0::DOUBLE PRECISION
            ELSE ROUND(
                (population.users_behind::NUMERIC * 100.0) /
                population.total_users::NUMERIC,
                1
            )::DOUBLE PRECISION
        END AS percentile
    FROM mine
    CROSS JOIN population;
$$;

REVOKE ALL ON FUNCTION public.ggman_record_account(TEXT, TEXT, TEXT, TEXT, INTEGER) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.ggman_record_account(TEXT, TEXT, TEXT, TEXT, INTEGER)
    TO anon, authenticated;

REVOKE ALL ON FUNCTION public.ggman_get_personal_stats() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.ggman_get_personal_stats()
    TO anon, authenticated;

COMMIT;
