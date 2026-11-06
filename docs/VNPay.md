# VNPay payment flow

Configure secrets with User Secrets or environment variables:

```powershell
dotnet user-secrets set "VnPay:TmnCode" "YOUR_TMN_CODE" --project LegacyVault.API
dotnet user-secrets set "VnPay:HashSecret" "YOUR_HASH_SECRET" --project LegacyVault.API
dotnet user-secrets set "VnPay:PaymentUrl" "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html" --project LegacyVault.API
dotnet user-secrets set "VnPay:ReturnUrl" "https://vault-owner-be.huydevops.id.vn/api/payments/vnpay/return" --project LegacyVault.API
dotnet user-secrets set "VnPay:IpnUrl" "https://vault-owner-be.huydevops.id.vn/api/payments/vnpay/ipn" --project LegacyVault.API
```

Equivalent environment variables are `VnPay__TmnCode`, `VnPay__HashSecret`, `VnPay__PaymentUrl`, `VnPay__ReturnUrl`, and `VnPay__IpnUrl`. `LegacyVault.API/appsettings.json` includes a non-secret sandbox template; keep the merchant code and hash secret in User Secrets or environment variables. `PaymentUrl` defaults to the sandbox endpoint in development. Set the live endpoint and live credentials when going to production.

For the deployed backend, configure VNPay to send its server-to-server IPN to `https://vault-owner-be.huydevops.id.vn/api/payments/vnpay/ipn`. The configured Return URL is `https://vault-owner-be.huydevops.id.vn/api/payments/vnpay/return`. The return URL is for the customer's browser; it verifies the signature and displays the persisted transaction state without settling it. The IPN endpoint verifies the VNPay HMAC-SHA512 signature, merchant code, order and amount, then is the only callback that changes transaction/subscription state. Return URL timestamps and payment expiration are generated in GMT+7 as required by VNPay.

## Start a payment

Clients can list active plans through `GET /api/payments/plans`. Authenticated users can inspect their active subscription at `GET /api/payments/me/subscription` (which returns `subscription`, `scheduledSubscription`, `upgradeWindowEndsAt`, `renewalWindowStartsAt`, `daysLeft`, and `isReadOnly`).

Before initiating checkout, Frontend can call `GET /api/payments/quote?planId={planId}` to get real-time price quotation, change type (`Upgrade`, `Renew`, `Scheduled`, `New`), eligibility status (`allowed`), and reason if blocked (`reasonCode`).

To start checkout, call `POST /api/payments/vnpay/checkout` with a plan ID:

```json
{ "planId": 1 }
```

The server automatically computes `amountDue` based on subscription lifecycle rules (server-side, never trusting client amounts):
- In upgrade window (`now <= start + 3 days`): difference `amountDue = newPrice - oldPrice`. The previous active plan becomes `Replaced` upon payment.
- In renewal window (`end - 5 days <= now < end`): full price, creating a `Scheduled` subscription queued to activate at `currentPlan.EndDate`.
- During locked period (`start + 3 days < now < end - 5 days`): rejected with `409 Conflict`.
- When expired (`now >= end`) or no active paid subscription: full price, activating immediately.

For full rules and error codes, refer to [Quy-tac-doi-goi-va-het-han-subscription.md](Quy-tac-doi-goi-va-het-han-subscription.md).

The response contains `transactionCode`, `amount`, and `paymentUrl`. Redirect the browser to `paymentUrl`. If amount is 0 (or selecting free plan), it activates directly without contacting VNPay. Starting checkout creates a pending transaction and pending subscription. A successful IPN activates the subscription (or schedules it if in renewal window); a failed IPN marks both records failed. Repeated IPN callbacks are idempotent.

The browser return endpoint returns `{ transactionCode, status, amount, gatewayResponseCode }`. The status reflects the server's persisted transaction state, so it can still be `Pending` if the browser return arrives before the IPN. The frontend should show a pending state and refresh `GET /api/payments/me/transactions` or `GET /api/payments/me/subscription`; do not treat the browser redirect alone as proof of payment.

IPN responds with VNPay's `RspCode` and `Message` JSON fields: `00` when updated, `02` when already confirmed, `01` when the order is unknown, `04` for amount mismatch, `97` for invalid signature, and `99` for other invalid data or processing errors. If the database is unavailable, the IPN endpoint responds `99` so VNPay can retry.

## Admin endpoints

An Admin can list all plans with `GET /api/admin/subscription-plans`, create with `POST`, edit with `PUT /api/admin/subscription-plans/{planId}`, and stop selling a plan with `DELETE /api/admin/subscription-plans/{planId}` (soft deactivation, `IsActive = false`). Discontinued plans (`IsActive == false` with no active or scheduled subscribers) can be permanently deleted via `DELETE /api/admin/subscription-plans/{planId}/permanent`, `DELETE /api/admin/subscription-plans/{planId}?permanent=true`, or in bulk via `DELETE /api/admin/subscription-plans/inactive` while preserving historical `PAYMENT_TRANSACTIONS`. Admin transaction records can be searched with `GET /api/admin/payment-transactions?page=1&pageSize=20&userId=1&status=Pending` and inspected with `GET /api/admin/payment-transactions/{transactionId}`. Transaction status is gateway controlled and is not manually editable through the API.
