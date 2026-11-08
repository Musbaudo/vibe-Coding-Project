<?php
/**
 * Paystack payment verification callback.
 * URL: https://mission.nyotacore.com.ng/payment/online/callback?reference=XXXX
 *
 * The SECRET key lives here (server-side) ONLY. Never expose it in HTML/JS.
 */

$cfgFile = dirname(__DIR__, 3) . '/private/config.php';
if (!is_file($cfgFile)) { http_response_code(500); exit('Missing private/config.php - copy private/config.example.php and fill in your settings.'); }
$cfg = require $cfgFile;
$PAYSTACK_SECRET_KEY = $cfg['paystack_secret_key'];

$reference = isset($_GET['reference']) ? trim($_GET['reference']) : '';
$status = 'error';
$message = 'No payment reference was provided.';
$amount = 0;
$customer_email = '';

if ($reference !== '') {
    $ch = curl_init("https://api.paystack.co/transaction/verify/" . rawurlencode($reference));
    curl_setopt_array($ch, [
        CURLOPT_RETURNTRANSFER => true,
        CURLOPT_HTTPHEADER => [
            "Authorization: Bearer " . $PAYSTACK_SECRET_KEY,
            "Cache-Control: no-cache",
        ],
        CURLOPT_TIMEOUT => 30,
    ]);
    $res = curl_exec($ch);
    $err = curl_error($ch);
    curl_close($ch);

    if ($err) {
        $message = 'Could not reach the payment provider. Please contact us with your reference: ' . htmlspecialchars($reference);
    } else {
        $data = json_decode($res, true);
        if (!empty($data['status']) && !empty($data['data']) && $data['data']['status'] === 'success') {
            $status = 'success';
            $amount = $data['data']['amount'] / 100; // kobo -> naira
            $customer_email = isset($data['data']['customer']['email']) ? $data['data']['customer']['email'] : '';
            $message = 'Your donation was received. Thank you.';
            // OPTIONAL: record the donation to a file/database or email your team here.
        } else {
            $status = 'failed';
            $message = 'This payment could not be verified. If you were charged, contact us with reference: ' . htmlspecialchars($reference);
        }
    }
}
?><!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Donation status — Nyota Foundations</title>
<link rel="icon" type="image/svg+xml" href="/favicon.svg">
<link href="https://fonts.googleapis.com/css2?family=Fraunces:opsz,wght@9..144,600&family=Inter:wght@400;600&display=swap" rel="stylesheet">
<style>
  body{margin:0;font-family:Inter,sans-serif;background:#f7f2e7;color:#221f1a;display:grid;place-items:center;min-height:100vh;padding:1.5rem}
  .card{background:#fff;border:1px solid rgba(34,31,26,.12);border-radius:18px;max-width:520px;padding:2.6rem;text-align:center;box-shadow:0 18px 45px -20px rgba(20,67,47,.45)}
  .badge{width:74px;height:74px;border-radius:50%;display:grid;place-items:center;margin:0 auto 1.2rem;font-size:2rem}
  .ok{background:#e6f4ec;color:#14432f}
  .bad{background:#fbeae4;color:#bc5f3c}
  h1{font-family:Fraunces,serif;font-size:1.8rem;margin:0 0 .5rem;color:#14432f}
  p{color:#5c584f;margin:0 0 1.4rem}
  .amt{font-family:Fraunces,serif;font-size:2.4rem;color:#d8a63a;margin:.4rem 0}
  a.btn{display:inline-block;background:#d8a63a;color:#14432f;font-weight:600;padding:.8rem 1.7rem;border-radius:999px;text-decoration:none}
</style>
</head>
<body>
  <div class="card">
    <?php if ($status === 'success'): ?>
      <div class="badge ok">&#10003;</div>
      <h1>Thank you for your gift</h1>
      <div class="amt">&#8358;<?php echo number_format($amount); ?></div>
      <p>Your donation to Nyota Foundations was received successfully<?php echo $customer_email ? ', and a receipt will be sent to ' . htmlspecialchars($customer_email) : ''; ?>. You are helping shelter a displaced family.</p>
    <?php else: ?>
      <div class="badge bad">&#33;</div>
      <h1><?php echo $status === 'failed' ? 'Payment not verified' : 'Something went wrong'; ?></h1>
      <p><?php echo htmlspecialchars($message); ?></p>
    <?php endif; ?>
    <a class="btn" href="/index.html">Back to home</a>
  </div>
</body>
</html>
