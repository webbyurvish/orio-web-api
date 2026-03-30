using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PKeetDashboard.API.DTOs;
using PKeetDashboard.API.Options;
using PKeetDashboard.API.Services;
using Stripe;
using Stripe.Checkout;

namespace PKeetDashboard.API.Controllers;

[ApiController]
[Route("api/payments/stripe")]
[Authorize]
public class StripePaymentsController : ControllerBase
{
    private readonly StripeOptions _stripe;
    private readonly ILogger<StripePaymentsController> _logger;

    public StripePaymentsController(
        IOptions<StripeOptions> stripe,
        ILogger<StripePaymentsController> logger)
    {
        _stripe = stripe.Value;
        _logger = logger;
    }

    /// <summary>Whether checkout uses INR (enables UPI on Stripe Checkout for eligible accounts).</summary>
    [HttpGet("checkout-options")]
    [ProducesResponseType(typeof(StripeCheckoutOptionsResponse), 200)]
    public ActionResult<StripeCheckoutOptionsResponse> GetCheckoutOptions()
    {
        var useInr = UseInrCheckout(_stripe);
        return Ok(new StripeCheckoutOptionsResponse
        {
            CheckoutCurrency = useInr ? "INR" : "USD",
            UpiEnabled = useInr,
        });
    }

