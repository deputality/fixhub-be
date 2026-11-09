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