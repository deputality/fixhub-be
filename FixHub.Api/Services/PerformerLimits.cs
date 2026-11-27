namespace FixHub.Api.Services;

// Ограничения для мастера, которого администратор ещё не проверил.
// Единственное место с цифрами: контроллеры и фронтенд берут лимиты отсюда.
public record PerformerLimits(int MaxServices, int MaxPortfolioItems, bool CanSendOffers)
{
    public static readonly PerformerLimits Unverified = new(MaxServices: 3, MaxPortfolioItems: 5, CanSendOffers: false);
    public static readonly PerformerLimits Verified = new(MaxServices: int.MaxValue, MaxPortfolioItems: 30, CanSendOffers: true);

    public static PerformerLimits For(bool isVerified) => isVerified ? Verified : Unverified;

    public const string OffersMessage =
        "Отправлять предложения по заявкам могут только проверенные мастера. Загрузите документы в профиле";

    public static string ServicesMessage(int max) =>
        $"Непроверенный мастер может опубликовать не больше {max} услуг. Пройдите проверку, чтобы добавить больше";

    public static string PortfolioMessage(int max) =>
        $"Непроверенный мастер может добавить не больше {max} работ в портфолио. Пройдите проверку, чтобы добавить больше";
}
