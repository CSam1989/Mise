using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.StaffIdentity.Application.Login;
using Mise.Modules.StaffIdentity.Application.Ports;
using Mise.Modules.StaffIdentity.Domain;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.StaffIdentity;

public class LoginCommandHandlerTests
{
    private readonly Mock<IStaffIdentityData> _staffIdentityData = new();
    private readonly Mock<IJwtTokenIssuer> _jwtTokenIssuer = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly LoginCommandHandler _sut;

    public LoginCommandHandlerTests()
    {
        _sut = new LoginCommandHandler(
            _staffIdentityData.Object,
            _jwtTokenIssuer.Object,
            _auditWriter.Object,
            new LoginCommandValidator(),
            _timeProvider,
            NullLogger<LoginCommandHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_EmptyUsername_ThrowsValidationExceptionAndNeverTouchesTheGatewayOrIssuer()
    {
        var act = () => _sut.HandleAsync(new LoginCommand("", "correct-horse"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _staffIdentityData.Verify(
            d => d.ValidateCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _jwtTokenIssuer.Verify(j => j.IssueToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<StaffRole>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCredentials_IssuesTokenAndWritesOneSignedInAuditEntry()
    {
        var staffUserId = Guid.NewGuid();
        _staffIdentityData
            .Setup(d => d.ValidateCredentialsAsync("jdoe", "correct-horse", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidateCredentialsResult(ValidateCredentialsOutcome.Success, staffUserId, "Jane Doe", StaffRole.Manager));
        _jwtTokenIssuer
            .Setup(j => j.IssueToken(staffUserId, "Jane Doe", StaffRole.Manager))
            .Returns(new IssuedToken("test-token", _timeProvider.GetUtcNow().AddHours(8)));

        var result = await _sut.HandleAsync(new LoginCommand("jdoe", "correct-horse"), CancellationToken.None);

        result.Outcome.Should().Be(LoginOutcome.Success);
        result.StaffUserId.Should().Be(staffUserId);
        result.Token.Should().Be("test-token");
        _auditWriter.Verify(
            a => a.WriteAsync(
                It.Is<AuditLogEntry>(e =>
                    e.EntityType == "StaffUser" && e.EntityId == staffUserId && e.Action == "SignedIn"
                    && e.PerformedByStaffId == staffUserId && e.OccurredAtUtc == _timeProvider.GetUtcNow()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(ValidateCredentialsOutcome.NotFound)]
    [InlineData(ValidateCredentialsOutcome.InvalidPassword)]
    public async Task HandleAsync_UnknownUsernameOrWrongPassword_ReturnsInvalidCredentialsAndNeverIssuesATokenOrAudits(
        ValidateCredentialsOutcome gatewayOutcome)
    {
        _staffIdentityData
            .Setup(d => d.ValidateCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidateCredentialsResult(gatewayOutcome, Guid.Empty, string.Empty, default));

        var result = await _sut.HandleAsync(new LoginCommand("jdoe", "wrong"), CancellationToken.None);

        result.Outcome.Should().Be(LoginOutcome.InvalidCredentials,
            because: "unknown username and wrong password must be indistinguishable to the caller — never reveal which one it was.");
        _jwtTokenIssuer.Verify(j => j.IssueToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<StaffRole>()), Times.Never);
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_InactiveAccount_ReturnsAccountInactiveAndNeverIssuesATokenOrAudits()
    {
        _staffIdentityData
            .Setup(d => d.ValidateCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidateCredentialsResult(ValidateCredentialsOutcome.Inactive, Guid.Empty, string.Empty, default));

        var result = await _sut.HandleAsync(new LoginCommand("jdoe", "correct-horse"), CancellationToken.None);

        result.Outcome.Should().Be(LoginOutcome.AccountInactive);
        _jwtTokenIssuer.Verify(j => j.IssueToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<StaffRole>()), Times.Never);
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
