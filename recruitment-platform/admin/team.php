<?php
/** Your password, and (for admins) the team who can sign in. */
require dirname(__DIR__) . '/app/auth.php';
require NCT_ROOT . '/app/admin_ui.php';
$user = require_login();
$pdo = db();
$isAdmin = $user['role'] === 'admin';

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    csrf_check();
    $a = $_POST['action'] ?? '';

    if ($a === 'password') {
        $st = $pdo->prepare('SELECT password_hash FROM users WHERE id = ?');
        $st->execute(array($user['id']));
        $new = (string)($_POST['new'] ?? '');
        if (!password_verify((string)($_POST['current'] ?? ''), $st->fetchColumn())) { flash('Your current password is not correct.', 'err'); }
        elseif ($p = password_problem($new)) { flash($p, 'err'); }
        elseif ($new !== (string)($_POST['new2'] ?? '')) { flash('The two new passwords do not match.', 'err'); }
        else {
            $pdo->prepare('UPDATE users SET password_hash = ? WHERE id = ?')->execute(array(password_hash($new, PASSWORD_DEFAULT), $user['id']));
            session_regenerate_id(true);
            flash('Password changed.');
        }
        redirect('team.php');
    }

    if (!$isAdmin) { flash('Only admins can manage the team.', 'err'); redirect('team.php'); }

    if ($a === 'add') {
        $name = post('name', 120); $email = strtolower(post('email', 190)); $pw = (string)($_POST['password'] ?? '');
        $role = ($_POST['role'] ?? '') === 'admin' ? 'admin' : 'recruiter';
        if ($name === '' || !filter_var($email, FILTER_VALIDATE_EMAIL)) { flash('Enter a name and a valid email.', 'err'); }
        elseif ($p = password_problem($pw)) { flash($p, 'err'); }
        else {
            $st = $pdo->prepare('SELECT 1 FROM users WHERE email = ?'); $st->execute(array($email));
            if ($st->fetchColumn()) { flash('Someone already uses that email.', 'err'); }
            else {
                $pdo->prepare('INSERT INTO users (name, email, password_hash, role) VALUES (?, ?, ?, ?)')
                    ->execute(array($name, $email, password_hash($pw, PASSWORD_DEFAULT), $role));
                flash($name . ' can now sign in. Share the password with them privately and ask them to change it.');
            }
        }
    } elseif ($a === 'toggle') {
        $id = (int)($_POST['id'] ?? 0);
        if ($id === (int)$user['id']) { flash('You cannot deactivate your own account.', 'err'); }
        else {
            $pdo->prepare('UPDATE users SET is_active = 1 - is_active WHERE id = ?')->execute(array($id));
            flash('Account updated.');
        }
    } elseif ($a === 'reset') {
        $id = (int)($_POST['id'] ?? 0); $pw = (string)($_POST['password'] ?? '');
        if ($p = password_problem($pw)) { flash($p, 'err'); }
        else {
            $pdo->prepare('UPDATE users SET password_hash = ? WHERE id = ?')->execute(array(password_hash($pw, PASSWORD_DEFAULT), $id));
            flash('Password reset. Share it privately.');
        }
    }
    redirect('team.php');
}

$users = $pdo->query('SELECT id, name, email, role, is_active, last_login_at FROM users ORDER BY is_active DESC, name')->fetchAll();
admin_head('Team & account', 'team.php', $user);
?>
<div class="head-row"><h1>Team &amp; account</h1></div>

<form class="card" method="post" autocomplete="off">
  <h2>Change your password</h2>
  <?= csrf_field() ?><input type="hidden" name="action" value="password">
  <div class="row">
    <div class="field"><label for="p-cur">Current password</label><input id="p-cur" name="current" type="password" required autocomplete="current-password"></div>
    <div class="field"><label for="p-new">New password (10+)</label><input id="p-new" name="new" type="password" minlength="10" required autocomplete="new-password"></div>
    <div class="field"><label for="p-new2">Repeat new password</label><input id="p-new2" name="new2" type="password" minlength="10" required autocomplete="new-password"></div>
    <button class="btn">Change password</button>
  </div>
</form>

<?php if ($isAdmin): ?>
<section class="card">
  <h2>People who can sign in</h2>
  <div class="table-wrap" style="border:0">
  <table>
    <thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Last sign-in</th><th>Status</th><th>Reset password</th><th></th></tr></thead>
    <tbody>
    <?php foreach ($users as $u): ?>
      <tr>
        <td><?= e($u['name']) ?><?= (int)$u['id'] === (int)$user['id'] ? ' <span class="muted">(you)</span>' : '' ?></td>
        <td><?= e($u['email']) ?></td>
        <td><?= $u['role'] === 'admin' ? 'Admin' : 'Recruiter' ?></td>
        <td class="num"><?= $u['last_login_at'] ? e(fmt_date($u['last_login_at'])) : '<span class="muted">Never</span>' ?></td>
        <td><?= $u['is_active'] ? 'Active' : '<span class="muted">Deactivated</span>' ?></td>
        <td>
          <?php if ((int)$u['id'] !== (int)$user['id']): ?>
          <form method="post" class="row" style="flex-wrap:nowrap" autocomplete="off">
            <?= csrf_field() ?><input type="hidden" name="action" value="reset"><input type="hidden" name="id" value="<?= $u['id'] ?>">
            <input name="password" type="password" minlength="10" placeholder="New password" aria-label="New password for <?= e($u['name']) ?>" autocomplete="new-password" style="min-width:150px">
            <button class="btn ghost sm">Set</button>
          </form>
          <?php endif; ?>
        </td>
        <td>
          <?php if ((int)$u['id'] !== (int)$user['id']): ?>
          <form method="post"><?= csrf_field() ?><input type="hidden" name="action" value="toggle"><input type="hidden" name="id" value="<?= $u['id'] ?>">
            <button class="btn <?= $u['is_active'] ? 'danger' : 'ghost' ?> sm"><?= $u['is_active'] ? 'Deactivate' : 'Reactivate' ?></button></form>
          <?php endif; ?>
        </td>
      </tr>
    <?php endforeach; ?>
    </tbody>
  </table>
  </div>
</section>

<form class="card" method="post" autocomplete="off">
  <h2>Add a team member</h2>
  <p class="muted" style="margin-top:0">Recruiters can work leads. Admins can also manage roles, locations, the team and delete leads.</p>
  <?= csrf_field() ?><input type="hidden" name="action" value="add">
  <div class="row">
    <div class="field"><label for="t-name">Name</label><input id="t-name" name="name" required></div>
    <div class="field"><label for="t-email">Email</label><input id="t-email" name="email" type="email" required></div>
    <div class="field"><label for="t-pw">Temporary password (10+)</label><input id="t-pw" name="password" type="password" minlength="10" required autocomplete="new-password"></div>
    <div class="field"><label for="t-role">Role</label><select id="t-role" name="role"><option value="recruiter">Recruiter</option><option value="admin">Admin</option></select></div>
    <button class="btn">Add member</button>
  </div>
</form>
<?php endif; ?>
<?php admin_foot();
