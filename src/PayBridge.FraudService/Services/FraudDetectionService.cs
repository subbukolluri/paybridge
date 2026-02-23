using Grpc.Core;
using PayBridge.FraudDetection;
using PayBridge.Logging;

namespace PayBridge.FraudService.Services;

public class FraudDetectionService : FraudDetectionSvc.FraudDetectionSvcBase
{
    private readonly IAppLogger _appLogger;
    private static readonly Random Rng = new();

    public FraudDetectionService(IAppLogger appLogger)
    {
        _appLogger = appLogger;
    }

    public override async Task<FraudCheckResponse> CheckTransaction(
        FraudCheckRequest request, ServerCallContext context)
    {
        _appLogger.LogInformation(
            "Fraud check requested: PaymentId={PaymentId}, Amount={Amount} {Currency}",
            null, request.PaymentId, request.Amount, request.Currency);

        await Task.Delay(Rng.Next(50, 200), context.CancellationToken);

        var riskScore = Rng.NextDouble();
        var approved = riskScore < 0.85;
        var reason = approved ? "Low risk" : "High risk score detected";

        _appLogger.LogInformation(
            "Fraud check result: PaymentId={PaymentId}, Approved={Approved}, RiskScore={RiskScore:F2}",
            null, request.PaymentId, approved, riskScore);

        return new FraudCheckResponse
        {
            Approved = approved,
            RiskScore = riskScore,
            Reason = reason
        };
    }
}
