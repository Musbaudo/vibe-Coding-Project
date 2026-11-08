<?php
/** Leads dashboard: summary, filters, paginated list. */
require dirname(__DIR__) . '/app/auth.php';
require NCT_ROOT . '/app/admin_ui.php';
$user = require_login();
$pdo = db();

// Import leads saved to the CSV backup while the database was unavailable
if ($_SERVER['REQUEST_METHOD'] === 'POST' && ($_POST['action'] ?? '') === 'import_csv') {
    csrf_check();
    nct_m3_import_csv($pdo);
    flash('Backup leads imported: ' . setting('csv_imported', '0'));
    redirect('index.php');
}

$statuses  = $pdo->query('SELECT * FROM lead_statuses ORDER BY sort_order')->fetchAll();
$roles     = $pdo->query('SELECT r.id, r.name, c.name AS cat FROM roles r JOIN role_categories c ON c.id = r.category_id ORDER BY c.sort_order, r.sort_order')->fetchAll();
$locations = $pdo->query('SELECT id, name FROM locations ORDER BY sort_order')->fetchAll();

// Filters
$f = array(
    'q'      => get('q'),
    'type'   => in_array(get('type'), array('employer', 'candidate'), true) ? get('type') : '',
    'status' => get('status', 30),
    'role'   => ctype_digit(get('role')) ? get('role') : '',
    'loc'    => ctype_digit(get('loc')) ? get('loc') : '',
);
list($where, $params) = lead_filters($f);

$perPage = 25;
$page = max(1, (int)get('page'));
$st = $pdo->prepare("SELECT COUNT(*) FROM leads l JOIN lead_statuses s ON s.id = l.status_id $where");
$st->execute($params);
$total = (int)$st->fetchColumn();
$pages = max(1, (int)ceil($total / $perPage));
$page = min($page, $pages);

