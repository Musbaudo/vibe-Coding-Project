<?php
/**
 * NyotaCore Talent - schema migrations and seed data.
 * Runs automatically on the first database connection of each request.
 * Each migration runs once and is recorded in schema_migrations.
 * Seeds use INSERT IGNORE on unique slugs, so re-running never duplicates data.
 */

function nct_migrations()
{
    return array(
        1 => 'nct_m1_schema',
        2 => 'nct_m2_seed',
        3 => 'nct_m3_import_csv',
    );
}

function nct_migrate(PDO $pdo)
{
    $pdo->exec('CREATE TABLE IF NOT EXISTS schema_migrations (
        version INT UNSIGNED NOT NULL PRIMARY KEY,
        applied_at DATETIME NOT NULL
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci');

    $done = $pdo->query('SELECT version FROM schema_migrations')->fetchAll(PDO::FETCH_COLUMN);
    $pending = array_diff(array_keys(nct_migrations()), array_map('intval', $done));
    if (!$pending) { return; }

    // One request migrates at a time; others wait up to 30s.
    $got = (int)$pdo->query("SELECT GET_LOCK('nct_migrate', 30)")->fetchColumn();
    if ($got !== 1) { throw new RuntimeException('Could not obtain migration lock'); }
    try {
        $done = array_map('intval', $pdo->query('SELECT version FROM schema_migrations')->fetchAll(PDO::FETCH_COLUMN));
        foreach (nct_migrations() as $version => $fn) {
            if (in_array($version, $done, true)) { continue; }
            $fn($pdo);
            $pdo->prepare('INSERT INTO schema_migrations (version, applied_at) VALUES (?, NOW())')->execute(array($version));
        }
    } finally {
        $pdo->query("SELECT RELEASE_LOCK('nct_migrate')");
    }
}

/* ------------------------------------------------------------------ */
/* 1. Tables                                                           */
/* ------------------------------------------------------------------ */
function nct_m1_schema(PDO $pdo)
{
    $t = ' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci';

    $pdo->exec('CREATE TABLE IF NOT EXISTS settings (
        k VARCHAR(64) NOT NULL PRIMARY KEY,
        v TEXT NULL
    )' . $t);

    $pdo->exec('CREATE TABLE IF NOT EXISTS role_categories (
        id SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
        slug VARCHAR(40) NOT NULL UNIQUE,
        name VARCHAR(80) NOT NULL,
        sort_order SMALLINT NOT NULL DEFAULT 0
    )' . $t);

    $pdo->exec('CREATE TABLE IF NOT EXISTS roles (
        id INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
        category_id SMALLINT UNSIGNED NOT NULL,
        slug VARCHAR(80) NOT NULL UNIQUE,
        name VARCHAR(120) NOT NULL,
        description VARCHAR(255) NULL,
        is_active TINYINT(1) NOT NULL DEFAULT 1,
        sort_order SMALLINT NOT NULL DEFAULT 0,
        created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
        KEY idx_roles_cat (category_id, is_active, sort_order),
        CONSTRAINT fk_roles_cat FOREIGN KEY (category_id) REFERENCES role_categories (id)
    )' . $t);

    $pdo->exec("CREATE TABLE IF NOT EXISTS locations (
        id SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
        slug VARCHAR(40) NOT NULL UNIQUE,
        name VARCHAR(80) NOT NULL,
        market ENUM('local','international','remote','other') NOT NULL DEFAULT 'international',
        is_active TINYINT(1) NOT NULL DEFAULT 1,
        sort_order SMALLINT NOT NULL DEFAULT 0
    )" . $t);

    $pdo->exec('CREATE TABLE IF NOT EXISTS lead_statuses (
        id SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
        slug VARCHAR(30) NOT NULL UNIQUE,
        name VARCHAR(60) NOT NULL,
        color VARCHAR(9) NOT NULL DEFAULT \'#6b7a8f\',
        is_closed TINYINT(1) NOT NULL DEFAULT 0,
        sort_order SMALLINT NOT NULL DEFAULT 0
    )' . $t);

    $pdo->exec("CREATE TABLE IF NOT EXISTS users (
        id INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
        name VARCHAR(120) NOT NULL,
        email VARCHAR(190) NOT NULL UNIQUE,
        password_hash VARCHAR(255) NOT NULL,
        role ENUM('admin','recruiter') NOT NULL DEFAULT 'recruiter',
        is_active TINYINT(1) NOT NULL DEFAULT 1,
        last_login_at DATETIME NULL,
        created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
    )" . $t);

    $pdo->exec("CREATE TABLE IF NOT EXISTS leads (
        id INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
        type ENUM('employer','candidate') NOT NULL,
        name VARCHAR(120) NOT NULL,
        email VARCHAR(190) NOT NULL,
        phone VARCHAR(40) NULL,
        company VARCHAR(160) NULL,
        positions SMALLINT UNSIGNED NULL,
        years_experience SMALLINT UNSIGNED NULL,
        role_id INT UNSIGNED NULL,
        role_other VARCHAR(120) NULL,
        location_id SMALLINT UNSIGNED NULL,
        location_other VARCHAR(80) NULL,
        message TEXT NULL,
        status_id SMALLINT UNSIGNED NOT NULL,
        assigned_to INT UNSIGNED NULL,
        cv_path VARCHAR(255) NULL,
        cv_original VARCHAR(255) NULL,
        source VARCHAR(30) NOT NULL DEFAULT 'web',
        ip VARCHAR(45) NULL,
        user_agent VARCHAR(255) NULL,
        mail_sent TINYINT(1) NOT NULL DEFAULT 0,
        created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
        KEY idx_leads_type (type),
        KEY idx_leads_status (status_id),
        KEY idx_leads_created (created_at),
        KEY idx_leads_email (email),
        KEY idx_leads_ip (ip, created_at),
        CONSTRAINT fk_leads_role FOREIGN KEY (role_id) REFERENCES roles (id) ON DELETE SET NULL,
        CONSTRAINT fk_leads_loc FOREIGN KEY (location_id) REFERENCES locations (id) ON DELETE SET NULL,
        CONSTRAINT fk_leads_status FOREIGN KEY (status_id) REFERENCES lead_statuses (id),
        CONSTRAINT fk_leads_user FOREIGN KEY (assigned_to) REFERENCES users (id) ON DELETE SET NULL
    )" . $t);

    $pdo->exec("CREATE TABLE IF NOT EXISTS lead_notes (
        id INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
        lead_id INT UNSIGNED NOT NULL,
        user_id INT UNSIGNED NULL,
        kind ENUM('note','status','system') NOT NULL DEFAULT 'note',
        body TEXT NOT NULL,
        created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
        KEY idx_notes_lead (lead_id, created_at),
        CONSTRAINT fk_notes_lead FOREIGN KEY (lead_id) REFERENCES leads (id) ON DELETE CASCADE,
        CONSTRAINT fk_notes_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE SET NULL
    )" . $t);

    $pdo->exec('CREATE TABLE IF NOT EXISTS login_attempts (
        id INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
        ip VARCHAR(45) NOT NULL,
        email VARCHAR(190) NULL,
        success TINYINT(1) NOT NULL DEFAULT 0,
        attempted_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
        KEY idx_login_ip (ip, attempted_at)
    )' . $t);
}

/* ------------------------------------------------------------------ */
/* 2. Reference data                                                   */
/* ------------------------------------------------------------------ */
function nct_m2_seed(PDO $pdo)
{
    $cats = array(
        array('trades', 'Skilled trades', 1),
        array('tech', 'Tech & professional', 2),
    );
    $st = $pdo->prepare('INSERT IGNORE INTO role_categories (slug, name, sort_order) VALUES (?, ?, ?)');
    foreach ($cats as $c) { $st->execute($c); }
    $catId = $pdo->query('SELECT slug, id FROM role_categories')->fetchAll(PDO::FETCH_KEY_PAIR);

    $roles = array(
        'trades' => array(
            array('light-vehicle-mechanic', 'Light Vehicle Mechanic', 'Diagnostics, servicing and repair of cars, SUVs and pickups'),
            array('heavy-vehicle-mechanic', 'Heavy Vehicle Mechanic', 'Diesel engines, brakes and drivetrains on trucks, buses and plant'),
            array('panel-beater', 'Panel Beater / Body Repair', 'Collision repair, panel work and chassis welding'),
            array('welder-fabricator', 'Welder / Fabricator', 'MIG, TIG and stick welding for structural steel and pipework'),
            array('electrician', 'Electrician', 'Installation, maintenance and fault-finding'),
            array('carpenter-joiner', 'Carpenter / Joiner', 'Framing, formwork, joinery and fitted furniture'),
            array('construction-trades', 'Construction Trades', 'Site-ready tradespeople and assistants'),
            array('production-operator', 'Production / Machine Operator', 'FMCG lines, textile and machine operation'),
        ),
        'tech' => array(
            array('cloud-engineer', 'Cloud Engineer', 'AWS, Azure and GCP builds, migrations and landing zones'),
            array('devops-engineer', 'DevOps Engineer', 'CI/CD, containers, Terraform and release automation'),
            array('sre', 'Site Reliability Engineer', 'Monitoring, incident response and uptime'),
            array('software-developer', 'Software Developer', 'Web, mobile and backend development'),
            array('ai-engineer', 'AI Engineer', 'LLM apps, AI agents and ML pipelines'),
            array('data-engineer', 'Data Engineer / Analyst', 'Pipelines, warehousing and BI dashboards'),
            array('it-support', 'IT Support', 'Service desk, end-user and infrastructure support'),
            array('customer-support', 'Customer Support', 'Voice, chat and email support agents'),
        ),
    );
    $st = $pdo->prepare('INSERT IGNORE INTO roles (category_id, slug, name, description, sort_order) VALUES (?, ?, ?, ?, ?)');
    foreach ($roles as $cat => $list) {
        foreach ($list as $i => $r) { $st->execute(array($catId[$cat], $r[0], $r[1], $r[2], $i + 1)); }
    }

    $locs = array(
        array('nigeria', 'Nigeria', 'local', 1),
        array('remote', 'Remote', 'remote', 2),
        array('australia', 'Australia', 'international', 3),
        array('united-kingdom', 'United Kingdom', 'international', 4),
        array('canada', 'Canada', 'international', 5),
        array('germany', 'Germany', 'international', 6),
        array('gulf-states', 'Gulf States', 'international', 7),
        array('other', 'Other', 'other', 99),
    );
    $st = $pdo->prepare('INSERT IGNORE INTO locations (slug, name, market, sort_order) VALUES (?, ?, ?, ?)');
    foreach ($locs as $l) { $st->execute($l); }

    $statuses = array(
        array('new', 'New', '#3b5bff', 0, 1),
        array('contacted', 'Contacted', '#0ea5e9', 0, 2),
        array('screening', 'Screening', '#8b5cf6', 0, 3),
        array('shortlisted', 'Shortlisted', '#f59e0b', 0, 4),
        array('interviewing', 'Interviewing', '#d97706', 0, 5),
        array('placed', 'Placed', '#1a7a42', 1, 6),
        array('on-hold', 'On hold', '#6b7a8f', 0, 7),
        array('not-suitable', 'Not suitable', '#9ca3af', 1, 8),
        array('spam', 'Spam', '#ef4444', 1, 9),
    );
    $st = $pdo->prepare('INSERT IGNORE INTO lead_statuses (slug, name, color, is_closed, sort_order) VALUES (?, ?, ?, ?, ?)');
    foreach ($statuses as $s) { $st->execute($s); }

    $st = $pdo->prepare('INSERT IGNORE INTO settings (k, v) VALUES (?, ?)');
    $st->execute(array('installed_at', date('c')));
    $st->execute(array('app_name', 'NyotaCore Talent'));
}

/* ------------------------------------------------------------------ */
/* 3. Bring in leads saved to the CSV before the database existed       */
/* ------------------------------------------------------------------ */
function nct_m3_import_csv(PDO $pdo)
{
    $csv = cfg('csv_log');
    if (!$csv || !is_file($csv) || !is_readable($csv)) { return; }
    $fh = fopen($csv, 'r');
    if (!$fh) { return; }

    $roles  = array_change_key_case($pdo->query('SELECT name, id FROM roles')->fetchAll(PDO::FETCH_KEY_PAIR));
    $locs   = array_change_key_case($pdo->query('SELECT name, id FROM locations')->fetchAll(PDO::FETCH_KEY_PAIR));
    $status = (int)$pdo->query("SELECT id FROM lead_statuses WHERE slug = 'new'")->fetchColumn();
    $ins = $pdo->prepare('INSERT INTO leads (type, name, email, phone, company, positions, years_experience, role_id, role_other,
        location_id, location_other, message, status_id, source, ip, mail_sent, created_at)
        VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)');
    $note = $pdo->prepare("INSERT INTO lead_notes (lead_id, kind, body) VALUES (?, 'system', 'Imported from the CSV backup')");

    $header = fgetcsv($fh);
    $n = 0;
    while (($r = fgetcsv($fh)) !== false) {
        if (count($r) < 13 || !filter_var($r[3], FILTER_VALIDATE_EMAIL)) { continue; }
        $type = stripos($r[1], 'candidate') !== false ? 'candidate' : 'employer';
        $roleKey = strtolower(trim($r[6])); $locKey = strtolower(trim($r[5]));
        $created = date('Y-m-d H:i:s', strtotime($r[0]) ?: time());
        $ins->execute(array(
            $type, mb_substr($r[2], 0, 120), $r[3], $r[4] ?: null, $r[7] ?: null,
            ctype_digit($r[8]) ? (int)$r[8] : null, ctype_digit($r[9]) ? (int)$r[9] : null,
            $roles[$roleKey] ?? null, isset($roles[$roleKey]) ? null : ($r[6] ?: null),
            $locs[$locKey] ?? null, isset($locs[$locKey]) ? null : ($r[5] ?: null),
            $r[10] ?: null, $status, 'csv-import', $r[11] ?: null, $r[12] === 'mailed' ? 1 : 0, $created,
        ));
        $note->execute(array($pdo->lastInsertId()));
        $n++;
    }
    fclose($fh);
    @rename($csv, $csv . '.imported-' . date('Ymd-His'));
    $pdo->prepare('INSERT INTO settings (k, v) VALUES (?, ?) ON DUPLICATE KEY UPDATE v = VALUES(v)')
        ->execute(array('csv_imported', $n . ' leads on ' . date('c')));
}
