using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using PayBridge.Contracts.Models;
using PayBridge.PaymentApi.Services;
using PayBridge.PaymentApi.Telemetry;

namespace PayBridge.PaymentApi.Controllers;

[ApiController]
[Route("[controller]")]
public class WebhooksController : ControllerBase
{
    private readonly PaymentOrchestrator _orchestrator;
    private readonly ILogger<WebhooksController> _logger;

    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public WebhooksController(PaymentOrchestrator orchestrator, ILogger<WebhooksController> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    [HttpPost("provider")]
    public async Task<IActionResult> ProviderWebhook(
        [FromBody] WebhookPayload payload,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Webhook received: TxId={ProviderTxId}, Status={Status}",
            payload.ProviderTransactionId, payload.Status);

        if (!payload.Metadata.TryGetValue("paybridge_payment_id", out var paymentIdStr))
        {
            _logger.LogWarning("Webhook missing paybridge_payment_id in Metadata");
            return BadRequest("Missing paybridge_payment_id");
        }

        // Real-world: third-party providers (e.g. Stripe) do not send traceparent.
        // We persist it when we create the payment; restore from DB so the webhook span
        // links to the original payment trace in Tempo.
        ActivityContext? parentContext = null;
        if (Guid.TryParse(paymentIdStr, out var paymentId))
        {
            var storedTraceParent = await _orchestrator.GetStoredTraceParentAsync(paymentId, ct);
            if (!string.IsNullOrWhiteSpace(storedTraceParent))
            {
                var carrier = new Dictionary<string, string> { ["traceparent"] = storedTraceParent };
                var ctx = Propagator.Extract(default, carrier,
                    (dict, key) =>
                        dict.TryGetValue(key, out var value)
                            ? new[] { value }
                            : Enumerable.Empty<string>());
                parentContext = ctx.ActivityContext;
            }
        }

        if (parentContext.HasValue)
        {
            using var activity = DiagnosticConfig.ActivitySource.StartActivity(
                "ProcessWebhook",
                ActivityKind.Consumer,
                parentContext: parentContext.Value);
            activity?.SetTag("payment.provider_tx_id", payload.ProviderTransactionId);
            activity?.SetTag("webhook.status", payload.Status);
            activity?.SetTag("payment.id", paymentIdStr);
        }

        await _orchestrator.HandleWebhookAsync(
            payload.ProviderTransactionId, payload.Status, paymentIdStr, ct);

        return Ok();
    }
}
