<?php
/**
 * NyotaCore Talent - call-back form handler.
 * Saves each enquiry to the database, stores an optional CV outside the web root,
 * and emails the recruitment inbox through SMTP.
 * If the database is down, the lead is written to the CSV backup instead
 * (the admin dashboard offers to import it later).
 */
require __DIR__ . '/app/bootstrap.php';
require NCT_ROOT . '/app/leads.php';
require NCT_ROOT . '/app/mailer.php';

header('X-Robots-Tag: noindex');

if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') {
    json_out(405, array('ok' => false, 'error' => 'Method not allowed.'));
}

// Bots fill the hidden "website" field: accept quietly and drop.
if (post('website') !== '') { json_out(200, array('ok' => true)); }

// Forms submitted within 3 seconds of page load are almost always bots.
$ts = (int)post('ts', 20);
if ($ts > 0 && (microtime(true) * 1000 - $ts) < 3000) {
    json_out(400, array('ok' => false, 'error' => 'Please take a moment to complete the form, then try again.'));
}

$pdo = db_or_null();
$ip  = client_ip();

// Rate limit per IP (database when available, temp file otherwise).
$limit = (int)cfg('max_per_hour', 5);
if ($pdo) {
    $st = $pdo->prepare('SELECT COUNT(*) FROM leads WHERE ip = ? AND created_at > (NOW() - INTERVAL 1 HOUR)');
    $st->execute(array($ip));
    $recent = (int)$st->fetchColumn();
} else {
    $rate = sys_get_temp_dir() . '/nc_talent_' . md5($ip);
    $hits = array();
    foreach (explode(',', (string)@file_get_contents($rate)) as $t) { if ((int)$t > time() - 3600) { $hits[] = (int)$t; } }
    $recent = count($hits);
    $hits[] = time();
    @file_put_contents($rate, implode(',', $hits));
}
if ($recent >= $limit) {
    json_out(429, array('ok' => false, 'error' => 'Too many requests from your connection. Please try again in an hour.'));
}

// Fields
$type      = post('mode', 20) === 'candidate' ? 'candidate' : 'employer';
$name      = post('name', 120);
$email     = post('email', 190);
$phone     = post('phone', 40);
$roleIn    = post('role', 120);
$locIn     = post('location', 80);
$company   = $type === 'employer' ? post('company', 160) : '';
$positions = $type === 'employer' ? post('positions', 6) : '';
$years     = $type === 'candidate' ? post('years', 3) : '';
$message   = post('message', 3000, true);

if ($name === '' || !filter_var($email, FILTER_VALIDATE_EMAIL)) {
    json_out(422, array('ok' => false, 'error' => 'Enter your name and a valid email address so we can reach you.'));
}
$positions = ctype_digit($positions) ? min((int)$positions, 65000) : null;
$years     = ctype_digit($years) ? min((int)$years, 70) : null;

