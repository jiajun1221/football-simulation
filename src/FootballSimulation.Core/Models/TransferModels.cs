namespace FootballSimulation.Models;

public enum PlayerTransferStatus
{
    None,
    Listed,
    Negotiating,
    Transferred,
    Unavailable
}

public enum PlayerRole
{
    KeyPlayer,
    Starter,
    Rotation,
    Prospect,
    Backup
}

public enum PlayerContractStatus
{
    Active,
    ExpiringSoon,
    PreContractEligible,
    Expired,
    FreeAgent
}

public enum OfferStatus
{
    Pending,
    PendingUntilWindowOpens,
    Accepted,
    Rejected,
    Countered,
    Withdrawn,
    AgreedForNextWindow,
    CompletedWhenWindowOpens,
    Completed
}

public enum TransferNotificationType
{
    Info,
    UserOfferAccepted,
    UserOfferRejected,
    UserOfferCountered,
    AiOfferReceived,
    TransferCompleted,
    InsufficientBudget,
    WindowClosed
}

public class ClubFinance
{
    public string LeagueId { get; set; } = string.Empty;
    public string ClubName { get; set; } = string.Empty;
    public decimal ClubTransferBudget { get; set; }
    public decimal ClubWageBudget { get; set; }
    public decimal TransferSpent { get; set; }
    public decimal TransferIncome { get; set; }
    public decimal WageSpent { get; set; }
    public decimal YouthWageSpent { get; set; }

    public decimal AvailableTransferBudget => Math.Max(0, ClubTransferBudget + TransferIncome - TransferSpent);
    public decimal AvailableWageBudget => Math.Max(0, ClubWageBudget - WageSpent - YouthWageSpent);
}

public class TransferOffer
{
    public string OfferId { get; set; } = Guid.NewGuid().ToString("N");
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string FromLeagueId { get; set; } = string.Empty;
    public string FromClubName { get; set; } = string.Empty;
    public string ToLeagueId { get; set; } = string.Empty;
    public string ToClubName { get; set; } = string.Empty;
    public decimal Fee { get; set; }
    public decimal? CounterFee { get; set; }
    public decimal? WeeklyWage { get; set; }
    public int ContractYears { get; set; } = 5;
    public PlayerRole SquadRole { get; set; } = PlayerRole.Rotation;
    public OfferStatus Status { get; set; } = OfferStatus.Pending;
    public int CreatedRound { get; set; }
    public int? AgreementRound { get; set; }
    public int? CompletedRound { get; set; }
    public bool IsUserOffer { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class TransferHistoryItem
{
    public string TransferId { get; set; } = Guid.NewGuid().ToString("N");
    public int RoundNumber { get; set; }
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string FromLeagueId { get; set; } = string.Empty;
    public string FromClubName { get; set; } = string.Empty;
    public string ToLeagueId { get; set; } = string.Empty;
    public string ToClubName { get; set; } = string.Empty;
    public decimal Fee { get; set; }
    public decimal? WeeklyWage { get; set; }
    public int? ContractEndYear { get; set; }
    public PlayerRole? SquadRole { get; set; }
    public string Type { get; set; } = "Permanent";
}

public class TransferNotification
{
    public string NotificationId { get; set; } = Guid.NewGuid().ToString("N");
    public TransferNotificationType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public int CreatedRound { get; set; }
    public string? RelatedOfferId { get; set; }
    public bool IsRead { get; set; }
}

public class TransferLeagueState
{
    public string LeagueId { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public List<Team> Teams { get; set; } = [];
}

public class TransferMarketState
{
    public string ActiveSeason { get; set; } = string.Empty;
    public List<TransferLeagueState> Leagues { get; set; } = [];
    public List<ClubFinance> ClubFinances { get; set; } = [];
    public List<TransferOffer> Offers { get; set; } = [];
    public List<TransferNotification> Inbox { get; set; } = [];
    public List<TransferHistoryItem> TransferHistory { get; set; } = [];
    public List<string> ShortlistedPlayerIds { get; set; } = [];
    public List<Player> FreeAgents { get; set; } = [];
    public int LastAiActivityRound { get; set; }
}

public class TransferSearchCriteria
{
    public string PlayerName { get; set; } = string.Empty;
    public string ClubName { get; set; } = string.Empty;
    public string Nationality { get; set; } = string.Empty;
    public string LeagueId { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public int? MinimumOverall { get; set; }
    public decimal? MaximumPrice { get; set; }
    public int? MinimumAge { get; set; }
    public int? MaximumAge { get; set; }
    public string Trait { get; set; } = string.Empty;
    public PlayerFormStatus? FormStatus { get; set; }
}

public record TransferPlayerListing(
    Player Player,
    Team Team,
    string LeagueId,
    string LeagueName,
    decimal MarketValue,
    decimal AskingPrice,
    string StatusText,
    decimal WeeklyWage,
    int? ContractEndYear,
    int YearsRemaining,
    string ContractStatusText);

public record ContractRenewalResult(
    bool Accepted,
    string Message,
    decimal WeeklyWage,
    int ContractEndYear,
    PlayerRole SquadRole);

public record TransferRecommendation(
    TransferPlayerListing Listing,
    string Reason);

public record TransferWindowInfo(
    bool IsOpen,
    string StatusText,
    int RoundsRemaining,
    string Tooltip);
