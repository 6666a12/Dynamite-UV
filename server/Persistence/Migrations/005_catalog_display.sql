ALTER TABLE du.catalog_releases ADD COLUMN display_metadata jsonb;
-- Existing releases remain without a declared media profile until explicitly revalidated.