    /// <summary>Creates a Stripe Checkout Session and returns the hosted payment URL.</summary>
    [HttpPost("create-checkout-session")]
    [ProducesResponseType(typeof(CreateStripeCheckoutResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(503)]
    public async Task<ActionResult<CreateStripeCheckoutResponse>> CreateCheckoutSession(
        [FromBody] CreateStripeCheckoutRequest body,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_stripe.SecretKey))
        {
            return StatusCode(503, new
            {
                message = "Stripe is not configured. Add Stripe:SecretKey (sk_test_…) to appsettings.",
            });
        }

        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out _))
            return Unauthorized();

        var productId = body.ProductId?.Trim() ?? "";
        var item = StripePaymentCatalog.TryGet(productId);
        if (item == null)
            return BadRequest(new { message = "Unknown product." });

        var tab = string.IsNullOrWhiteSpace(body.BillingTab) ? "credits" : body.BillingTab.Trim();
        if (tab is not ("credits" or "subscription" or "lifetime"))
            tab = "credits";

        var baseUrl = _stripe.DashboardBaseUrl.TrimEnd('/');
        var successUrl =
            $"{baseUrl}/dashboard/buyCredits?tab={Uri.EscapeDataString(tab)}&payment=success&session_id={{CHECKOUT_SESSION_ID}}";
        var cancelUrl =
            $"{baseUrl}/dashboard/buyCredits?tab={Uri.EscapeDataString(tab)}&payment=cancelled";

        StripeConfiguration.ApiKey = _stripe.SecretKey;

        var useInr = UseInrCheckout(_stripe);
        var sessionService = new SessionService();
        Session session;

        try
        {
            if (item.PriceMode == StripeCheckoutPriceMode.OneTime)
            {
                var options = new SessionCreateOptions
                {
                    Mode = "payment",
                    SuccessUrl = successUrl,
                    CancelUrl = cancelUrl,
                    ClientReferenceId = userIdStr,
                    Metadata = new Dictionary<string, string>
                    {
                        ["user_id"] = userIdStr,
                        ["product_id"] = item.Id,
                    },
                    LineItems = new List<SessionLineItemOptions>
                    {
                        BuildLineItem(item, useInr, recurringInterval: null),
                    },
                };
                ApplyPaymentMethodTypes(options, useInr);
                session = await sessionService.CreateAsync(options, requestOptions: null, cancellationToken: ct);
            }
            else
            {
                var interval = item.PriceMode == StripeCheckoutPriceMode.SubscriptionMonthly
                    ? "month"
                    : "year";
                var options = new SessionCreateOptions
                {
                    Mode = "subscription",
                    SuccessUrl = successUrl,
                    CancelUrl = cancelUrl,
                    ClientReferenceId = userIdStr,
                    Metadata = new Dictionary<string, string>
                    {
                        ["user_id"] = userIdStr,
                        ["product_id"] = item.Id,
                    },
                    LineItems = new List<SessionLineItemOptions>
                    {
                        BuildLineItem(item, useInr, recurringInterval: interval),
                    },
                };
                ApplyPaymentMethodTypes(options, useInr);
                session = await sessionService.CreateAsync(options, requestOptions: null, cancellationToken: ct);
            }
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe Checkout session creation failed");
            return BadRequest(new { message = ex.StripeError?.Message ?? "Stripe could not start checkout." });
        }

        if (string.IsNullOrEmpty(session.Url))
            return BadRequest(new { message = "Stripe did not return a checkout URL." });

        return Ok(new CreateStripeCheckoutResponse { Url = session.Url });
    }

    /// <summary>
    /// Confirms payment after redirect. Call once per completed session; the SPA should de-duplicate with sessionStorage.
    /// </summary>
    [HttpGet("verify-session")]
    [ProducesResponseType(typeof(VerifyStripeSessionResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(503)]
    public async Task<ActionResult<VerifyStripeSessionResponse>> VerifySession(
        [FromQuery] string? sessionId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_stripe.SecretKey))
        {
            return StatusCode(503, new
            {
                message = "Stripe is not configured.",
            });
        }

        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { message = "sessionId is required." });

        StripeConfiguration.ApiKey = _stripe.SecretKey;
        var sessionService = new SessionService();
        Session session;
        try
        {
            session = await sessionService.GetAsync(sessionId, requestOptions: null, cancellationToken: ct);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe session retrieve failed");
            return BadRequest(new { message = ex.StripeError?.Message ?? "Invalid session." });
        }

        if (!string.Equals(session.ClientReferenceId, userIdStr, StringComparison.Ordinal))
        {
            if (!session.Metadata.TryGetValue("user_id", out var metaUser) ||
                !string.Equals(metaUser, userIdStr, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "This payment does not belong to the signed-in user." });
            }
        }

        var paid = string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase);
        var complete = string.Equals(session.Status, "complete", StringComparison.OrdinalIgnoreCase);
        if (!paid || !complete)
        {
            return Ok(new VerifyStripeSessionResponse
            {
                Paid = false,
                ProductId = null,
                SessionId = session.Id,
            });
        }

        session.Metadata.TryGetValue("product_id", out var productId);
        if (string.IsNullOrEmpty(productId) || StripePaymentCatalog.TryGet(productId) == null)
        {
            _logger.LogWarning("Paid session {SessionId} missing valid product_id metadata", session.Id);
            return BadRequest(new { message = "Session metadata is invalid." });
        }

        return Ok(new VerifyStripeSessionResponse
        {
            Paid = true,
            ProductId = productId,
            SessionId = session.Id,
        });
    }

    private static bool UseInrCheckout(StripeOptions o) =>
        string.Equals(o.CheckoutCurrency?.Trim(), "inr", StringComparison.OrdinalIgnoreCase);

    /// <summary>UPI is only valid with INR; we also keep cards for international cards on INR pricing where supported.</summary>
    private static void ApplyPaymentMethodTypes(SessionCreateOptions options, bool useInr)
    {
        if (useInr)
            options.PaymentMethodTypes = new List<string> { "card", "upi" };
    }

    private static SessionLineItemOptions BuildLineItem(
        StripeCatalogItem item,
        bool useInr,
        string? recurringInterval)
    {
        var currency = useInr ? "inr" : "usd";
        var amount = useInr ? item.UnitAmountInrPaise : item.UnitAmountUsdCents;

        var priceData = new SessionLineItemPriceDataOptions
        {
            Currency = currency,
            UnitAmount = amount,
            ProductData = new SessionLineItemPriceDataProductDataOptions
            {
                Name = item.Name,
                Description = item.Description,
            },
        };

        if (!string.IsNullOrEmpty(recurringInterval))
        {
            priceData.Recurring = new SessionLineItemPriceDataRecurringOptions
            {
                Interval = recurringInterval,
            };
        }

        return new SessionLineItemOptions
        {
            Quantity = 1,
            PriceData = priceData,
        };
    }
}
