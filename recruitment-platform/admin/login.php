<?php
/** Admin sign-in with per-IP lockout after repeated failures. */
require dirname(__DIR__) . '/app/auth.php';
require NCT_ROOT . '/app/admin_ui.php';

$pdo = db_or_null();
if (!$pdo) {
    http_response_code(503);
    exit('The database is not reachable. Check db_name, db_user and db_pass in private/config.php.');
}
if (admin_count($pdo) === 0) { redirect('setup.php'); }
if (current_user()) { redirect('index.php'); }

$next = safe_next($_GET['next'] ?? ($_POST['next'] ?? ''));
$err = null; $email = '';
$ip = client_ip();

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    csrf_check();
    $email = strtolower(post('email', 190));
    $pw    = (string)($_POST['password'] ?? '');
    if (login_blocked($pdo, $ip)) {
        $err = 'Too many failed attempts. Wait ' . NCT_LOGIN_WINDOW . ' minutes and try again.';
    } else {
        $st = $pdo->prepare('SELECT id, name, email, role, password_hash FROM users WHERE email = ? AND is_active = 1');
        $st->execute(array($email));
        $u = $st->fetch();
        if ($u && password_verify($pw, $u['password_hash'])) {
            if (password_needs_rehash($u['password_hash'], PASSWORD_DEFAULT)) {
                $pdo->prepare('UPDATE users SET password_hash = ? WHERE id = ?')->execute(array(password_hash($pw, PASSWORD_DEFAULT), $u['id']));
            }
            record_login($pdo, $ip, $email, true);
            sign_in($u);
            redirect($next);
        }
        record_login($pdo, $ip, $email, false);
        $err = 'That email and password do not match an active account.';
    }
}
admin_head('Sign in');
?>
<div class="auth">
  <form class="card" method="post">
    <img src="/assets/talent-lockup.png" alt="NyotaCore Talent">
    <h1>Sign in to the admin</h1>
    <?php if ($err): ?><div class="flash err" role="alert"><?= e($err) ?></div><?php endif; ?>
    <?= csrf_field() ?>
    <input type="hidden" name="next" value="<?= e($next) ?>">
    <div class="field"><label for="l-email">Email</label><input id="l-email" name="email" type="email" value="<?= e($email) ?>" required autocomplete="username"></div>
    <div class="field"><label for="l-pw">Password</label><input id="l-pw" name="password" type="password" required autocomplete="current-password"></div>
    <button class="btn">Sign in</button>
  </form>
</div>
<?php admin_foot();
