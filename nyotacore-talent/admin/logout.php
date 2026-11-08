<?php
require dirname(__DIR__) . '/app/auth.php';
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    csrf_check();
    $_SESSION = array();
    session_destroy();
}
redirect('login.php');
