ALTER TABLE du.accounts ADD COLUMN role varchar(16) NOT NULL DEFAULT 'user' CHECK (role IN ('user','admin'));
CREATE TABLE du.catalog_releases (
    id uuid PRIMARY KEY,
    pack_id text NOT NULL,
    version varchar(32) NOT NULL,
    title text NOT NULL,
    artist text NOT NULL,
    revision bigint NOT NULL,
    storage_name text NOT NULL UNIQUE,
    sha256 text NOT NULL CHECK (sha256 ~ '^[0-9a-f]{64}$'),
    byte_size bigint NOT NULL CHECK (byte_size > 0),
    rights text NOT NULL,
    published boolean NOT NULL DEFAULT true,
    published_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE(pack_id,version)
);
CREATE INDEX catalog_published ON du.catalog_releases(pack_id,published_at DESC) WHERE published;
CREATE TABLE du.release_charts (
    release_id uuid NOT NULL REFERENCES du.catalog_releases(id),
    chart_id text NOT NULL,
    digest text NOT NULL,
    ruleset_id text NOT NULL,
    PRIMARY KEY(release_id,chart_id)
);
CREATE TABLE du.admin_audit (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    actor_id uuid NOT NULL REFERENCES du.accounts(id),
    action text NOT NULL,
    target text NOT NULL,
    reason text NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
