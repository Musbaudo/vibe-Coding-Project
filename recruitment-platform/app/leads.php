<?php
/** NyotaCore Talent - lead lookups shared by the public form and the admin area. */

/** Active roles grouped by category, and active locations, for the form. */
function form_options(PDO $pdo)
{
    $roles = $pdo->query('SELECT r.id, r.name, c.name AS category
        FROM roles r JOIN role_categories c ON c.id = r.category_id
        WHERE r.is_active = 1 ORDER BY c.sort_order, r.sort_order, r.name')->fetchAll();
    $locs = $pdo->query('SELECT id, name, market FROM locations WHERE is_active = 1 ORDER BY sort_order, name')->fetchAll();
    return array('roles' => $roles, 'locations' => $locs);
}

/** Accept an id or a display name from the form and resolve it to a row id. */
function resolve_id(PDO $pdo, $table, $value)
{
    if ($value === '') { return null; }
    if (ctype_digit($value)) {
        $st = $pdo->prepare("SELECT id FROM $table WHERE id = ?");
        $st->execute(array((int)$value));
    } else {
        $st = $pdo->prepare("SELECT id FROM $table WHERE name = ?");
        $st->execute(array($value));
    }
    $id = $st->fetchColumn();
    return $id === false ? null : (int)$id;
}

function name_of(PDO $pdo, $table, $id)
{
    if (!$id) { return null; }
    $st = $pdo->prepare("SELECT name FROM $table WHERE id = ?");
    $st->execute(array($id));
    $v = $st->fetchColumn();
    return $v === false ? null : $v;
}

function status_id(PDO $pdo, $slug)
{
    $st = $pdo->prepare('SELECT id FROM lead_statuses WHERE slug = ?');
    $st->execute(array($slug));
    return (int)$st->fetchColumn();
}

function add_note(PDO $pdo, $leadId, $userId, $body, $kind = 'note')
{
    $pdo->prepare('INSERT INTO lead_notes (lead_id, user_id, kind, body) VALUES (?, ?, ?, ?)')
        ->execute(array($leadId, $userId, $kind, $body));
}

/** WHERE clause + params for the admin lead list and export. Expects aliases l (leads) and s (lead_statuses). */
function lead_filters(array $f)
{
    $w = array(); $p = array();
    if ($f['q'] !== '') {
        if (preg_match('/^NCT-?0*(\d+)$/i', $f['q'], $m)) { $w[] = 'l.id = ?'; $p[] = (int)$m[1]; }
        else {
            $w[] = '(l.name LIKE ? OR l.email LIKE ? OR l.company LIKE ? OR l.phone LIKE ?)';
            $like = '%' . str_replace(array('%', '_'), array('\%', '\_'), $f['q']) . '%';
            array_push($p, $like, $like, $like, $like);
        }
    }
    if ($f['type'] !== '')   { $w[] = 'l.type = ?';        $p[] = $f['type']; }
    if ($f['status'] !== '') { $w[] = 's.slug = ?';        $p[] = $f['status']; }
    if ($f['role'] !== '')   { $w[] = 'l.role_id = ?';     $p[] = (int)$f['role']; }
    if ($f['loc'] !== '')    { $w[] = 'l.location_id = ?'; $p[] = (int)$f['loc']; }
    return array($w ? 'WHERE ' . implode(' AND ', $w) : '', $p);
}

/** Absolute path of a stored CV, guarded against path tricks. */
function cv_abs_path($rel)
{
    if (!$rel || strpos($rel, '..') !== false) { return null; }
    $p = rtrim(cfg('uploads_dir'), '/\\') . '/' . ltrim($rel, '/\\');
    return is_file($p) ? $p : null;
}
