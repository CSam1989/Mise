using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.StaffIdentity.Application.Ports;
using Mise.Modules.StaffIdentity.Application.RegisterStaff;
using Mise.Modules.StaffIdentity.Domain;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.StaffIdentity;

public class RegisterStaffCommandHandlerTests
{
    private readonly Mock<IStaffIdentityData> _staffIdentityData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly RegisterStaffCommandHandler _sut;

    public RegisterStaffCommandHandlerTests()
    {
        _sut = new RegisterStaffCommandHandler(
            _staffIdentityData.Object,
            _auditWriter.Object,
            new RegisterStaffCommandValidator(),
            _timeProvider,
            NullLogger<RegisterStaffCommandHandler>.Instance);
    }

    private static RegisterStaffCommand ValidCommand() =>
        new(Guid.NewGuid(), "jdoe", "correct-horse", "Jane Doe", StaffRole.FloorStaff, Guid.NewGuid());

    [Fact]
    public async Task HandleAsync_InvalidCommand_ThrowsValidationExceptionAndNeverTouchesTheGatewayOrAuditWriter()
    {
        var command = ValidCommand() with { Username = "" };

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>(
            because: "the handler validates before touching the gateway (CLAUDE.md's handler test contract).");
        _staffIdentityData.Verify(
            d => d.RegisterStaffAsync(It.IsAny<StaffUser>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_PersistsViaGatewayAndWritesOneAuditEntry()
    {
        var command = ValidCommand();
        var staffUserId = Guid.NewGuid();
        _staffIdentityData
            .Setup(d => d.RegisterStaffAsync(It.IsAny<StaffUser>(), command.Username, command.Password, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterStaffResult(RegisterStaffOutcome.Created, staffUserId));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().Be(staffUserId);
        _auditWriter.Verify(
            a => a.WriteAsync(
                It.Is<AuditLogEntry>(e =>
                    e.EntityType == "StaffUser"
                    && e.EntityId == staffUserId
                    && e.Action == "Created"
                    && e.PerformedByStaffId == command.PerformedByStaffId
                    && e.OccurredAtUtc == _timeProvider.GetUtcNow()),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the audit entry's timestamp must come from the injected TimeProvider, never the wall clock.");
    }

    [Fact]
    public async Task HandleAsync_UsernameAlreadyTaken_ThrowsFieldScopedValidationExceptionAndSkipsTheAuditWrite()
    {
        var command = ValidCommand();
        _staffIdentityData
            .Setup(d => d.RegisterStaffAsync(It.IsAny<StaffUser>(), command.Username, command.Password, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterStaffResult(RegisterStaffOutcome.UsernameTaken, Guid.Empty));

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>(
            because: "a taken username is an expected, field-scoped 400 (same shape validation failures use), not a 500.");
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(RegisterStaffCommand.Username));
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_ReturnsExistingIdAndSkipsTheAuditWrite()
    {
        var command = ValidCommand();
        var existingStaffUserId = Guid.NewGuid();
        _staffIdentityData
            .Setup(d => d.RegisterStaffAsync(It.IsAny<StaffUser>(), command.Username, command.Password, command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterStaffResult(RegisterStaffOutcome.AlreadyProcessed, existingStaffUserId));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().Be(existingStaffUserId,
            because: "a replayed OperationId returns the id of the staff user it originally created (docs/plan.md's OperationId replay contract).");
        _auditWriter.Verify(
            a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "replaying an already-processed operation must not write a second audit entry for the same effect.");
    }
}
