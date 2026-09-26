-- GGman anonymous feature-usage telemetry.
-- Stores daily aggregate counters only; no raw account, IP, hardware or credential data.

BEGIN;

CREATE TABLE IF NOT EXISTS public.ggman_telemetry_events (
    owner_id TEXT NOT NULL,
    event_day DATE NOT NULL,
    event_name TEXT NOT NULL,
    event_count INTEGER NOT NULL DEFAULT 0,
    app_version TEXT NOT NULL DEFAULT '',
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (owner_id, event_day, event_name),
    CONSTRAINT ggman_telemetry_events_count_check CHECK (event_count > 0),
    CONSTRAINT ggman_telemetry_events_name_check CHECK (event_name ~ '^[a-z0-9_]{1,64}$')
);

CREATE INDEX IF NOT EXISTS ggman_telemetry_events_day_idx
    ON public.ggman_telemetry_events (event_day);

ALTER TABLE public.ggman_telemetry_events ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS ggman_telemetry_events_select_own ON public.ggman_telemetry_events;
CREATE POLICY ggman_telemetry_events_select_own
    ON public.ggman_telemetry_events
    FOR SELECT
    USING (owner_id = auth.uid()::text);

DROP POLICY IF EXISTS ggman_telemetry_events_insert_own ON public.ggman_telemetry_events;
CREATE POLICY ggman_telemetry_events_insert_own
    ON public.ggman_telemetry_events
    FOR INSERT
    WITH CHECK (owner_id = auth.uid()::text);

DROP POLICY IF EXISTS ggman_telemetry_events_update_own ON public.ggman_telemetry_events;
CREATE POLICY ggman_telemetry_events_update_own
    ON public.ggman_telemetry_events
    FOR UPDATE
    USING (owner_id = auth.uid()::text)
    WITH CHECK (owner_id = auth.uid()::text);

REVOKE ALL ON TABLE public.ggman_telemetry_events FROM PUBLIC;
GRANT SELECT, INSERT, UPDATE ON TABLE public.ggman_telemetry_events TO anon, authenticated;

CREATE OR REPLACE FUNCTION public.ggman_record_usage(
    p_events JSONB,
    p_app_version TEXT
)
RETURNS JSONB
LANGUAGE plpgsql
VOLATILE
SECURITY INVOKER
AS $$
DECLARE
    event_item RECORD;
    parsed_count INTEGER;
    current_day DATE := timezone('utc', now())::date;
BEGIN
    IF jsonb_typeof(p_events) <> 'object' THEN
        RAISE EXCEPTION 'Telemetry payload must be a JSON object.';
    END IF;

    FOR event_item IN
        SELECT key, value
        FROM jsonb_each_text(p_events)
    LOOP
        IF event_item.key !~ '^[a-z0-9_]{1,64}$' THEN
            RAISE EXCEPTION 'Telemetry event name is invalid.';
        END IF;

        BEGIN
            parsed_count := LEAST(1000, GREATEST(1, event_item.value::integer));
        EXCEPTION WHEN invalid_text_representation OR numeric_value_out_of_range THEN
            RAISE EXCEPTION 'Telemetry event count is invalid.';
        END;

        INSERT INTO public.ggman_telemetry_events (
            owner_id,
            event_day,
            event_name,
            event_count,
            app_version,
            updated_at
        )
        VALUES (
            auth.uid()::text,
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
                public.ggman_telemetry_events.event_count + EXCLUDED.event_count
            ),
            app_version = EXCLUDED.app_version,
            updated_at = now();
    END LOOP;

    DELETE FROM public.ggman_telemetry_events
    WHERE owner_id = auth.uid()::text
      AND event_day < current_day - 180;

    RETURN jsonb_build_object('event_day', current_day);
END;
$$;

REVOKE ALL ON FUNCTION public.ggman_record_usage(JSONB, TEXT) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.ggman_record_usage(JSONB, TEXT) TO anon, authenticated;

COMMIT;
