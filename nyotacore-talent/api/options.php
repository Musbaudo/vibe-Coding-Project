<?php
/** Roles and locations for the call-back form, from the database. */
require dirname(__DIR__) . '/app/bootstrap.php';
require NCT_ROOT . '/app/leads.php';

header('Cache-Control: public, max-age=300');
header('X-Robots-Tag: noindex');

$pdo = db_or_null();
if (!$pdo) { json_out(503, array('ok' => false)); }
json_out(200, array('ok' => true) + form_options($pdo));
