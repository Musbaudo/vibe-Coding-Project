<?php
/** NyotaCore Talent - small shared helpers. */

if (!function_exists('mb_substr')) {
    function mb_substr($s, $start, $len = null) { return $len === null ? substr($s, $start) : substr($s, $start, $len); }
}

function e($v) { return htmlspecialchars((string)$v, ENT_QUOTES, 'UTF-8'); }

function client_ip() { return substr($_SERVER['REMOTE_ADDR'] ?? '0.0.0.0', 0, 45); }

function json_out($code, $data)
{
    http_response_code($code);
    header('Content-Type: application/json; charset=utf-8');
    echo json_encode($data);
    exit;
}

function redirect($path)
{
    header('Location: ' . $path);
    exit;
}

function post($key, $max = 255, $multiline = false)
{
    $v = isset($_POST[$key]) ? trim((string)$_POST[$key]) : '';
    if (!$multiline) { $v = str_replace(array("\r", "\n"), ' ', $v); }
    return mb_substr($v, 0, $max);
}

function get($key, $max = 120)
{
    return (isset($_GET[$key]) && is_scalar($_GET[$key])) ? mb_substr(trim((string)$_GET[$key]), 0, $max) : '';
}

/** Admin session with safe cookie settings. */
function start_admin_session()
{
    if (session_status() === PHP_SESSION_ACTIVE) { return; }
    $secure = (!empty($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off')
           || (($_SERVER['HTTP_X_FORWARDED_PROTO'] ?? '') === 'https');
    session_name('nct_admin');
    session_set_cookie_params(array(
        'lifetime' => 0, 'path' => '/admin', 'secure' => $secure,
        'httponly' => true, 'samesite' => 'Lax',
    ));
    session_start();
}

function csrf_token()
{
    if (empty($_SESSION['csrf'])) { $_SESSION['csrf'] = bin2hex(random_bytes(32)); }
    return $_SESSION['csrf'];
}

function csrf_field() { return '<input type="hidden" name="csrf" value="' . e(csrf_token()) . '">'; }

function csrf_check()
{
    $sent = $_POST['csrf'] ?? '';
    if (!is_string($sent) || empty($_SESSION['csrf']) || !hash_equals($_SESSION['csrf'], $sent)) {
        http_response_code(400);
        exit('Your session expired. Go back, refresh the page and try again.');
    }
}

function flash($msg = null, $kind = 'ok')
{
    if ($msg !== null) { $_SESSION['flash'] = array($kind, $msg); return null; }
    $f = $_SESSION['flash'] ?? null;
    unset($_SESSION['flash']);
    return $f;
}

function setting($key, $default = null)
{
    $st = db()->prepare('SELECT v FROM settings WHERE k = ?');
    $st->execute(array($key));
    $v = $st->fetchColumn();
    return $v === false ? $default : $v;
}

function set_setting($key, $value)
{
    db()->prepare('INSERT INTO settings (k, v) VALUES (?, ?) ON DUPLICATE KEY UPDATE v = VALUES(v)')
        ->execute(array($key, (string)$value));
}

function lead_ref($id) { return 'NCT-' . str_pad((string)$id, 5, '0', STR_PAD_LEFT); }

function fmt_date($dt, $withTime = true)
{
    if (!$dt) { return ''; }
    $t = strtotime($dt);
    return $withTime ? date('j M Y, H:i', $t) : date('j M Y', $t);
}
