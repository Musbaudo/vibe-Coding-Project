<?php
/** One lead: details, status changes, notes, CV, delete. */
require dirname(__DIR__) . '/app/auth.php';
require NCT_ROOT . '/app/admin_ui.php';
$user = require_login();
$pdo = db();

$id = (int)($_GET['id'] ?? 0);
$st = $pdo->prepare('SELECT l.*, COALESCE(r.name, l.role_other) AS role, rc.name AS role_cat,
        COALESCE(lo.name, l.location_other) AS location, s.slug AS status_slug, s.name AS status_name, s.color AS status_color,
        u.name AS assignee
    FROM leads l
    JOIN lead_statuses s ON s.id = l.status_id
    LEFT JOIN roles r ON r.id = l.role_id
    LEFT JOIN role_categories rc ON rc.id = r.category_id
    LEFT JOIN locations lo ON lo.id = l.location_id
    LEFT JOIN users u ON u.id = l.assigned_to
    WHERE l.id = ?');
$st->execute(array($id));
$lead = $st->fetch();
if (!$lead) { http_response_code(404); admin_head('Not found', 'index.php', $user); echo '<div class="card">That lead does not exist. <a href="index.php">Back to leads</a></div>'; admin_foot(); exit; }

$statuses = $pdo->query('SELECT * FROM lead_statuses ORDER BY sort_order')->fetchAll();
$team = $pdo->query('SELECT id, name FROM users WHERE is_active = 1 ORDER BY name')->fetchAll();

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    csrf_check();
    $action = $_POST['action'] ?? '';
    if ($action === 'update') {
        $newStatus = (int)($_POST['status_id'] ?? 0);
        $assign = ctype_digit((string)($_POST['assigned_to'] ?? '')) ? (int)$_POST['assigned_to'] : null;
        $note = post('note', 5000, true);
        $changes = array();
        foreach ($statuses as $s) {
            if ((int)$s['id'] === $newStatus && $newStatus !== (int)$lead['status_id']) {
                $pdo->prepare('UPDATE leads SET status_id = ? WHERE id = ?')->execute(array($newStatus, $id));
                add_note($pdo, $id, $user['id'], 'Status: ' . $lead['status_name'] . ' → ' . $s['name'], 'status');
                $changes[] = 'status';
            }
        }
        if ($assign !== ($lead['assigned_to'] === null ? null : (int)$lead['assigned_to'])) {
            $pdo->prepare('UPDATE leads SET assigned_to = ? WHERE id = ?')->execute(array($assign, $id));
            $who = 'nobody';
            foreach ($team as $t) { if ((int)$t['id'] === $assign) { $who = $t['name']; } }
            add_note($pdo, $id, $user['id'], 'Assigned to ' . $who, 'status');
            $changes[] = 'owner';
        }
        if ($note !== '') { add_note($pdo, $id, $user['id'], $note, 'note'); $changes[] = 'note'; }
        flash($changes ? 'Saved (' . implode(', ', $changes) . ').' : 'Nothing changed.');
        redirect('lead.php?id=' . $id);
    }
    if ($action === 'delete') {
        if ($user['role'] !== 'admin') { flash('Only admins can delete leads.', 'err'); redirect('lead.php?id=' . $id); }
        if (($_POST['confirm'] ?? '') !== lead_ref($id)) { flash('Type the reference ' . lead_ref($id) . ' to confirm deletion.', 'err'); redirect('lead.php?id=' . $id); }
        if ($p = cv_abs_path($lead['cv_path'])) { @unlink($p); }
        $pdo->prepare('DELETE FROM leads WHERE id = ?')->execute(array($id));
        flash(lead_ref($id) . ' and its CV were deleted.');
        redirect('index.php');
    }
}

$notes = $pdo->prepare('SELECT n.*, u.name AS author FROM lead_notes n LEFT JOIN users u ON u.id = n.user_id WHERE n.lead_id = ? ORDER BY n.created_at DESC, n.id DESC');
$notes->execute(array($id));
$notes = $notes->fetchAll();

// Earlier enquiries from the same email
$prev = $pdo->prepare('SELECT id, type, created_at FROM leads WHERE email = ? AND id <> ? ORDER BY created_at DESC LIMIT 5');
$prev->execute(array($lead['email'], $id));
$prev = $prev->fetchAll();

$isEmp = $lead['type'] === 'employer';
admin_head(lead_ref($id), 'index.php', $user);
?>
<div class="head-row">
  <div>
    <a href="index.php" class="muted">← All leads</a>
    <h1><?= e($lead['name']) ?> <span class="mono muted" style="font-size:16px"><?= e(lead_ref($id)) ?></span></h1>
  </div>
  <div class="row">
    <span class="tag<?= $isEmp ? '' : ' cand' ?>"><?= $isEmp ? 'Employer' : 'Candidate' ?></span>
    <?= status_pill(array('name' => $lead['status_name'], 'color' => $lead['status_color'])) ?>
  </div>
</div>

