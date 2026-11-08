<?php
/** Roles and locations shown in the public form: add, rename, hide, reorder. */
require dirname(__DIR__) . '/app/auth.php';
require NCT_ROOT . '/app/admin_ui.php';
$user = require_login();
$pdo = db();

function slugify($s)
{
    $s = strtolower(trim(preg_replace('/[^A-Za-z0-9]+/', '-', $s), '-'));
    return $s !== '' ? substr($s, 0, 70) : 'item';
}
function unique_slug(PDO $pdo, $table, $base)
{
    $slug = $base; $i = 2;
    $st = $pdo->prepare("SELECT 1 FROM $table WHERE slug = ?");
    while (true) { $st->execute(array($slug)); if (!$st->fetchColumn()) { return $slug; } $slug = $base . '-' . $i++; }
}

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    csrf_check();
    if ($user['role'] !== 'admin') { flash('Only admins can change roles and locations.', 'err'); redirect('catalog.php'); }
    $a = $_POST['action'] ?? '';
    $id = (int)($_POST['id'] ?? 0);
    if ($a === 'add_role') {
        $name = post('name', 120); $cat = (int)($_POST['category_id'] ?? 0);
        if ($name === '' || !$cat) { flash('Enter a role name and pick a category.', 'err'); }
        else {
            $max = (int)$pdo->query('SELECT COALESCE(MAX(sort_order),0) FROM roles WHERE category_id = ' . $cat)->fetchColumn();
            $pdo->prepare('INSERT INTO roles (category_id, slug, name, description, sort_order) VALUES (?, ?, ?, ?, ?)')
                ->execute(array($cat, unique_slug($pdo, 'roles', slugify($name)), $name, post('description', 255) ?: null, $max + 1));
            flash('Role "' . $name . '" added. It now shows in the form.');
        }
    } elseif ($a === 'save_role' && $id) {
        $pdo->prepare('UPDATE roles SET name = ?, category_id = ?, sort_order = ?, is_active = ? WHERE id = ?')
            ->execute(array(post('name', 120) ?: 'Untitled', (int)$_POST['category_id'], (int)$_POST['sort_order'], isset($_POST['is_active']) ? 1 : 0, $id));
        flash('Role saved.');
    } elseif ($a === 'add_location') {
        $name = post('name', 80); $market = in_array($_POST['market'] ?? '', array('local', 'international', 'remote', 'other'), true) ? $_POST['market'] : 'international';
        if ($name === '') { flash('Enter a location name.', 'err'); }
        else {
            $max = (int)$pdo->query("SELECT COALESCE(MAX(sort_order),0) FROM locations WHERE slug <> 'other'")->fetchColumn();
            $pdo->prepare('INSERT INTO locations (slug, name, market, sort_order) VALUES (?, ?, ?, ?)')
                ->execute(array(unique_slug($pdo, 'locations', slugify($name)), $name, $market, $max + 1));
            flash('Location "' . $name . '" added.');
        }
    } elseif ($a === 'save_location' && $id) {
        $market = in_array($_POST['market'] ?? '', array('local', 'international', 'remote', 'other'), true) ? $_POST['market'] : 'international';
        $pdo->prepare('UPDATE locations SET name = ?, market = ?, sort_order = ?, is_active = ? WHERE id = ?')
            ->execute(array(post('name', 80) ?: 'Untitled', $market, (int)$_POST['sort_order'], isset($_POST['is_active']) ? 1 : 0, $id));
        flash('Location saved.');
    }
    redirect('catalog.php');
}

