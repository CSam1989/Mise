using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mise.UI.Abstractions;
using Mise.UI.Components;
using Moq;

namespace Mise.Client.UnitTests.Reservations;

public class ReservationFormTests : BunitContext
{
    public ReservationFormTests() =>
        Services.AddSingleton<ILogger<ReservationForm>>(NullLogger<ReservationForm>.Instance);

    [Fact]
    public void Submit_PartySizeEmpty_ShowsInlineErrorAndNeverCallsTheClient()
    {
        var client = new Mock<IReservationsClient>();
        var cut = Render<ReservationForm>(parameters => parameters
            .Add(p => p.ReservationsClient, client.Object));

        cut.Find("[data-testid=input-customer-name]").Input("Jane Doe");
        cut.Find("[data-testid=input-customer-phone]").Input("+32 470 00 00 00");
        cut.Find("[data-testid=btn-submit-reservation]").Click();

        cut.Find("[data-testid=error-party-size]").TextContent.Should().Be(
            "Party size is required and must be more than 0.",
            because: "the mockup's exact copy is what the UI shows — mirrors CreateReservationCommandValidator's message.");
        client.Verify(
            c => c.CreateAsync(It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Submit_ValidInput_InvokesClientOnceAndRaisesOnCreated()
    {
        var reservationDto = new ReservationDto(Guid.NewGuid(), "Jane Doe", 4, DateTimeOffset.UtcNow, "Confirmed");
        var client = new Mock<IReservationsClient>();
        client
            .Setup(c => c.CreateAsync(It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateReservationResult.Succeeded(reservationDto));

        ReservationDto? created = null;
        var cut = Render<ReservationForm>(parameters => parameters
            .Add(p => p.ReservationsClient, client.Object)
            .Add(p => p.OnCreated, EventCallback.Factory.Create<ReservationDto>(this, dto => created = dto)));

        cut.Find("[data-testid=input-customer-name]").Input("Jane Doe");
        cut.Find("[data-testid=input-customer-phone]").Input("+32 470 00 00 00");
        cut.Find("[data-testid=input-party-size]").Input("4");
        cut.Find("[data-testid=btn-submit-reservation]").Click();

        client.Verify(
            c => c.CreateAsync(
                It.Is<CreateReservationRequest>(r =>
                    r.CustomerName == "Jane Doe" && r.CustomerPhone == "+32 470 00 00 00" && r.PartySize == 4),
                It.IsAny<CancellationToken>()),
            Times.Once);
        created.Should().Be(reservationDto,
            because: "a successful submission must raise OnCreated with the client's returned DTO.");
    }

    [Fact]
    public void Submit_ServerRejectsPhone_ShowsFieldErrorFromTheApiResponse()
    {
        var client = new Mock<IReservationsClient>();
        client
            .Setup(c => c.CreateAsync(It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateReservationResult.Failed(
                new Dictionary<string, string[]> { ["CustomerPhone"] = ["CustomerPhone is required."] }));

        var cut = Render<ReservationForm>(parameters => parameters
            .Add(p => p.ReservationsClient, client.Object));

        cut.Find("[data-testid=input-customer-name]").Input("Jane Doe");
        cut.Find("[data-testid=input-party-size]").Input("4");
        cut.Find("[data-testid=btn-submit-reservation]").Click();

        cut.Find("[data-testid=error-customer-phone]").TextContent.Should().Be("CustomerPhone is required.",
            because: "a 400 ValidationProblem's field errors must render next to the field they name.");
    }

    [Fact]
    public void Submit_ClientThrows_ShowsGenericErrorAndNeverRaisesOnCreated()
    {
        var client = new Mock<IReservationsClient>();
        client
            .Setup(c => c.CreateAsync(It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var onCreatedCalled = false;
        var cut = Render<ReservationForm>(parameters => parameters
            .Add(p => p.ReservationsClient, client.Object)
            .Add(p => p.OnCreated, EventCallback.Factory.Create<ReservationDto>(this, _ => onCreatedCalled = true)));

        cut.Find("[data-testid=input-customer-name]").Input("Jane Doe");
        cut.Find("[data-testid=input-customer-phone]").Input("+32 470 00 00 00");
        cut.Find("[data-testid=input-party-size]").Input("4");
        cut.Find("[data-testid=btn-submit-reservation]").Click();

        cut.Find("[data-testid=error-unexpected]").TextContent.Should().Be(
            "Something went wrong creating the reservation. Please try again.",
            because: "a transport failure must show a generic message, never the raw exception, and must not crash the component.");
        onCreatedCalled.Should().BeFalse();
    }
}