$st = $pdo->prepare("SELECT l.id, l.type, l.name, l.email, l.company, l.created_at, l.cv_path, l.positions,
        COALESCE(r.name, l.role_other) AS role, COALESCE(lo.name, l.location_other) AS location,
        s.name AS status_name, s.color AS status_color
    FROM leads l
    JOIN lead_statuses s ON s.id = l.status_id
    LEFT JOIN roles r ON r.id = l.role_id
    LEFT JOIN locations lo ON lo.id = l.location_id
    $where ORDER BY l.created_at DESC, l.id DESC LIMIT $perPage OFFSET " . (($page - 1) * $perPage));
$st->execute($params);
$rows = $st->fetchAll();

// Summary
$sum = $pdo->query("SELECT
    SUM(s.is_closed = 0) AS open_leads,
    SUM(s.slug = 'new') AS new_leads,
    SUM(l.created_at > NOW() - INTERVAL 7 DAY) AS week,
    SUM(l.type = 'employer' AND s.is_closed = 0) AS employers,
    SUM(l.type = 'candidate' AND s.is_closed = 0) AS candidates
  FROM leads l JOIN lead_statuses s ON s.id = l.status_id")->fetch();
$byStatus = $pdo->query('SELECT status_id, COUNT(*) FROM leads GROUP BY status_id')->fetchAll(PDO::FETCH_KEY_PAIR);

$csv = cfg('csv_log');
$csvRows = (is_file($csv) ? max(0, count(file($csv)) - 1) : 0);

function qs(array $over)
{
    $all = array_merge($_GET, $over);
    $all = array_filter($all, function ($v) { return is_scalar($v) && (string)$v !== ''; });
    return '?' . http_build_query($all);
}

admin_head('Leads', 'index.php', $user);
?>
<div class="head-row">
  <h1>Leads</h1>
  <a class="btn ghost" href="export.php<?= e(qs(array('page' => ''))) ?>">Export CSV</a>
</div>

<?php if ($csvRows > 0): ?>
<form class="flash err" method="post" style="display:flex;gap:12px;align-items:center;justify-content:space-between;flex-wrap:wrap">
  <span><?= $csvRows ?> lead<?= $csvRows === 1 ? '' : 's' ?> were saved to the CSV backup while the database was unavailable.</span>
  <?= csrf_field() ?><input type="hidden" name="action" value="import_csv">
  <button class="btn sm">Import them now</button>
</form>
<?php endif; ?>

<section class="stats" aria-label="Summary">
  <div class="stat"><b><?= (int)$sum['open_leads'] ?></b><span>Open leads</span></div>
  <div class="stat"><b><?= (int)$sum['new_leads'] ?></b><span>New, not yet contacted</span></div>
  <div class="stat"><b><?= (int)$sum['week'] ?></b><span>Received in the last 7 days</span></div>
  <div class="stat"><b><?= (int)$sum['employers'] ?></b><span>Open employer enquiries</span></div>
  <div class="stat"><b><?= (int)$sum['candidates'] ?></b><span>Open candidates</span></div>
</section>

<nav class="chips" aria-label="Filter by status">
  <a class="chip<?= $f['status'] === '' ? ' on' : '' ?>" href="<?= e(qs(array('status' => '', 'page' => ''))) ?>">All <b><?= array_sum($byStatus) ?></b></a>
  <?php foreach ($statuses as $s): ?>
    <a class="chip<?= $f['status'] === $s['slug'] ? ' on' : '' ?>" href="<?= e(qs(array('status' => $s['slug'], 'page' => ''))) ?>">
      <span class="dot" style="--c:<?= e($s['color']) ?>"></span><?= e($s['name']) ?> <b><?= (int)($byStatus[$s['id']] ?? 0) ?></b>
    </a>
  <?php endforeach; ?>
</nav>

<form class="card filters" method="get">
  <?php if ($f['status'] !== ''): ?><input type="hidden" name="status" value="<?= e($f['status']) ?>"><?php endif; ?>
  <div class="field"><label for="q">Search</label><input id="q" name="q" value="<?= e($f['q']) ?>" placeholder="Name, email, company or NCT-00012"></div>
  <div class="field"><label for="type">Type</label>
    <select id="type" name="type"><option value="">All</option>
      <option value="employer"<?= $f['type'] === 'employer' ? ' selected' : '' ?>>Employers</option>
      <option value="candidate"<?= $f['type'] === 'candidate' ? ' selected' : '' ?>>Candidates</option>
    </select></div>
  <div class="field"><label for="role">Role</label>
    <select id="role" name="role"><option value="">All</option>
      <?php $cat = null; foreach ($roles as $r): if ($r['cat'] !== $cat): if ($cat !== null) echo '</optgroup>'; $cat = $r['cat']; ?><optgroup label="<?= e($cat) ?>"><?php endif; ?>
        <option value="<?= $r['id'] ?>"<?= $f['role'] === (string)$r['id'] ? ' selected' : '' ?>><?= e($r['name']) ?></option>
      <?php endforeach; ?></optgroup>
    </select></div>
  <div class="field"><label for="loc">Location</label>
    <select id="loc" name="loc"><option value="">All</option>
      <?php foreach ($locations as $l): ?><option value="<?= $l['id'] ?>"<?= $f['loc'] === (string)$l['id'] ? ' selected' : '' ?>><?= e($l['name']) ?></option><?php endforeach; ?>
    </select></div>
  <div class="field"><label>&nbsp;</label><a class="btn ghost" href="index.php">Clear</a></div>
  <div class="field"><label>&nbsp;</label><button class="btn">Filter</button></div>
</form>

<div class="table-wrap">
<?php if (!$rows): ?>
  <div class="empty"><?= $total === 0 && !array_filter($f) ? 'No leads yet. New form submissions will appear here.' : 'No leads match these filters.' ?></div>
<?php else: ?>
  <table>
    <thead><tr><th>Ref</th><th>Received</th><th>Type</th><th>Name</th><th>Role</th><th>Location</th><th>Status</th><th>CV</th></tr></thead>
    <tbody>
    <?php foreach ($rows as $r): ?>
      <tr>
        <td class="mono"><?= e(lead_ref($r['id'])) ?></td>
        <td class="num"><?= e(fmt_date($r['created_at'])) ?></td>
        <td><span class="tag<?= $r['type'] === 'candidate' ? ' cand' : '' ?>"><?= $r['type'] === 'employer' ? 'Employer' : 'Candidate' ?></span></td>
        <td><a class="row-link" href="lead.php?id=<?= $r['id'] ?>"><?= e($r['name']) ?></a>
          <div class="muted"><?= e($r['company'] ?: $r['email']) ?><?= $r['positions'] ? ' · ' . (int)$r['positions'] . ' positions' : '' ?></div></td>
        <td><?= e($r['role'] ?: '-') ?></td>
        <td><?= e($r['location'] ?: '-') ?></td>
        <td><?= status_pill(array('name' => $r['status_name'], 'color' => $r['status_color'])) ?></td>
        <td><?= $r['cv_path'] ? '<a href="cv.php?id=' . $r['id'] . '">Download</a>' : '<span class="muted">-</span>' ?></td>
      </tr>
    <?php endforeach; ?>
    </tbody>
  </table>
<?php endif; ?>
</div>

<?php if ($pages > 1): ?>
<nav class="pager" aria-label="Pages">
  <span class="muted"><?= $total ?> leads · page <?= $page ?> of <?= $pages ?></span>
  <?php if ($page > 1): ?><a class="btn ghost sm" href="<?= e(qs(array('page' => $page - 1))) ?>">Previous</a><?php endif; ?>
  <?php if ($page < $pages): ?><a class="btn ghost sm" href="<?= e(qs(array('page' => $page + 1))) ?>">Next</a><?php endif; ?>
</nav>
<?php endif; ?>
<?php admin_foot();
