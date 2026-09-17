using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mise.Modules.Tables.Application.Ports;
using Mise.Modules.Tables.Application.UpdateSection;
using Mise.Modules.Tables.Domain;
using Mise.SharedKernel.Infrastructure;
using Moq;

namespace Mise.UnitTests.Tables;

public class UpdateSectionCommandHandlerTests
{
    private readonly Mock<ISectionsData> _sectionsData = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-15T18:00:00+02:00"));
    private readonly UpdateSectionCommandHandler _sut;

    public UpdateSectionCommandHandlerTests()
    {
        _sut = new UpdateSectionCommandHandler(
            _sectionsData.Object, _auditWriter.Object, new UpdateSectionCommandValidator(), _timeProvider,
            NullLogger<UpdateSectionCommandHandler>.Instance);
    }

    private static UpdateSectionCommand ValidCommand(Guid sectionId) => new(Guid.NewGuid(), sectionId, "Main Room", 2, Guid.NewGuid());

    [Fact]
    public async Task HandleAsync_NameEmpty_ThrowsValidationExceptionAndNeverTouchesTheGatewayOrAuditWriter()
    {
        var command = ValidCommand(Guid.NewGuid()) with { Name = "" };

        var act = () => _sut.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _sectionsData.Verify(d => d.GetSectionByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_SectionDoesNotExist_ReturnsNullAndNeverTouchesTheAuditWriter()
    {
        var command = ValidCommand(Guid.NewGuid());
        _sectionsData.Setup(d => d.GetSectionByIdAsync(command.SectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Section?)null);

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().BeNull();
        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_UpdatesViaGatewayAndWritesOneAuditEntry()
    {
        var section = Section.Create(Guid.NewGuid(), "Patio", 0);
        var command = ValidCommand(section.Id);
        _sectionsData.Setup(d => d.GetSectionByIdAsync(section.Id, It.IsAny<CancellationToken>())).ReturnsAsync(section);
        _sectionsData
            .Setup(d => d.UpdateSectionAsync(It.IsAny<Section>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SectionMutationResult(section, WasAlreadyProcessed: false));

        var result = await _sut.HandleAsync(command, CancellationToken.None);

        result.Should().NotBeNull();
        section.Name.Should().Be("Main Room", because: "the handler must call UpdateDetails on the loaded aggregate before persisting.");
        _auditWriter.Verify(
            a => a.WriteAsync(
                It.Is<AuditLogEntry>(e => e.EntityType == "Section" && e.EntityId == section.Id && e.Action == "Updated"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_OperationIdAlreadyProcessed_SkipsTheAuditWrite()
    {
        var section = Section.Create(Guid.NewGuid(), "Patio", 0);
        var command = ValidCommand(section.Id);
        _sectionsData.Setup(d => d.GetSectionByIdAsync(section.Id, It.IsAny<CancellationToken>())).ReturnsAsync(section);
        _sectionsData
            .Setup(d => d.UpdateSectionAsync(It.IsAny<Section>(), command.OperationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SectionMutationResult(section, WasAlreadyProcessed: true));

        await _sut.HandleAsync(command, CancellationToken.None);

        _auditWriter.Verify(a => a.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
