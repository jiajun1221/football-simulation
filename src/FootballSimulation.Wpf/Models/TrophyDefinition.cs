using FootballSimulation.Models;

namespace FootballSimulation.Wpf.Models;

public enum TrophyCelebrationNextRoute
{
    Dashboard,
    RoundResult,
    SeasonOverview
}

public sealed record TrophyDefinition(
    string CompetitionId,
    string CompetitionName,
    CompetitionType CompetitionType,
    string LeagueId,
    string TrophyImagePath,
    string BackgroundImagePath,
    string ThemeColor,
    string AccentColor,
    string CelebrationTitle,
    string CelebrationSubtitle);

public sealed record TrophyCelebrationEvent(
    string CelebrationKey,
    string CompetitionId,
    string CompetitionName,
    CompetitionType CompetitionType,
    string LeagueId,
    string ClubName,
    string Season,
    string TrophyImagePath,
    string BackgroundImagePath,
    string ThemeColor,
    string AccentColor,
    string CelebrationTitle,
    string CelebrationSubtitle,
    string Message,
    TrophyCelebrationNextRoute NextRoute);