$cats  = $pdo->query('SELECT * FROM role_categories ORDER BY sort_order')->fetchAll();
$roles = $pdo->query('SELECT r.*, (SELECT COUNT(*) FROM leads WHERE role_id = r.id) AS used FROM roles r
    JOIN role_categories c ON c.id = r.category_id ORDER BY c.sort_order, r.sort_order, r.name')->fetchAll();
$locs  = $pdo->query('SELECT l.*, (SELECT COUNT(*) FROM leads WHERE location_id = l.id) AS used FROM locations l ORDER BY sort_order, name')->fetchAll();
$admin = $user['role'] === 'admin';
$dis = $admin ? '' : ' disabled';

admin_head('Roles & locations', 'catalog.php', $user);
?>
<div class="head-row"><h1>Roles &amp; locations</h1></div>
<p class="muted" style="margin:0">These lists fill the Role and Location menus on the public form. Hidden items stay on existing leads but disappear from the form.<?= $admin ? '' : ' Only admins can make changes.' ?></p>

<?php foreach ($cats as $c): ?>
<section class="card">
  <h2><?= e($c['name']) ?></h2>
  <div class="table-wrap" style="border:0">
  <table>
    <thead><tr><th>Name</th><th>Category</th><th>Order</th><th>In form</th><th>Leads</th><th></th></tr></thead>
    <tbody>
    <?php foreach ($roles as $r): if ((int)$r['category_id'] !== (int)$c['id']) continue; ?>
      <?php $fid = 'fr' . $r['id']; ?>
      <tr>
        <td style="min-width:220px"><input form="<?= $fid ?>" name="name" value="<?= e($r['name']) ?>" aria-label="Role name"<?= $dis ?>></td>
        <td><select form="<?= $fid ?>" name="category_id" aria-label="Category"<?= $dis ?>><?php foreach ($cats as $cc): ?><option value="<?= $cc['id'] ?>"<?= $cc['id'] == $r['category_id'] ? ' selected' : '' ?>><?= e($cc['name']) ?></option><?php endforeach; ?></select></td>
        <td style="width:90px"><input form="<?= $fid ?>" name="sort_order" type="number" value="<?= (int)$r['sort_order'] ?>" aria-label="Order"<?= $dis ?>></td>
        <td><input form="<?= $fid ?>" type="checkbox" name="is_active" style="width:auto"<?= $r['is_active'] ? ' checked' : '' ?> aria-label="Show in form"<?= $dis ?>></td>
        <td class="num"><?= (int)$r['used'] ?></td>
        <td><?php if ($admin): ?><form id="<?= $fid ?>" method="post"><?= csrf_field() ?><input type="hidden" name="action" value="save_role"><input type="hidden" name="id" value="<?= $r['id'] ?>"><button class="btn ghost sm">Save</button></form><?php endif; ?></td>
      </tr>
    <?php endforeach; ?>
    </tbody>
  </table>
  </div>
</section>
<?php endforeach; ?>

<?php if ($admin): ?>
<form class="card" method="post">
  <h2>Add a role</h2>
  <?= csrf_field() ?><input type="hidden" name="action" value="add_role">
  <div class="row">
    <div class="field"><label for="r-name">Role name</label><input id="r-name" name="name" required placeholder="e.g. Plumber"></div>
    <div class="field"><label for="r-cat">Category</label><select id="r-cat" name="category_id"><?php foreach ($cats as $c): ?><option value="<?= $c['id'] ?>"><?= e($c['name']) ?></option><?php endforeach; ?></select></div>
    <div class="field" style="flex:2"><label for="r-desc">Short description (optional)</label><input id="r-desc" name="description"></div>
    <button class="btn">Add role</button>
  </div>
</form>
<?php endif; ?>

<section class="card">
  <h2>Locations</h2>
  <div class="table-wrap" style="border:0">
  <table>
    <thead><tr><th>Name</th><th>Market</th><th>Order</th><th>In form</th><th>Leads</th><th></th></tr></thead>
    <tbody>
    <?php foreach ($locs as $l): ?>
      <?php $fid = 'fl' . $l['id']; ?>
      <tr>
        <td style="min-width:200px"><input form="<?= $fid ?>" name="name" value="<?= e($l['name']) ?>" aria-label="Location name"<?= $dis ?>></td>
        <td><select form="<?= $fid ?>" name="market" aria-label="Market"<?= $dis ?>><?php foreach (array('local' => 'Local (Nigeria)', 'international' => 'International', 'remote' => 'Remote', 'other' => 'Other') as $k => $v): ?><option value="<?= $k ?>"<?= $l['market'] === $k ? ' selected' : '' ?>><?= $v ?></option><?php endforeach; ?></select></td>
        <td style="width:90px"><input form="<?= $fid ?>" name="sort_order" type="number" value="<?= (int)$l['sort_order'] ?>" aria-label="Order"<?= $dis ?>></td>
        <td><input form="<?= $fid ?>" type="checkbox" name="is_active" style="width:auto"<?= $l['is_active'] ? ' checked' : '' ?> aria-label="Show in form"<?= $dis ?>></td>
        <td class="num"><?= (int)$l['used'] ?></td>
        <td><?php if ($admin): ?><form id="<?= $fid ?>" method="post"><?= csrf_field() ?><input type="hidden" name="action" value="save_location"><input type="hidden" name="id" value="<?= $l['id'] ?>"><button class="btn ghost sm">Save</button></form><?php endif; ?></td>
      </tr>
    <?php endforeach; ?>
    </tbody>
  </table>
  </div>
  <?php if ($admin): ?>
  <form method="post" class="row" style="margin-top:16px">
    <?= csrf_field() ?><input type="hidden" name="action" value="add_location">
    <div class="field"><label for="l-name">New location</label><input id="l-name" name="name" required placeholder="e.g. Ireland"></div>
    <div class="field"><label for="l-market">Market</label><select id="l-market" name="market"><option value="international">International</option><option value="local">Local (Nigeria)</option><option value="remote">Remote</option><option value="other">Other</option></select></div>
    <button class="btn">Add location</button>
  </form>
  <?php endif; ?>
</section>
<?php admin_foot();
