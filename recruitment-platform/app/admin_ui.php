<?php
/** NyotaCore Talent - admin page shell. */

function admin_head($title, $active = '', $user = null)
{
    $nav = array('index.php' => 'Leads', 'catalog.php' => 'Roles & locations', 'team.php' => 'Team & account');
    ?><!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex, nofollow">
<title><?= e($title) ?> · NyotaCore Talent Admin</title>
<link rel="icon" type="image/png" sizes="32x32" href="/assets/favicon/favicon-32.png">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@400;500;600;700;800&family=IBM+Plex+Mono:wght@400;500&display=swap">
<link rel="stylesheet" href="/admin/admin.css">
</head>
<body>
<?php if ($user): ?>
<header class="topbar">
  <div class="wrap topbar-in">
    <a href="index.php" class="brand"><img src="/assets/talent-lockup-white.png" alt="NyotaCore Talent" height="32"><span>Admin</span></a>
    <nav class="tabs" aria-label="Admin">
      <?php foreach ($nav as $href => $label): ?>
        <a href="<?= $href ?>"<?= $active === $href ? ' class="on" aria-current="page"' : '' ?>><?= e($label) ?></a>
      <?php endforeach; ?>
    </nav>
    <div class="who">
      <span><?= e($user['name']) ?></span>
      <form method="post" action="logout.php"><?= csrf_field() ?><button class="link">Sign out</button></form>
    </div>
  </div>
</header>
<?php endif; ?>
<main class="<?= $user ? 'wrap page' : 'bare' ?>">
<?php if ($f = flash()): ?>
  <div class="flash <?= e($f[0]) ?>" role="status"><?= e($f[1]) ?></div>
<?php endif;
}

function admin_foot()
{
    ?>
</main>
</body>
</html>
<?php
}

function status_pill(array $s)
{
    return '<span class="pill" style="--c:' . e($s['color']) . '">' . e($s['name']) . '</span>';
}
