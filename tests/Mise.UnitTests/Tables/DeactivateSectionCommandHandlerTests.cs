using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Tables.Application.DeactivateSection;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Tables;

public class DeactivateSectionCommandHandlerTests
{
    private readonly Mock<ISectionsData> _sectionsData = new();
    private readonly Mock<ITablesData> _tablesData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly DeactivateSectionCommandHandler _sut;

    public DeactivateSectionCommandHandlerTests()
    {
        _sut = new DeactivateSectionCommandHandler(
            _sectionsData.Object, _tablesData.Object, _auditWriter.Object, _timeProvider,
            NullLogger<DeactivateSectionCommandHandler>.Instance);
    }

    private static DeactivateSectionCommand CommandFor(Guid sectionId) => new(Guid.NewGuid(), sectionId, Guid.NewGuid());

    [Fact]
    public async Task HandleAsync_SectionDoesNotExist_ReturnsNullAndNeverChecksForActiveTables()
    {
        var command = CommandFor(Guid.NewGuid());
        _sectionsData.Setup(d => d.GetSectionByIdAsync(command.SectionId, It.IsAny<CancellationToken>())).ReturnsAsync((Section?)null);

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().BeNull();
        _tablesData.Verify(d => d.AnyActiveTablesInSectionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_SectionHasActiveTables_ThrowsFieldScopedValidationExceptionAndSkipsTheAuditWrite()
    {
        var section = Section.Create(Guid.NewGuid(), "Patio", 0);
        var command = CommandFor(section.Id);
        _sectionsData.Setup(d => d.GetSectionByIdAsync(section.Id, It.IsAny<CancellationToken>())).ReturnsAsync(section);
        _tablesData.Setup(d => d.AnyActiveTablesInSectionAsync(section.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>(
            because: "docs/plan.md correction #12 — a section with active tables must be blocked with a clear reason, same field-scoped 400 shape as RegisterStaffCommandHandler's taken-username check.");
        exception.Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(DeactivateSectionCommand.SectionId));
        section.IsActive.Should().BeTrue(because: "a rejected deactivation must not partially apply.");
        _auditWriter.Verify(a => a.Stage(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_SectionHasNoActiveTables_DeactivatesAndWritesOneAuditEntry()
    {
        var section = Section.Create(Guid.NewGuid(), "Patio", 0);
        var command = CommandFor(section.Id);
        _sectionsData.Setup(d => d.GetSectionByIdAsync(section.Id, It.IsAny<CancellationToken>())).ReturnsAsync(section);
        _tablesData.Setup(d => d.AnyActiveTablesInSectionAsync(section.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _sectionsData
            .Setup(d => d.DeactivateSectionAsync(It.IsAny<Section>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SectionMutationResult(section, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().NotBeNull();
        section.IsActive.Should().BeFalse();
        _auditWriter.Verify(
            a => a.Stage(
                It.Is<AuditLogEntry>(e => e.EntityType == "Section" && e.EntityId == section.Id && e.Action == "Deactivated")),
            Times.Once);
    }
}
