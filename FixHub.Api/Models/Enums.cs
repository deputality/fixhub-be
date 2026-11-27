namespace FixHub.Api.Models;

public enum UserRole
{
    Client = 1,
    Performer = 2,
    Admin = 3
}

public enum PricingUnit
{
    Hour = 1,
    Piece = 2
}

public enum OrderStatus
{
    New = 1,
    Confirmed = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5
}

public enum NotificationType
{
    NewOrder = 1,
    OrderStatusChanged = 2,
    NewReview = 3,
    ProfileVerified = 4,
    NewMessage = 5,
    NewOffer = 6,
    OfferAccepted = 7,
    ReviewReply = 8
}

// Заявка клиента «назовите цену»
public enum RequestStatus
{
    Open = 1,
    Chosen = 2,
    Cancelled = 3
}

public enum OfferStatus
{
    Pending = 1,
    Accepted = 2,
    Rejected = 3,
    Withdrawn = 4
}