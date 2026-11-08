<?php
// Nyota Foundations - private settings.
// Copy this file to private/config.php and fill in your own values.
// private/ is blocked from the web by private/.htaccess and is never committed.
return array(
    // ---- SMTP (contact form) ----
    'smtp_host'   => 'mail.example.com',
    'smtp_port'   => 587,
    'smtp_secure' => 'tls',          // 'tls', 'ssl' or '' for none
    'smtp_user'   => 'you@example.com',
    'smtp_pass'   => 'CHANGE_ME',
    'mail_to'     => 'hello@mission.nyotacore.com.ng',

    // ---- Paystack (donations) ----
    // Secret key: server-side only, never in HTML/JS.
    'paystack_secret_key' => 'sk_test_CHANGE_ME',
);
