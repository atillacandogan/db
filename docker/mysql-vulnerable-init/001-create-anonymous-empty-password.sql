-- Intentionally vulnerable test fixture.
-- MySQL executes files in /docker-entrypoint-initdb.d on first initialization.
CREATE USER IF NOT EXISTS ''@'%'
    IDENTIFIED WITH caching_sha2_password BY '';

GRANT SELECT ON demo.* TO ''@'%';
