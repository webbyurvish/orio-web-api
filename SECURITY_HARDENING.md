# API Security Hardening (Defense-in-Depth)

This project has been hardened to be resilient against abuse (rate floods, brute-force, header spoofing), secret exposure, and common OWASP API risks.

## What’s implemented

### Secret & configuration security
- **No committed secrets**: `appsettings.json` and `appsettings.Development.json` were sanitized.
- **Fail-fast config**: `Security` options are validated at startup (`ValidateOnStart`).
- **JWT secret no longer has a fallback**: the application will refuse to boot if `JwtSettings:SecretKey` is missing/short.

### Reverse-proxy safety (no spoofed headers)
- `ForwardedHeaders` is configured to trust **only** explicitly configured proxies/networks:
  - `Security:TrustedProxies`
  - `Security:TrustedNetworks` (CIDR, e.g. `10.0.0.0/8`)
- **Important**: do not clear `KnownNetworks/Proxies` in production; that allows attackers to spoof `X-Forwarded-For`.

### Rate limiting & abuse protection (production-grade)
Implemented using ASP.NET Core rate limiting (token bucket + sliding window):
- **Global per-IP token bucket**: burst + sustained protection.
- **Per-user limiter** (fallbacks to IP for anonymous).
- **Endpoint-specific policies** for sensitive auth endpoints:
  - `[EnableRateLimiting("AuthSensitive")]` on login/register/verify/token exchange.
- Proper `429` responses and **`Retry-After`** header emission.
- Rate-limit rejection events are logged (without sensitive payloads).

### Concurrency / queue protections (flood resilience)
- Global concurrency limiter (`ConcurrencyGlobal`) to cap simultaneous work and prevent resource exhaustion under floods.
- Queue limits are configurable; defaults are **fail-fast** under overload.

### Request hardening
- **Request body size guard** rejects large payloads early (`413`).
- **Content-Type guard** rejects unsupported media types (`415`) for body-bearing requests.
- Safe JSON settings:
  - `MaxDepth` to mitigate deep object / JSON bomb payloads
  - disallow comments & trailing commas

### Safe errors (no stack traces / internals)
- Global exception middleware returns `application/problem+json` without internal details.
- Client disconnects are handled without noisy error logs.

### HTTP security headers
- HSTS + HTTPS enforcement in non-development.
- `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`, and a minimal `Content-Security-Policy`.
- Best-effort removal of fingerprinting headers.

### Swagger exposure controls
- Swagger is now **Development-only** by default to avoid accidental production exposure.

## Configuration reference

Add a `Security` section (example in `appsettings.json`):
- `TrustedProxies`: list of proxy IPs (e.g. your load balancer)
- `TrustedNetworks`: CIDR ranges
- `RateLimiting` and `Concurrency` knobs
- `MaxRequestBodyBytes`, `JsonMaxDepth`, request timeouts

## Remaining recommended infrastructure protections (deploy-time)

These are outside app code, but strongly recommended:
- Put the API behind a **WAF/CDN** (Azure Front Door / Cloudflare / AWS WAF).
- Enable **DDoS protection** at the edge (Azure DDoS Protection, Cloudflare DDoS).
- Set strict ingress rules: only allow traffic from the WAF to the app service / VM.
- Enable centralized logging + alerting (rate-limit spikes, auth failures, 5xx spikes).
- Use managed secret stores:
  - Azure Key Vault / AWS Secrets Manager
- Use least-privilege DB credentials and rotate keys regularly.

