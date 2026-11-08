<?php
/** NyotaCore Talent - admin sign-in, sessions and permissions. */

require_once __DIR__ . '/bootstrap.php';
require_once __DIR__ . '/leads.php';

const NCT_IDLE_SECONDS   = 7200;  // sign out after 2 hours idle
const NCT_LOGIN_MAX_FAIL = 5;     // failed sign-ins per IP ...
const NCT_LOGIN_WINDOW   = 15;    // ... within this many minutes

start_admin_session();
header('X-Robots-Tag: noindex, nofollow');
header('Cache-Control: no-store');
header('X-Frame-Options: DENY');

function admin_count(PDO $pdo) { return (int)$pdo->query('SELECT COUNT(*) FROM users')->fetchColumn(); }

function current_user()
{
    static $user = false;
    if ($user !== false) { return $user; }
    $user = null;
    if (empty($_SESSION['uid'])) { return null; }
    if (time() - ($_SESSION['seen'] ?? 0) > NCT_IDLE_SECONDS) { session_unset(); return null; }
    $st = db()->prepare('SELECT id, name, email, role FROM users WHERE id = ? AND is_active = 1');
    $st->execute(array((int)$_SESSION['uid']));
    $user = $st->fetch() ?: null;
    if ($user) { $_SESSION['seen'] = time(); }
    return $user;
}

function require_login()
{
    $pdo = db_or_null();
    if (!$pdo) {
        http_response_code(503);
        exit('The database is not reachable. Check db_name, db_user and db_pass in private/config.php.');
    }
    if (admin_count($pdo) === 0) { redirect('setup.php'); }
    $u = current_user();
    if (!$u) { redirect('login.php?next=' . urlencode($_SERVER['REQUEST_URI'] ?? 'index.php')); }
    return $u;
}

function require_admin()
{
    $u = require_login();
    if ($u['role'] !== 'admin') { http_response_code(403); exit('Only admins can do that.'); }
    return $u;
}

function login_blocked(PDO $pdo, $ip)
{
    $st = $pdo->prepare('SELECT COUNT(*) FROM login_attempts WHERE ip = ? AND success = 0
        AND attempted_at > (NOW() - INTERVAL ' . NCT_LOGIN_WINDOW . ' MINUTE)');
    $st->execute(array($ip));
    return (int)$st->fetchColumn() >= NCT_LOGIN_MAX_FAIL;
}

function record_login(PDO $pdo, $ip, $email, $ok)
{
    $pdo->prepare('INSERT INTO login_attempts (ip, email, success) VALUES (?, ?, ?)')->execute(array($ip, $email, $ok ? 1 : 0));
    $pdo->exec('DELETE FROM login_attempts WHERE attempted_at < (NOW() - INTERVAL 30 DAY)');
}

function sign_in(array $user)
{
    session_regenerate_id(true);
    $_SESSION['uid']  = (int)$user['id'];
    $_SESSION['seen'] = time();
    db()->prepare('UPDATE users SET last_login_at = NOW() WHERE id = ?')->execute(array($user['id']));
}

function safe_next($next)
{
    // Only allow redirects back into the admin area
    return (is_string($next) && preg_match('#^/admin/[\w./?=&%-]*$#', $next)) ? $next : 'index.php';
}

function password_problem($pw)
{
    if (strlen($pw) < 10) { return 'Use at least 10 characters for the password.'; }
    return null;
}
