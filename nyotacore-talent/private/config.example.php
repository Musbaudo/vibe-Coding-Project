<?php
// NyotaCore Talent - private settings. This folder is blocked from the web by private/.htaccess.
return array(
    // ---- Site ----
    'app_url'      => 'https://talent.nyotacore.com.ng',
    'timezone'     => 'Africa/Lagos',

    // ---- MySQL (cPanel > MySQL Databases) ----
    // cPanel prefixes names with your account name, e.g. nyotaco_talent
    'db_host'      => 'localhost',
    'db_name'      => 'CPANELUSER_talent',
    'db_user'      => 'CPANELUSER_talent',
    'db_pass'      => 'CHANGE_ME',

    // ---- SMTP (cPanel > Email Accounts) ----
    'smtp_host'    => 'mail.nyotacore.com.ng',
    'smtp_port'    => 465,                         // SSL/TLS
    'smtp_user'    => 'talent@nyotacore.com.ng',
    'smtp_pass'    => 'CHANGE_ME',
    'from_email'   => 'talent@nyotacore.com.ng',   // must match smtp_user on cPanel
    'from_name'    => 'NyotaCore Talent',
    'to_email'     => 'talent@nyotacore.com.ng',   // inbox that receives new-lead alerts

    // ---- Limits and storage ----
    'max_per_hour' => 5,                           // form submissions per visitor IP per hour
    'cv_max_mb'    => 5,
    // Kept one level above the web root so they can never be downloaded directly
    'uploads_dir'  => dirname(__DIR__, 2) . '/nyotacore-talent-uploads',
    'csv_log'      => dirname(__DIR__, 2) . '/nyotacore-talent-leads.csv',
);
