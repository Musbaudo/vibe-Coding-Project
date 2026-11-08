<?php
/** Download the current lead list (same filters as the dashboard) as CSV. */
require dirname(__DIR__) . '/app/auth.php';
require_login();
$pdo = db();

$f = array(
    'q'      => get('q'),
    'type'   => in_array(get('type'), array('employer', 'candidate'), true) ? get('type') : '',
    'status' => get('status', 30),
    'role'   => ctype_digit(get('role')) ? get('role') : '',
    'loc'    => ctype_digit(get('loc')) ? get('loc') : '',
);
list($where, $params) = lead_filters($f);
$st = $pdo->prepare("SELECT l.id, l.created_at, l.type, l.name, l.email, l.phone, l.company, l.positions, l.years_experience,
        COALESCE(r.name, l.role_other) AS role, COALESCE(lo.name, l.location_other) AS location, s.name AS status,
        u.name AS owner, l.message, IF(l.cv_path IS NULL, 'no', 'yes') AS has_cv, l.source
    FROM leads l JOIN lead_statuses s ON s.id = l.status_id
    LEFT JOIN roles r ON r.id = l.role_id LEFT JOIN locations lo ON lo.id = l.location_id
    LEFT JOIN users u ON u.id = l.assigned_to
    $where ORDER BY l.created_at DESC");
$st->execute($params);

header('Content-Type: text/csv; charset=utf-8');
header('Content-Disposition: attachment; filename="nyotacore-talent-leads-' . date('Y-m-d') . '.csv"');
$out = fopen('php://output', 'w');
fwrite($out, "\xEF\xBB\xBF"); // so Excel reads UTF-8 correctly
fputcsv($out, array('Ref', 'Received', 'Type', 'Name', 'Email', 'Phone', 'Company', 'Positions', 'Years experience', 'Role', 'Location', 'Status', 'Owner', 'Message', 'CV', 'Source'));
while ($r = $st->fetch()) {
    $row = array(lead_ref($r['id']), $r['created_at'], ucfirst($r['type']), $r['name'], $r['email'], $r['phone'], $r['company'],
        $r['positions'], $r['years_experience'], $r['role'], $r['location'], $r['status'], $r['owner'], $r['message'], $r['has_cv'], $r['source']);
    // Stop spreadsheet formula injection
    foreach ($row as &$v) { if (is_string($v) && $v !== '' && strpos('=+-@', $v[0]) !== false) { $v = "'" . $v; } }
    fputcsv($out, $row);
}
fclose($out);
