<?php
/**
 * Contact form handler for Nyota Foundations.
 * Sends messages via SMTP to hello@mission.nyotacore.com.ng
 */

use PHPMailer\PHPMailer\PHPMailer;
use PHPMailer\PHPMailer\SMTP;
use PHPMailer\PHPMailer\Exception;

require __DIR__ . '/phpmailer/src/Exception.php';
require __DIR__ . '/phpmailer/src/PHPMailer.php';
require __DIR__ . '/phpmailer/src/SMTP.php';

// ---- Settings (private/config.php, not in the repo) ----
$cfgFile = __DIR__ . '/private/config.php';
if (!is_file($cfgFile)) { http_response_code(500); exit('Missing private/config.php - copy private/config.example.php and fill in your settings.'); }
$cfg = require $cfgFile;
$SMTP_HOST = $cfg['smtp_host'];
$SMTP_PORT = (int)$cfg['smtp_port'];
$SMTP_USER = $cfg['smtp_user'];
$SMTP_PASS = $cfg['smtp_pass'];
$MAIL_TO   = $cfg['mail_to'];
$SMTP_SEC  = isset($cfg['smtp_secure']) ? $cfg['smtp_secure'] : '';

function back($status, $msg) {
    header('Location: /contact.html?sent=' . $status . '&msg=' . rawurlencode($msg));
    exit;
}

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    back('error', 'Please use the contact form.');
}

$fname   = trim($_POST['fname']   ?? '');
$lname   = trim($_POST['lname']   ?? '');
$email   = trim($_POST['email']   ?? '');
$topic   = trim($_POST['topic']   ?? 'General enquiry');
$message = trim($_POST['message'] ?? '');

if ($fname === '' || $email === '' || $message === '' || !filter_var($email, FILTER_VALIDATE_EMAIL)) {
    back('error', 'Please complete all required fields with a valid email.');
}

$mail = new PHPMailer(true);
try {
    $mail->isSMTP();
    $mail->Host       = $SMTP_HOST;
    $mail->Port       = $SMTP_PORT;
    $mail->SMTPAuth   = true;
    $mail->Username   = $SMTP_USER;
    $mail->Password   = $SMTP_PASS;
    $mail->SMTPSecure = $SMTP_SEC;
    $mail->SMTPAutoTLS = ($SMTP_SEC !== '');

    $mail->setFrom($SMTP_USER, 'Nyota Foundations Website');
    $mail->addAddress($MAIL_TO);
    $mail->addReplyTo($email, "$fname $lname");

    $mail->Subject = 'Website enquiry: ' . $topic;
    $mail->Body =
        "New message from the Nyota Foundations website\n\n" .
        "Name:  $fname $lname\n" .
        "Email: $email\n" .
        "Topic: $topic\n\n" .
        "Message:\n$message\n";

    $mail->send();
    back('ok', 'Thank you. Your message has been sent.');
} catch (Exception $e) {
    back('error', 'Sorry, the message could not be sent right now. Please email us directly.');
}
