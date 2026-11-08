<?php
/** Stream a candidate's CV to a signed-in admin user. */
require dirname(__DIR__) . '/app/auth.php';
require_login();

$st = db()->prepare('SELECT id, name, cv_path, cv_original FROM leads WHERE id = ?');
$st->execute(array((int)($_GET['id'] ?? 0)));
$lead = $st->fetch();
$path = $lead ? cv_abs_path($lead['cv_path']) : null;
if (!$path) { http_response_code(404); exit('CV not found.'); }

$ext  = strtolower(pathinfo($path, PATHINFO_EXTENSION));
$type = array('pdf' => 'application/pdf', 'doc' => 'application/msword',
    'docx' => 'application/vnd.openxmlformats-officedocument.wordprocessingml.document');
$name = lead_ref($lead['id']) . ' - ' . preg_replace('/[^\w .-]+/u', '', $lead['name']) . '.' . $ext;

header('Content-Type: ' . ($type[$ext] ?? 'application/octet-stream'));
header('Content-Disposition: attachment; filename="' . str_replace('"', '', $name) . '"');
header('Content-Length: ' . filesize($path));
header('X-Content-Type-Options: nosniff');
readfile($path);