<div class="detail">
  <div class="stack">
    <section class="card">
      <h2>Details</h2>
      <dl class="kv">
        <dt>Email</dt><dd><a href="mailto:<?= e($lead['email']) ?>"><?= e($lead['email']) ?></a></dd>
        <dt>Phone / WhatsApp</dt><dd><?= $lead['phone'] ? '<a href="tel:' . e(preg_replace('/[^\d+]/', '', $lead['phone'])) . '">' . e($lead['phone']) . '</a>' : '-' ?></dd>
        <?php if ($isEmp): ?>
          <dt>Company</dt><dd><?= e($lead['company'] ?: '-') ?></dd>
          <dt>Positions</dt><dd><?= $lead['positions'] !== null ? (int)$lead['positions'] : '-' ?></dd>
        <?php else: ?>
          <dt>Experience</dt><dd><?= $lead['years_experience'] !== null ? (int)$lead['years_experience'] . ' years' : '-' ?></dd>
          <dt>CV</dt><dd><?= $lead['cv_path'] ? '<a href="cv.php?id=' . $id . '">' . e($lead['cv_original'] ?: 'Download') . '</a>' : '-' ?></dd>
        <?php endif; ?>
        <dt>Role</dt><dd><?= e($lead['role'] ?: '-') ?><?= $lead['role_cat'] ? ' <span class="muted">· ' . e($lead['role_cat']) . '</span>' : '' ?></dd>
        <dt><?= $isEmp ? 'Hiring for' : 'Open to work in' ?></dt><dd><?= e($lead['location'] ?: '-') ?></dd>
        <dt>Received</dt><dd><?= e(fmt_date($lead['created_at'])) ?> <span class="muted">· <?= e($lead['source']) ?></span></dd>
        <dt>Email alert</dt><dd><?= $lead['mail_sent'] ? 'Sent' : '<span style="color:var(--err)">Not sent</span>' ?></dd>
        <dt>Owner</dt><dd><?= e($lead['assignee'] ?: 'Unassigned') ?></dd>
      </dl>
      <div style="margin-top:16px">
        <label>Message</label>
        <div class="msgbox"><?= $lead['message'] ? e($lead['message']) : '<span class="muted">No message</span>' ?></div>
      </div>
    </section>

    <?php if ($prev): ?>
    <section class="card">
      <h2>Earlier enquiries from this email</h2>
      <ul>
        <?php foreach ($prev as $p): ?><li><a href="lead.php?id=<?= $p['id'] ?>"><?= e(lead_ref($p['id'])) ?></a> · <?= $p['type'] === 'employer' ? 'Employer' : 'Candidate' ?> · <?= e(fmt_date($p['created_at'], false)) ?></li><?php endforeach; ?>
      </ul>
    </section>
    <?php endif; ?>

    <section class="card">
      <h2>Activity</h2>
      <?php if (!$notes): ?><p class="muted">No activity yet.</p><?php else: ?>
      <ul class="timeline">
        <?php foreach ($notes as $n): ?>
          <li class="<?= e($n['kind']) ?>">
            <div class="meta"><?= e($n['author'] ?: 'System') ?> · <?= e(fmt_date($n['created_at'])) ?></div>
            <div class="body"><?= e($n['body']) ?></div>
          </li>
        <?php endforeach; ?>
      </ul>
      <?php endif; ?>
    </section>
  </div>

  <div class="stack">
    <form class="card stack" method="post">
      <h2>Update</h2>
      <?= csrf_field() ?><input type="hidden" name="action" value="update">
      <div class="field"><label for="u-status">Status</label>
        <select id="u-status" name="status_id">
          <?php foreach ($statuses as $s): ?><option value="<?= $s['id'] ?>"<?= (int)$s['id'] === (int)$lead['status_id'] ? ' selected' : '' ?>><?= e($s['name']) ?></option><?php endforeach; ?>
        </select></div>
      <div class="field"><label for="u-owner">Owner</label>
        <select id="u-owner" name="assigned_to"><option value="">Unassigned</option>
          <?php foreach ($team as $t): ?><option value="<?= $t['id'] ?>"<?= (int)$t['id'] === (int)$lead['assigned_to'] ? ' selected' : '' ?>><?= e($t['name']) ?></option><?php endforeach; ?>
        </select></div>
      <div class="field"><label for="u-note">Add a note</label><textarea id="u-note" name="note" placeholder="Call summary, next step, interview date..."></textarea></div>
      <button class="btn">Save</button>
    </form>

    <?php if ($user['role'] === 'admin'): ?>
    <form class="card stack" method="post">
      <h2>Delete lead</h2>
      <p class="muted" style="margin:0">Removes the lead, its notes and CV permanently. Use this for spam or when someone asks for their data to be erased.</p>
      <?= csrf_field() ?><input type="hidden" name="action" value="delete">
      <div class="field"><label for="d-confirm">Type <?= e(lead_ref($id)) ?> to confirm</label><input id="d-confirm" name="confirm" autocomplete="off"></div>
      <button class="btn danger">Delete permanently</button>
    </form>
    <?php endif; ?>
  </div>
</div>
<?php admin_foot();
