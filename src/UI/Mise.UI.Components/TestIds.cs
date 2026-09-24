namespace Mise.UI.Components;

/// <summary>The only selector vocabulary bUnit and Playwright may use: <c>{noun}-{id}</c> for rows, <c>btn-{action}</c> for buttons.</summary>
public static class TestIds
{
    public const string TopBar = "top-bar";
    public const string RestaurantName = "restaurant-name";
    public const string ShellDate = "shell-date";
    public const string ShellClock = "shell-clock";
    public const string UserBlock = "user-block";
    public const string UserAvatar = "user-avatar";
    public const string CurrentUser = "nav-current-user";
    public const string CurrentUserRole = "nav-current-user-role";

    public const string SideNav = "side-nav";
    public const string NavGroupManager = "nav-group-manager";
    public const string NavReservations = "nav-reservations";
    public const string SignOut = "btn-logout";
    public const string SkipToContent = "skip-to-content";
    public const string MainContent = "main-content";

    public const string PageHeader = "page-header";
    public const string PageTitle = "page-title";
    public const string PageSubtitle = "page-subtitle";
    public const string PageActions = "page-actions";

    public const string StatusPill = "status-pill";
    public const string StatusDot = "status-dot";

    public const string Backdrop = "overlay-backdrop";
    public const string Close = "btn-close";
    public const string OverlayTitle = "overlay-title";
    public const string OverlayFooter = "overlay-footer";
    public const string FocusSentinelStart = "focus-sentinel-start";
    public const string FocusSentinelEnd = "focus-sentinel-end";

    public const string Toast = "toast";
    public const string ToastRegion = "toast-region";

    public const string EmptyState = "empty-state";
    public const string EmptyStateTitle = "empty-state-title";
    public const string EmptyStateBody = "empty-state-body";

    public const string StepperValue = "stepper-value";
    public const string StepperDecrease = "btn-stepper-decrease";
    public const string StepperIncrease = "btn-stepper-increase";

    public const string ReservationForm = "form-create-reservation";
    public const string InputCustomerName = "input-customer-name";
    public const string InputCustomerPhone = "input-customer-phone";
    public const string InputPartySize = "input-party-size";
    public const string InputReservationDateTime = "input-reservation-date-time";
    public const string ErrorCustomerPhone = "error-customer-phone";
    public const string ErrorPartySize = "error-party-size";
    public const string ErrorUnexpected = "error-unexpected";
    public const string SubmitReservation = "btn-submit-reservation";
    public const string DayList = "day-list";
    public const string DayListEmpty = "day-list-empty";

    public const string AuthFrame = "auth-frame";
    public const string AuthCard = "auth-card";
    public const string LoginHeading = "login-heading";
    public const string InputUsername = "input-username";
    public const string InputPassword = "input-password";
    public const string LoginError = "error-login";
    public const string LoginSubmit = "btn-login";

    public const string DesignGallery = "design-gallery";
    public const string GalleryOpenDrawer = "btn-gallery-open-drawer";
    public const string GalleryOpenModal = "btn-gallery-open-modal";
    public const string GalleryShowToast = "btn-gallery-show-toast";
    public const string GalleryDrawer = "drawer-gallery";
    public const string GalleryModal = "modal-gallery";
    public const string GallerySegmented = "segmented-gallery";
    public const string GalleryStepper = "stepper-gallery";

    public static string Segment(string value) => $"segment-{value}";

    public static string ReservationRow(Guid id) => $"reservation-row-{id}";
}
