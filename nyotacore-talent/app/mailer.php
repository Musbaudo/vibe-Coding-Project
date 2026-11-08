<?php
/** NyotaCore Talent - SMTP mail through the cPanel mailbox (PHPMailer). */

use PHPMailer\PHPMailer\PHPMailer;
use PHPMailer\PHPMailer\Exception as MailException;

require_once NCT_ROOT . '/lib/PHPMailer/Exception.php';
require_once NCT_ROOT . '/lib/PHPMailer/PHPMailer.php';
require_once NCT_ROOT . '/lib/PHPMailer/SMTP.php';

/**
 * Send a plain-text email. Returns true on success.
 * $opts: reply_to => [email, name], attach => [path, filename]
 */
function send_mail($to, $subject, $body, array $opts = array())
{
    $mail = new PHPMailer(true);
    try {
        $mail->isSMTP();
        $mail->Host       = cfg('smtp_host');
        $mail->Port       = (int)cfg('smtp_port', 465);
        $mail->SMTPAuth   = true;
        $mail->Username   = cfg('smtp_user');
        $mail->Password   = cfg('smtp_pass');
        $mail->SMTPSecure = ((int)cfg('smtp_port', 465) === 465) ? PHPMailer::ENCRYPTION_SMTPS : PHPMailer::ENCRYPTION_STARTTLS;
        $mail->Timeout    = 15;
        $mail->CharSet    = 'UTF-8';
        $mail->setFrom(cfg('from_email'), cfg('from_name', 'NyotaCore Talent'));
        $mail->addAddress($to);
        if (!empty($opts['reply_to'])) { $mail->addReplyTo($opts['reply_to'][0], $opts['reply_to'][1] ?? ''); }
        if (!empty($opts['attach']) && is_file($opts['attach'][0])) { $mail->addAttachment($opts['attach'][0], $opts['attach'][1]); }
        $mail->Subject = $subject;
        $mail->Body    = $body;
        $mail->send();
        return true;
    } catch (MailException $e) {
        error_log('NyotaCore Talent SMTP error: ' . $mail->ErrorInfo);
        return false;
    }
}
