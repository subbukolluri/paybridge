using FluentAssertions;
using PayBridge.Contracts.Enums;
using PayBridge.PaymentApi.Domain;

namespace PayBridge.Tests.Domain;

public class PaymentStateMachineTests
{
    [Theory]
    [InlineData(PaymentStatus.Created, PaymentStatus.FraudChecking, true)]
    [InlineData(PaymentStatus.FraudChecking, PaymentStatus.Submitted, true)]
    [InlineData(PaymentStatus.FraudChecking, PaymentStatus.Failed, true)]
    [InlineData(PaymentStatus.Submitted, PaymentStatus.Completed, true)]
    [InlineData(PaymentStatus.Submitted, PaymentStatus.Failed, true)]
    [InlineData(PaymentStatus.Completed, PaymentStatus.Refunded, true)]
    [InlineData(PaymentStatus.Created, PaymentStatus.Completed, false)]
    [InlineData(PaymentStatus.Created, PaymentStatus.Failed, false)]
    [InlineData(PaymentStatus.Failed, PaymentStatus.Completed, false)]
    [InlineData(PaymentStatus.Failed, PaymentStatus.Submitted, false)]
    [InlineData(PaymentStatus.Refunded, PaymentStatus.Completed, false)]
    [InlineData(PaymentStatus.Completed, PaymentStatus.Submitted, false)]
    public void IsValidTransition_ShouldReturnExpectedResult(
        PaymentStatus from, PaymentStatus to, bool expected)
    {
        PaymentStateMachine.IsValidTransition(from, to).Should().Be(expected);
    }

    [Theory]
    [InlineData(PaymentStatus.Failed, true)]
    [InlineData(PaymentStatus.Refunded, true)]
    [InlineData(PaymentStatus.Created, false)]
    [InlineData(PaymentStatus.FraudChecking, false)]
    [InlineData(PaymentStatus.Submitted, false)]
    [InlineData(PaymentStatus.Completed, false)]
    public void IsTerminal_ShouldReturnExpectedResult(PaymentStatus status, bool expected)
    {
        PaymentStateMachine.IsTerminal(status).Should().Be(expected);
    }

    [Fact]
    public void Payment_TransitionTo_ValidTransition_ShouldSucceed()
    {
        var payment = new Payment { Status = PaymentStatus.Created };

        payment.TransitionTo(PaymentStatus.FraudChecking);

        payment.Status.Should().Be(PaymentStatus.FraudChecking);
    }

    [Fact]
    public void Payment_TransitionTo_InvalidTransition_ShouldThrow()
    {
        var payment = new Payment { Status = PaymentStatus.Created };

        var act = () => payment.TransitionTo(PaymentStatus.Completed);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Invalid state transition*");
    }

    [Fact]
    public void Payment_TransitionTo_TerminalState_ShouldSetCompletedAt()
    {
        var payment = new Payment
        {
            Status = PaymentStatus.Submitted,
            CompletedAt = null
        };

        payment.TransitionTo(PaymentStatus.Completed);

        payment.CompletedAt.Should().NotBeNull();
        payment.CompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Payment_TransitionTo_NonTerminalState_ShouldNotSetCompletedAt()
    {
        var payment = new Payment
        {
            Status = PaymentStatus.Created,
            CompletedAt = null
        };

        payment.TransitionTo(PaymentStatus.FraudChecking);

        payment.CompletedAt.Should().BeNull();
    }
}
