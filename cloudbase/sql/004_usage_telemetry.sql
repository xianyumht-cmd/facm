-- GGman anonymous feature-usage telemetry.
-- Stores daily aggregate counters only; no raw account, IP, hardware or credential data.
-- Uses a dedicated table so older event-log schemas remain untouched.

BEGIN;

CREATE TABLE IF NOT EXISTS public.ggman_usage_daily (
    owner_id TEXT NOT NULL,
    event_day DATE NOT NULL,
    event_name TEXT NOT NULL,
    event_count INTEGER NOT NULL DEFAULT 0,
    app_version TEXT NOT NULL DEFAULT '',
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (owner_id, event_day, event_name),
    CONSTRAINT ggman_usage_daily_count_check CHECK (event_count > 0),
    CONSTRAINT ggman_usage_daily_name_check CHECK (event_name ~ '^[a-z0-9_]{1,64}$')
);

CREATE INDEX IF NOT EXISTS ggman_usage_daily_day_idx
    ON public.ggman_usage_daily (event_day);

ALTER TABLE public.ggman_usage_daily ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS ggman_usage_daily_select_own ON public.ggman_usage_daily;
CREATE POLICY ggman_usage_daily_select_own
    ON public.ggman_usage_daily
    FOR SELECT
    USING (owner_id = auth.uid()::text);

REVOKE ALL ON TABLE public.ggman_usage_daily FROM PUBLIC;
GRANT SELECT ON TABLE public.ggman_usage_daily TO anon, authenticated;

CREATE OR REPLACE FUNCTION public.ggman_record_usage(
    p_events JSONB,
    p_app_version TEXT
)
RETURNS JSONB
LANGUAGE plpgsql
VOLATILE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
    event_item RECORD;
    parsed_count INTEGER;
    current_day DATE := timezone('utc', now())::date;
    current_owner TEXT := auth.uid()::text;
BEGIN
    IF current_owner IS NULL OR length(current_owner) = 0 THEN
        RAISE EXCEPTION 'authentication required';
    END IF;

    IF jsonb_typeof(p_events) <> 'object' THEN
        RAISE EXCEPTION 'Telemetry payload must be a JSON object.';
    END IF;

    FOR event_item IN
        SELECT key, value
        FROM jsonb_each_text(p_events)
    LOOP
        IF event_item.key NOT IN (
            'app_launch',
            'league_dashboard_open',
            'league_player_open',
            'league_live_open',
            'mayhem_lookup_open',
            'personal_stats_open',
            'opgg_advisor_open',
            'efficiency_open',
            'game_repair_open',
            'presence_open',
            'champ_select_companion_open'
        ) THEN
            RAISE EXCEPTION 'Telemetry event name is not allowed.';
        END IF;

        BEGIN
            parsed_count := LEAST(1000, GREATEST(1, event_item.value::integer));
        EXCEPTION WHEN invalid_text_representation OR numeric_value_out_of_range THEN
            RAISE EXCEPTION 'Telemetry event count is invalid.';
        END;

        INSERT INTO public.ggman_usage_daily (
            owner_id,
            event_day,
            event_name,
            event_count,
            app_version,
            updated_at
        )
        VALUES (
            current_owner,
            current_day,
            event_item.key,
            parsed_count,
            left(COALESCE(p_app_version, ''), 32),
            now()
        )
        ON CONFLICT (owner_id, event_day, event_name)
        DO UPDATE SET
            event_count = LEAST(
                1000000,
                public.ggman_usage_daily.event_count + EXCLUDED.event_count
            ),
            app_version = EXCLUDED.app_version,
            updated_at = now();
    END LOOP;

    DELETE FROM public.ggman_usage_daily
    WHERE owner_id = current_owner
      AND event_day < current_day - 180;

    RETURN jsonb_build_object('event_day', current_day);
END;
$$;

REVOKE ALL ON FUNCTION public.ggman_record_usage(JSONB, TEXT) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.ggman_record_usage(JSONB, TEXT) TO anon, authenticated;

COMMIT;
