-- Independent laboratory data. Never import production CloudBase users automatically.
CREATE TABLE IF NOT EXISTS sync_documents (
  owner_id TEXT NOT NULL CHECK(length(owner_id) BETWEEN 6 AND 256),
  kind TEXT NOT NULL CHECK(kind IN ('app_settings','ui_text','esc_profile','game_profile')),
  version INTEGER NOT NULL CHECK(version > 0),
  payload_json TEXT NOT NULL,
  content_hash TEXT NOT NULL CHECK(length(content_hash) = 64),
  updated_at TEXT NOT NULL,
  PRIMARY KEY (owner_id, kind)
);
CREATE INDEX IF NOT EXISTS sync_documents_updated_at_idx ON sync_documents(updated_at);
