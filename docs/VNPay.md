# VNPay payment flow

Configure secrets with User Secrets or environment variables:

```powershell
dotnet user-secrets set "VnPay:TmnCode" "YOUR_TMN_CODE" --project LegacyVault.API
dotnet user-secrets set "VnPay:HashSecret" "YOUR_HASH_SECRET" --project LegacyVault.API
dotnet user-secrets set "VnPay:PaymentUrl" "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html" --project LegacyVault.API
dotnet user-secrets set "VnPay:ReturnUrl" "https://your-public-api/api/payments/vnpay/return" --project LegacyVault.API
```

VNPay must be configured to send its server-to-server IPN to `https://your-public-api/api/payments/vnpay/ipn`. The return URL is for the customer's browser; the IPN endpoint is the authoritative server notification. Both callbacks verify the VNPay HMAC-SHA512 signature and amount before changing transaction state. Use the production payment URL and production merchant credentials for live payments.

## Start a payment

Clients can list active plans through `GET /api/payments/plans`. Authenticated users can inspect their active subscription at `GET /api/payments/me/subscription` and their paged transaction history at `GET /api/payments/me/transactions?page=1&pageSize=20&status=Success`. They then call `POST /api/payments/vnpay/checkout` with a plan ID:

```json
{ "planId": 1 }
```

The response contains `transactionCode`, `amount`, and `paymentUrl`. Redirect the browser to `paymentUrl`. The plan must be active and priced as a positive whole number of VND. Starting checkout creates a pending transaction and pending subscription. Successful callbacks activate it and expire the user's previous active subscription; failed callbacks mark both records failed. Repeated callbacks are idempotent.

The browser return endpoint returns `{ transactionCode, status, amount }`. The frontend should display the payment result and refresh account/subscription state from its normal API. Do not treat the browser redirect alone as proof of payment; VNPay's IPN updates the same transaction.

## Admin endpoints

An Admin can list all plans with `GET /api/admin/subscription-plans`, create with `POST`, edit with `PUT /api/admin/subscription-plans/{planId}`, and stop selling a plan with `DELETE /api/admin/subscription-plans/{planId}`. The delete operation is a soft deactivation, preserving subscriptions and transaction history. Admin transaction records can be searched with `GET /api/admin/payment-transactions?page=1&pageSize=20&userId=1&status=Pending` and inspected with `GET /api/admin/payment-transactions/{transactionId}`. Transaction status is gateway controlled and is not manually editable through the API.
