CREATE TABLE IF NOT EXISTS buckets (
  bucket_id     text PRIMARY KEY,
  owner_pubkey  text NOT NULL,
  current_seq   bigint NOT NULL DEFAULT 0,
  content_hash  text   NOT NULL DEFAULT '',
  created_at    timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS entries (
  bucket_id     text   NOT NULL REFERENCES buckets(bucket_id),
  key           text   NOT NULL,
  version       bigint NOT NULL,
  seq           bigint NOT NULL,
  content_hash  text   NOT NULL,
  scheme        text   NOT NULL,
  deleted       bool   NOT NULL DEFAULT false,
  value         bytea  NOT NULL,
  updated_at    timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (bucket_id, key)
);
CREATE INDEX IF NOT EXISTS entries_by_seq ON entries (bucket_id, seq);

CREATE TABLE IF NOT EXISTS sessions (
  token_hash  text PRIMARY KEY,
  bucket_id   text NOT NULL,
  device      text,
  created_at  timestamptz NOT NULL DEFAULT now(),
  expires_at  timestamptz NOT NULL,
  last_seen   timestamptz
);

CREATE TABLE IF NOT EXISTS challenges (
  nonce       text PRIMARY KEY,
  pubkey      text NOT NULL,
  issued_at   timestamptz NOT NULL DEFAULT now(),
  expires_at  timestamptz NOT NULL,
  consumed    bool NOT NULL DEFAULT false
);
