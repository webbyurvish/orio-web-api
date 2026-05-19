using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.DTOs;
using PKeetDashboard.API.Options;
using PKeetDashboard.API.Services;

namespace PKeetDashboard.API.Controllers;

[ApiController]
[Route("api/payments/razorpay")]
public class RazorpayPaymentsController : ControllerBase
{
    private readonly RazorpayApiClient _razorpay;
    private readonly RazorpayOptions _options;
    private readonly PaymentFulfillmentService _fulfillment;
    private readonly AppDbContext _db;
    private readonly ILogger<RazorpayPaymentsController> _logger;

    public RazorpayPaymentsController(
        RazorpayApiClient razorpay,
        IOptions<RazorpayOptions> options,
        PaymentFulfillmentService fulfillment,
        AppDbContext db,
        ILogger<RazorpayPaymentsController> logger)
    {
        _razorpay = razorpay;
        _options = options.Value;
        _fulfillment = fulfillment;
        _db = db;
        _logger = logger;
    }

    [HttpGet("checkout-options")]
    [Authorize]
    [ProducesResponseType(typeof(RazorpayCheckoutOptionsResponse), 200)]
    public ActionResult<RazorpayCheckoutOptionsResponse> GetCheckoutOptions()
    {
        if (!_razorpay.IsConfigured)
        {
            return StatusCode(503, new { message = "Razorpay is not configured. Add Razorpay:KeyId and Razorpay:KeySecret." });
        }

        return Ok(new RazorpayCheckoutOptionsResponse
        {
            KeyId = _razorpay.KeyId,
            Currency = "INR",
            TestMode = _razorpay.IsTestMode,
        });
    }

    [HttpPost("create-order")]
    [Authorize]
    [ProducesResponseType(typeof(CreateRazorpayOrderResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(503)]
    public async Task<ActionResult<CreateRazorpayOrderResponse>> CreateOrder(
        [FromBody] CreateRazorpayOrderRequest body,
        CancellationToken ct)
    {
        if (!_razorpay.IsConfigured)
        {
            return StatusCode(503, new
            {
                message = "Razorpay is not configured. Add Razorpay:KeyId (rzp_test_…) and Razorpay:KeySecret to appsettings.",
            });
        }

        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var productId = body.ProductId?.Trim() ?? "";
        var item = PaymentCatalog.TryGet(productId);
        if (item == null)
            return BadRequest(new { message = "Unknown product." });

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return Unauthorized();

        var receipt = BuildRazorpayReceipt(userId);
        var notes = new Dictionary<string, string>
        {
            ["user_id"] = userIdStr,
            ["product_id"] = item.Id,
        };

        try
        {
            var order = await _razorpay.CreateOrderAsync(item.UnitAmountInrPaise, receipt, notes, ct);
            return Ok(new CreateRazorpayOrderResponse
            {
                KeyId = _razorpay.KeyId,
                OrderId = order.Id,
                Amount = order.Amount,
                Currency = order.Currency,
                Name = "Smeed AI",
                Description = item.Description,
                PrefillEmail = user.Email,
                PrefillName = $"{user.FirstName} {user.LastName}".Trim(),
                TestMode = _razorpay.IsTestMode,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Razorpay order creation failed");
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("verify-payment")]
    [Authorize]
    [ProducesResponseType(typeof(VerifyRazorpayPaymentResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(503)]
    public async Task<ActionResult<VerifyRazorpayPaymentResponse>> VerifyPayment(
        [FromBody] VerifyRazorpayPaymentRequest body,
        CancellationToken ct)
    {
        if (!_razorpay.IsConfigured)
            return StatusCode(503, new { message = "Razorpay is not configured." });

        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var orderId = body.RazorpayOrderId?.Trim() ?? "";
        var paymentId = body.RazorpayPaymentId?.Trim() ?? "";
        var signature = body.RazorpaySignature?.Trim() ?? "";

        if (string.IsNullOrEmpty(orderId) || string.IsNullOrEmpty(paymentId) || string.IsNullOrEmpty(signature))
            return BadRequest(new { message = "razorpayOrderId, razorpayPaymentId, and razorpaySignature are required." });

        if (!RazorpaySignature.VerifyPayment(orderId, paymentId, signature, _options.KeySecret))
            return BadRequest(new { message = "Payment signature verification failed." });

        RazorpayPaymentDto? payment;
        try
        {
            payment = await _razorpay.GetPaymentAsync(paymentId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Razorpay payment fetch failed");
            return BadRequest(new { message = "Could not confirm payment with Razorpay." });
        }

        if (payment == null || !string.Equals(payment.OrderId, orderId, StringComparison.Ordinal))
            return BadRequest(new { message = "Payment does not match order." });

        if (!string.Equals(payment.Status, "captured", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(payment.Status, "authorized", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new VerifyRazorpayPaymentResponse
            {
                Paid = false,
                OrderId = orderId,
                PaymentId = paymentId,
            });
        }

        RazorpayOrderDto order;
        try
        {
            order = await _razorpay.GetOrderAsync(orderId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Razorpay order fetch failed");
            return BadRequest(new { message = "Could not load order from Razorpay." });
        }

        if (order.Notes == null ||
            !order.Notes.TryGetValue("user_id", out var noteUserId) ||
            !string.Equals(noteUserId, userIdStr, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "This payment does not belong to the signed-in user." });
        }

        if (!order.Notes.TryGetValue("product_id", out var productId) || string.IsNullOrWhiteSpace(productId))
            return BadRequest(new { message = "Order metadata is invalid." });

        var item = PaymentCatalog.TryGet(productId);
        if (item == null)
            return BadRequest(new { message = "Unknown product on order." });

        await _fulfillment.ApplyPaidOrderAsync(userId, orderId, paymentId, item, ct);

        var receipt = await _db.PaymentReceipts.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RazorpayOrderId == orderId, ct);

        var creditsBalance = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.CallCredits)
            .FirstOrDefaultAsync(ct);

        return Ok(new VerifyRazorpayPaymentResponse
        {
            Paid = true,
            ProductId = item.Id,
            OrderId = orderId,
            PaymentId = paymentId,
            CreditsApplied = receipt?.CreditsApplied ?? 0m,
            CreditsBalance = creditsBalance,
        });
    }

    /// <summary>Razorpay receipt must be ≤40 characters. User/product live in order notes.</summary>
    private static string BuildRazorpayReceipt(Guid userId)
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var shortUser = userId.ToString("N")[..8];
        var receipt = $"sm_{shortUser}_{stamp}";
        return receipt.Length <= 40 ? receipt : receipt[..40];
    }
}
