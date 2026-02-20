using Microsoft.AspNetCore.Mvc;
using PayBridge.Contracts.Models;
using PayBridge.PaymentApi.Services;

namespace PayBridge.PaymentApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly PaymentOrchestrator _orchestrator;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(PaymentOrchestrator orchestrator, ILogger<PaymentsController> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreatePayment(
        [FromBody] CreatePaymentRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.MerchantId) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.Amount <= 0)
        {
            return BadRequest(new { Error = "Invalid payment request" });
        }

        _logger.LogInformation(
            "Payment request received: MerchantId={MerchantId}, Amount={Amount} {Currency}",
            request.MerchantId, request.Amount, request.Currency);

        var result = await _orchestrator.ProcessPaymentAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetPayment(Guid id, CancellationToken ct)
    {
        var result = await _orchestrator.GetPaymentAsync(id, ct);
        if (result == null)
            return NotFound();

        return Ok(result);
    }
}