// Optional CV (candidates only)
$cvRel = null; $cvOrig = null; $cvAbs = null;
if ($type === 'candidate' && isset($_FILES['cv']) && $_FILES['cv']['error'] !== UPLOAD_ERR_NO_FILE) {
    $f = $_FILES['cv'];
    $maxBytes = (int)cfg('cv_max_mb', 5) * 1024 * 1024;
    if ($f['error'] === UPLOAD_ERR_INI_SIZE || $f['error'] === UPLOAD_ERR_FORM_SIZE || $f['size'] > $maxBytes) {
        json_out(422, array('ok' => false, 'error' => 'Your CV is too large. The limit is ' . (int)cfg('cv_max_mb', 5) . ' MB.'));
    }
    if ($f['error'] !== UPLOAD_ERR_OK || !is_uploaded_file($f['tmp_name'])) {
        json_out(422, array('ok' => false, 'error' => 'Your CV did not upload. Please try again.'));
    }
    $ext = strtolower(pathinfo($f['name'], PATHINFO_EXTENSION));
    $allowed = array(
        'pdf'  => array('application/pdf'),
        'doc'  => array('application/msword', 'application/octet-stream', 'application/x-ole-storage', 'application/CDFV2'),
        'docx' => array('application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'application/zip', 'application/octet-stream'),
    );
    $mime = function_exists('finfo_open') ? finfo_file(finfo_open(FILEINFO_MIME_TYPE), $f['tmp_name']) : 'application/octet-stream';
    if (!isset($allowed[$ext]) || !in_array($mime, $allowed[$ext], true)) {
        json_out(422, array('ok' => false, 'error' => 'Upload your CV as a PDF or Word document.'));
    }
    $sub  = date('Y/m');
    $root = rtrim(cfg('uploads_dir'), '/\\');
    $dir  = $root . '/' . $sub;
    // If the uploads folder ever sits inside a web root, keep it closed anyway
    if ((is_dir($root) || @mkdir($root, 0750, true)) && !is_file($root . '/.htaccess')) {
        @file_put_contents($root . '/.htaccess', "Require all denied\n");
    }
    if (!is_dir($dir) && !@mkdir($dir, 0750, true)) {
        error_log('NyotaCore Talent: cannot create upload dir ' . $dir);
        json_out(500, array('ok' => false, 'error' => 'We could not save your CV right now. Please try again shortly.'));
    }
    $cvRel  = $sub . '/' . bin2hex(random_bytes(16)) . '.' . $ext;
    $cvAbs  = rtrim(cfg('uploads_dir'), '/\\') . '/' . $cvRel;
    $cvOrig = mb_substr(preg_replace('/[^\w .()-]+/u', '_', basename($f['name'])), 0, 200);
    if (!move_uploaded_file($f['tmp_name'], $cvAbs)) {
        json_out(500, array('ok' => false, 'error' => 'We could not save your CV right now. Please try again shortly.'));
    }
    @chmod($cvAbs, 0640);
}

// Save
$leadId = null; $ref = null;
$roleName = $roleIn; $locName = $locIn;
if ($pdo) {
    try {
        $roleId = resolve_id($pdo, 'roles', $roleIn);
        $locId  = resolve_id($pdo, 'locations', $locIn);
        $roleName = $roleId ? name_of($pdo, 'roles', $roleId) : $roleIn;
        $locName  = $locId ? name_of($pdo, 'locations', $locId) : $locIn;
        $pdo->prepare('INSERT INTO leads (type, name, email, phone, company, positions, years_experience, role_id, role_other,
            location_id, location_other, message, status_id, cv_path, cv_original, source, ip, user_agent)
            VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)')
            ->execute(array(
                $type, $name, $email, $phone ?: null, $company ?: null, $positions, $years,
                $roleId, $roleId ? null : ($roleIn ?: null), $locId, $locId ? null : ($locIn ?: null),
                $message ?: null, status_id($pdo, 'new'), $cvRel, $cvOrig, 'web', $ip,
                mb_substr($_SERVER['HTTP_USER_AGENT'] ?? '', 0, 255),
            ));
        $leadId = (int)$pdo->lastInsertId();
        $ref = lead_ref($leadId);
    } catch (Throwable $e) {
        error_log('NyotaCore Talent: lead insert failed: ' . $e->getMessage());
        $pdo = null;
    }
}

// Email
$label = $type === 'employer' ? 'Employer enquiry' : 'Candidate registration';
$subject = sprintf('[NyotaCore Talent]%s %s: %s (%s)', $ref ? ' ' . $ref : '', $label, $roleName ?: '-', $locName ?: '-');
$lines = array('Type' => $label, 'Name' => $name, 'Email' => $email, 'Phone' => $phone,
    ($type === 'employer' ? 'Hiring for' : 'Open to work in') => $locName, 'Role' => $roleName);
if ($type === 'employer') { $lines['Company'] = $company; $lines['Positions'] = (string)$positions; }
else { $lines['Years of experience'] = (string)$years; $lines['CV'] = $cvOrig ? $cvOrig . ' (attached)' : 'none'; }
$body = '';
foreach ($lines as $k => $v) { $body .= str_pad($k . ':', 22) . ($v !== '' && $v !== null ? $v : '-') . "\n"; }
$body .= "\nMessage:\n" . ($message !== '' ? $message : '-') . "\n\n";
if ($leadId) { $body .= 'Open in admin: ' . rtrim(cfg('app_url'), '/') . '/admin/lead.php?id=' . $leadId . "\n"; }
$body .= 'Submitted: ' . date('Y-m-d H:i T') . "\n";

$sent = send_mail(cfg('to_email'), $subject, $body, array(
    'reply_to' => array($email, $name),
    'attach'   => $cvAbs ? array($cvAbs, $cvOrig) : null,
));
if ($leadId && $sent) { $pdo->prepare('UPDATE leads SET mail_sent = 1 WHERE id = ?')->execute(array($leadId)); }

// CSV backup when the database could not take the lead
if (!$leadId) {
    $csv = cfg('csv_log');
    $new = !is_file($csv);
    $fh = @fopen($csv, 'a');
    if ($fh) {
        if ($new) { fputcsv($fh, array('submitted','type','name','email','phone','location','role','company','positions','years','message','ip','mail')); }
        fputcsv($fh, array(date('c'), $label, $name, $email, $phone, $locName, $roleName, $company, $positions, $years, $message, $ip, $sent ? 'mailed' : 'MAIL FAILED'));
        fclose($fh);
    } elseif (!$sent) {
        json_out(500, array('ok' => false, 'error' => 'We could not send your request right now. Please try again shortly.'));
    }
}

json_out(200, array('ok' => true, 'ref' => $ref));
