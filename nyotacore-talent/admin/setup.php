<?php
/** First-run setup: create the first admin account. Only works while no users exist. */
require dirname(__DIR__) . '/app/auth.php';
require NCT_ROOT . '/app/admin_ui.php';

$pdo = db_or_null();
if (!$pdo) {
    http_response_code(503);
    exit('The database is not reachable. Check db_name, db_user and db_pass in private/config.php.');
}
if (admin_count($pdo) > 0) { redirect('login.php'); }

$err = null;
$name = ''; $email = '';
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    csrf_check();
    $name  = post('name', 120);
    $email = strtolower(post('email', 190));
    $pw    = (string)($_POST['password'] ?? '');
    $pw2   = (string)($_POST['password2'] ?? '');
    if ($name === '' || !filter_var($email, FILTER_VALIDATE_EMAIL)) { $err = 'Enter your name and a valid email address.'; }
    elseif ($p = password_problem($pw)) { $err = $p; }
    elseif ($pw !== $pw2) { $err = 'The two passwords do not match.'; }
    else {
        $pdo->prepare("INSERT INTO users (name, email, password_hash, role) VALUES (?, ?, ?, 'admin')")
            ->execute(array($name, $email, password_hash($pw, PASSWORD_DEFAULT)));
        $st = $pdo->prepare('SELECT id, name, email, role FROM users WHERE email = ?');
        $st->execute(array($email));
        sign_in($st->fetch());
        flash('Welcome, ' . $name . '. Your admin account is ready.');
        redirect('index.php');
    }
}
admin_head('Set up');
?>
<div class="auth">
  <form class="card" method="post" autocomplete="off">
    <img src="/assets/talent-lockup.png" alt="NyotaCore Talent">
    <div>
      <h1>Create the admin account</h1>
      <p class="muted">The database is installed and seeded. Create the first admin to sign in. This page closes once an account exists.</p>
    </div>
    <?php if ($err): ?><div class="flash err" role="alert"><?= e($err) ?></div><?php endif; ?>
    <?= csrf_field() ?>
    <div class="field"><label for="s-name">Full name</label><input id="s-name" name="name" value="<?= e($name) ?>" required></div>
    <div class="field"><label for="s-email">Email</label><input id="s-email" name="email" type="email" value="<?= e($email) ?>" required></div>
    <div class="field"><label for="s-pw">Password (10+ characters)</label><input id="s-pw" name="password" type="password" minlength="10" required autocomplete="new-password"></div>
    <div class="field"><label for="s-pw2">Repeat password</label><input id="s-pw2" name="password2" type="password" minlength="10" required autocomplete="new-password"></div>
    <button class="btn">Create admin and sign in</button>
  </form>
</div>
<?php admin_foot();
