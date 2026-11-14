CREATE TABLE du.plays (
    account_id uuid NOT NULL REFERENCES du.accounts(id),
    play_id uuid NOT NULL,
    release_id uuid NOT NULL,
    chart_id text NOT NULL,
    payload jsonb NOT NULL,
    submitted_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY(account_id,play_id),
    FOREIGN KEY(release_id,chart_id) REFERENCES du.release_charts(release_id,chart_id)
);
