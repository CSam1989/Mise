using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Tables.Application.CreateSection;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Tables;

public class CreateSectionCommandHandlerTests
{
    private readonly Mock<ISectionsData> _sectionsData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly CreateSectionCommandHandler _sut;

    public CreateSectionCommandHandlerTests()
    {
        _sut = new CreateSectionCommandHandler(
            _sectionsData.Object, _auditWriter.Object, new CreateSectionCommandValidator(), _timeProvider,
            NullLogger<CreateSectionCommandHandler>.Instance);
    }

    private static CreateSectionCommand ValidCommand() => new(Guid.NewGuid(), "Patio", 1, Guid.NewGuid());

    [Fact]
    public async Task HandleAsync_NameEmpty_ThrowsValidationExceptionAndNeverTouchesTheGatewayOrAuditWriter()
    {
        var command = ValidCommand() with { Name = "" };

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _sectionsData.Verify(
            d => d.CreateSectionAsync(It.IsAny<Section>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditWriter.Verify(a => a.Stage(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_PersistsViaGatewayAndWritesOneAuditEntry()
    {
        var command = ValidCommand();
        var section = Section.Create(Guid.NewGuid(), command.Name, command.DisplayOrder);
        Section? passedSection = null;
        _sectionsData
            .Setup(d => d.CreateSectionAsync(It.IsAny<Section>(), command.OperationId, It.IsAny<CancellationToken>()))
            .Callback<Section, Guid, CancellationToken>((s, _, _) => passedSection = s)
            .ReturnsAsync(new SectionMutationResult(section, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().Be(section.Id);
        _auditWriter.Verify(
            a => a.Stage(
                It.Is<AuditLogEntry>(e =>
                    e.EntityType == "Section" && e.EntityId == passedSection!.Id && e.Action == "Created"
                    && e.PerformedByStaffId == command.PerformedByStaffId && e.OccurredAtUtc == _timeProvider.GetUtcNow())),
            Times.Once,
            "EntityId is the handler's own client-generated Section.Id, staged before the gateway call — not the " +
            "mocked gateway result's (unrelated) section object.");
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_ReturnsExistingIdAndStagesAuditEntryRegardless()
    {
        var command = ValidCommand();
        var existingSection = Section.Create(Guid.NewGuid(), "Existing", 0);
        _sectionsData
            .Setup(d => d.CreateSectionAsync(It.IsAny<Section>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SectionMutationResult(existingSection, WasAlreadyProcessed: true));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().Be(existingSection.Id);
        _auditWriter.Verify(
            a => a.Stage(It.IsAny<AuditLogEntry>()),
            Times.Once,
            "Stage is called unconditionally before the gateway call — see CreateReservationCommandHandlerTests' " +
            "identical replay test for the full rationale.");
    }
}
