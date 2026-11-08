<?php
/**
 * NyotaCore Talent - application bootstrap.
 * Loads config, opens the database, and makes sure the schema is installed
 * and up to date (tables are created and seeded automatically on first run).
 */

if (defined('NCT_BOOT')) { return; }
define('NCT_BOOT', true);
define('NCT_ROOT', dirname(__DIR__));

ini_set('display_errors', '0');
ini_set('log_errors', '1');
error_reporting(E_ALL);

if (!is_file(NCT_ROOT . '/private/config.php')) { http_response_code(500); exit('Missing private/config.php - copy private/config.example.php and fill in your settings.'); }
$GLOBALS['nct_cfg'] = require NCT_ROOT . '/private/config.php';
date_default_timezone_set(cfg('timezone', 'Africa/Lagos'));

require __DIR__ . '/helpers.php';
require __DIR__ . '/migrations.php';

function cfg($key, $default = null)
{
    return array_key_exists($key, $GLOBALS['nct_cfg']) ? $GLOBALS['nct_cfg'][$key] : $default;
}

/** Shared PDO connection. Throws on failure. */
function db()
{
    static $pdo = null;
    if ($pdo === null) {
        $dsn = 'mysql:host=' . cfg('db_host') . ';dbname=' . cfg('db_name') . ';charset=utf8mb4';
        $pdo = new PDO($dsn, cfg('db_user'), cfg('db_pass'), array(
            PDO::ATTR_ERRMODE            => PDO::ERRMODE_EXCEPTION,
            PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
            PDO::ATTR_EMULATE_PREPARES   => false,
        ));
        $pdo->exec("SET time_zone = '" . date('P') . "'");
        nct_migrate($pdo);
    }
    return $pdo;
}

/** Same as db() but returns null instead of throwing, and logs the reason. */
function db_or_null()
{
    try { return db(); }
    catch (Throwable $e) { error_log('NyotaCore Talent DB unavailable: ' . $e->getMessage()); return null; }
}
