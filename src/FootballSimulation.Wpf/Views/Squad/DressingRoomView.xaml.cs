using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Services;

namespace FootballSimulation.Wpf.Views;

public partial class DressingRoomView : UserControl
{
    private Team? _team;

    public DressingRoomView()
    {
        InitializeComponent();
    }

    public void LoadTeam(Team? team)
    {
        _team = team;
        if (team is null)
        {
            PlayersGrid.ItemsSource = Array.Empty<object>();
            AtmosphereText.Text = "No squad data";
            StateMessageText.Text = "Dressing room data is unavailable.";
            StateMessageBorder.Visibility = Visibility.Visible;
            return;
        }

        DressingRoomService.EnsureTeam(team);
        HarmonyText.Text = $"{team.DressingRoom.Harmony:0}/100";
        HappinessText.Text = $"{team.DressingRoom.SquadHappiness:0}/100";
        AtmosphereText.Text = DescribeAtmosphere(team.DressingRoom.Harmony);
        RecentEventText.Text = team.DressingRoom.RecentEvents.LastOrDefault()?.Description ?? "No recent events";
        var rows = team.AllPlayers.OrderByDescending(player => player.IsCaptain).ThenBy(player => player.Name).Select(CreateRow).ToList();
        StateMessageText.Text = rows.Count == 0 ? "No players are available." : string.Empty;
        StateMessageBorder.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PlayersGrid.ItemsSource = rows;
        PlayersGrid.SelectedItem = rows.FirstOrDefault();
    }

    private void PlayersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowPlayerDetails(PlayersGrid.SelectedItem as PlayerMoodRow);
    }

    private void PlayersGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var row = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not PlayerMoodRow playerRow) return;

        PlayersGrid.SelectedItem = playerRow;
        ShowPlayerDetails(playerRow);
    }

    private void ShowPlayerDetails(PlayerMoodRow? row)
    {
        if (_team is null || row is null) return;
        var player = row.Player;
        DetailNameText.Text = player.Name;
        var nationality = PlayerNationalityDisplayService.Resolve(player);
        DetailFlagBadge.FlagSource = nationality.FlagImagePath;
        DetailFlagBadge.ToolTip = nationality.Name;
        var moodDisplay = CreateMoodFace(player.MentalState.Emotions);
        DetailMoodFaceText.Text = moodDisplay.Face;
        DetailMoodFaceBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(moodDisplay.Background));
        DetailMoodFaceBorder.ToolTip = moodDisplay.Label;
        var keyEmotion = DescribeKeyEmotion(player);
        var emotionBadge = DescribeEmotionBadge(keyEmotion);
        var emotionIcon = DescribeEmotionIcon(keyEmotion);
        DetailEmotionIconPath.Data = string.IsNullOrWhiteSpace(emotionIcon.PathData) ? null : Geometry.Parse(emotionIcon.PathData);
        DetailEmotionIconText.Text = emotionIcon.Symbol;
        DetailEmotionIconPath.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(emotionBadge.Foreground));
        DetailEmotionIconText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(emotionBadge.Foreground));
        DetailEmotionIconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(emotionBadge.Background));
        DetailEmotionIconBorder.ToolTip = $"Key emotion: {keyEmotion}";
        var p = player.MentalState.Personality;
        var archetypeBadge = DescribeArchetypeBadge(row.Archetype);
        DetailArchetypeBadgeText.Text = row.Archetype;
        DetailArchetypeBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(archetypeBadge.Foreground));
        DetailArchetypeBadgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(archetypeBadge.Background));
        DetailSecondaryTagText.Text = string.IsNullOrWhiteSpace(row.SecondaryTag) ? string.Empty : $"· {row.SecondaryTag}";
        PersonalityText.Text = DescribePersonality(p);
        var id = RelationshipService.GetStableId(player, _team);
        FriendsList.ItemsSource = RelationshipNames(id, relationship => relationship.Friendship >= 12, "No close friends yet");
        RivalsList.ItemsSource = RelationshipNames(id, relationship => relationship.Rivalry >= 12 || relationship.Friendship <= -12, "No notable rivals");
        var mentor = string.IsNullOrWhiteSpace(player.MentalState.MentorPlayerId)
            ? null
            : ResolvePlayer(player.MentalState.MentorPlayerId);
        MentorText.Text = mentor?.Name ?? "None";
        MentorFlagBadge.Visibility = mentor is null ? Visibility.Collapsed : Visibility.Visible;
        if (mentor is not null)
        {
            var mentorNationality = PlayerNationalityDisplayService.Resolve(mentor);
            MentorFlagBadge.FlagSource = mentorNationality.FlagImagePath;
            MentorFlagBadge.ToolTip = mentorNationality.Name;
        }
        MoodChangesList.ItemsSource = player.MentalState.RecentChanges.Count == 0
            ? ["No recent mood changes"]
            : player.MentalState.RecentChanges.AsEnumerable().Reverse().Select(change => $"{change.Reason}: {(change.Delta >= 0 ? "+" : string.Empty)}{change.Delta:0.0}");
        LoadSelectedPlayerGroups(player);
        DetailsScrollViewer.ScrollToTop();
    }

    private void LoadSelectedPlayerGroups(Player player)
    {
        var playerId = RelationshipService.GetStableId(player, _team!);

        var cliques = _team!.DressingRoom.Cliques
            .Where(clique => clique.PlayerIds.Contains(playerId, StringComparer.OrdinalIgnoreCase))
            .Select(clique => new CliqueDisplay(clique.Name, ResolvePlayers(clique.PlayerIds).ToList()))
            .ToList();
        CliquesList.ItemsSource = cliques.Count == 0
            ? [new CliqueDisplay("Not part of an established group", [])]
            : cliques;

        ChemistryList.ItemsSource = CreateChemistryHints(_team, player);
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T parent) return parent;
            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private PlayerMoodRow CreateRow(Player player)
    {
        var e = player.MentalState.Emotions;
        var archetype = PersonalityService.GetArchetype(player);
        var mood = CreateMoodFace(e);
        var keyEmotion = DescribeKeyEmotion(player);
        var nationality = PlayerNationalityDisplayService.Resolve(player);
        var archetypeBadge = DescribeArchetypeBadge(archetype);
        var emotionBadge = DescribeEmotionBadge(keyEmotion);
        var emotionIcon = DescribeEmotionIcon(keyEmotion);
        return new PlayerMoodRow(
            player,
            player.Name,
            nationality.FlagImagePath,
            nationality.Name,
            archetype,
            PersonalityService.GetSecondaryTag(player),
            archetypeBadge.Background,
            archetypeBadge.Foreground,
            mood.Label,
            mood.Face,
            mood.Background,
            mood.Foreground,
            keyEmotion,
            emotionIcon.PathData,
            emotionIcon.Symbol,
            emotionBadge.Background,
            emotionBadge.Foreground);
    }

    private IEnumerable<FlaggedPlayerDisplay> RelationshipNames(string id, Func<PlayerRelationship, bool> filter, string empty)
    {
        var players = _team!.DressingRoom.Relationships.Where(r => (r.FirstPlayerId == id || r.SecondPlayerId == id) && filter(r))
            .Select(r => ResolvePlayer(r.FirstPlayerId == id ? r.SecondPlayerId : r.FirstPlayerId))
            .Where(player => player is not null)
            .Select(player => CreateFlaggedPlayer(player!))
            .ToList();
        return players.Count == 0 ? [FlaggedPlayerDisplay.Message(empty)] : players;
    }

    private IEnumerable<FlaggedPlayerDisplay> ResolvePlayers(IEnumerable<string> ids) => ids
        .Select(ResolvePlayer)
        .Where(player => player is not null)
        .Select(player => CreateFlaggedPlayer(player!));

    private Player? ResolvePlayer(string id) => _team!.AllPlayers.FirstOrDefault(p => RelationshipService.GetStableId(p, _team) == id);
    private string ResolveName(string id) => _team!.AllPlayers.FirstOrDefault(p => RelationshipService.GetStableId(p, _team) == id)?.Name ?? "Unknown player";

    private static FlaggedPlayerDisplay CreateFlaggedPlayer(Player player)
    {
        var nationality = PlayerNationalityDisplayService.Resolve(player);
        return new FlaggedPlayerDisplay(player.Name, nationality.FlagImagePath, nationality.Name, Visibility.Visible);
    }

    private static IEnumerable<ChemistryHintDisplay> CreateChemistryHints(Team team, Player selectedPlayer)
    {
        var selectedPlayerId = RelationshipService.GetStableId(selectedPlayer, team);
        var links = new FormationChemistryService().GetAdjacentLinks(team.Players)
            .Where(pair =>
                RelationshipService.GetStableId(pair.First, team).Equals(selectedPlayerId, StringComparison.OrdinalIgnoreCase) ||
                RelationshipService.GetStableId(pair.Second, team).Equals(selectedPlayerId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (links.Count == 0)
        {
            return [new ChemistryHintDisplay(FlaggedPlayerDisplay.Message(string.Empty), "No adjacent formation pairing for this player.", Visibility.Collapsed)];
        }

        return links.Take(8).Select(pair =>
        {
            var otherPlayer = RelationshipService.GetStableId(pair.First, team).Equals(selectedPlayerId, StringComparison.OrdinalIgnoreCase)
                ? pair.Second
                : pair.First;
            return new ChemistryHintDisplay(
                CreateFlaggedPlayer(otherPlayer),
                pair.First.MentalState.MatchChemistryModifier >= 0.02 ? "Strong pairing" : pair.First.MentalState.MatchChemistryModifier < 0 ? "Poor understanding" : "Developing understanding",
                Visibility.Visible);
        });
    }

    private static string DescribeAtmosphere(double harmony) => harmony switch { >= 75 => "United", >= 58 => "Positive", >= 42 => "Steady", >= 25 => "Uneasy", _ => "Fractured" };
    private static string DescribePersonality(PersonalityProfile p) => p.Leadership >= 15 ? "A strong influence who handles responsibility well." : p.Professionalism >= 15 ? "Consistent, diligent and dependable." : p.Sociability >= 15 ? "Social and quick to connect with teammates." : p.Ego >= 15 ? "Needs status and careful management." : "A balanced presence in the squad.";

    private static string DescribeKeyEmotion(Player player)
    {
        var emotions = player.MentalState.Emotions;
        var personality = player.MentalState.Personality;
        var deviations = new Dictionary<string, double>
        {
            ["Confidence"] = Math.Abs(emotions.Confidence - Math.Clamp(player.Morale, 35, 65)),
            ["Joy"] = Math.Abs(emotions.Joy - Math.Clamp(30 + player.Morale * 0.2, 30, 55)),
            ["Focus"] = Math.Abs(emotions.Focus - Math.Clamp(45 + personality.Professionalism * 0.8, 45, 65)),
            ["Frustration"] = Math.Abs(emotions.Frustration - Math.Clamp(30 - personality.Temperament * 0.8, 10, 35)),
            ["Anxiety"] = Math.Abs(emotions.Anxiety - Math.Clamp(32 - personality.Resilience * 0.8, 10, 35))
        };
        var strongest = deviations.MaxBy(item => item.Value);
        return strongest.Value < 3 ? "Balanced" : strongest.Key;
    }

    private static BadgeDisplay DescribeArchetypeBadge(string archetype) => archetype switch
    {
        "Captain Material" => new("#DBEAFE", "#1D4ED8"),
        "Hothead" => new("#FEE2E2", "#B91C1C"),
        "Diva" => new("#FCE7F3", "#BE185D"),
        "Club Man" => new("#DCFCE7", "#15803D"),
        "Mercenary" => new("#FEF3C7", "#A16207"),
        "Quiet Professional" => new("#E0E7FF", "#4338CA"),
        "Big-Game Player" => new("#EDE9FE", "#6D28D9"),
        "Fragile Talent" => new("#FFE4E6", "#BE123C"),
        "Sensitive Soul" => new("#FCE7F3", "#BE185D"),
        "Enforcer" => new("#FED7AA", "#C2410C"),
        "Social Glue" => new("#CCFBF1", "#0F766E"),
        "Lone Wolf" => new("#E5E7EB", "#374151"),
        "Driven Professional" => new("#DBEAFE", "#1D4ED8"),
        "Humble Servant" => new("#D1FAE5", "#047857"),
        "Free Spirit" => new("#FAE8FF", "#A21CAF"),
        "Passenger" => new("#F1F5F9", "#475569"),
        "Gentleman" => new("#ECFCCB", "#3F6212"),
        _ => new("#E2E8F0", "#334155")
    };

    private static BadgeDisplay DescribeEmotionBadge(string emotion) => emotion switch
    {
        "Confidence" => new("#F3E8FF", "#7E22CE"),
        "Joy" => new("#DCFCE7", "#15803D"),
        "Focus" => new("#DBEAFE", "#1D4ED8"),
        "Frustration" => new("#FFEDD5", "#C2410C"),
        "Anxiety" => new("#FEE2E2", "#B91C1C"),
        "Balanced" => new("#E2E8F0", "#334155"),
        _ => new("#E2E8F0", "#334155")
    };

    private static EmotionIconDisplay DescribeEmotionIcon(string emotion) => emotion switch
    {
        "Confidence" => new("M12,22 C12,22 20,18 20,12 L20,5 L12,2 L4,5 L4,12 C4,18 12,22 12,22 M9,12 L11,14 L15,10", string.Empty),
        "Joy" => new("M12,2 L12,4 M12,20 L12,22 M4.93,4.93 L6.34,6.34 M17.66,17.66 L19.07,19.07 M2,12 L4,12 M20,12 L22,12 M4.93,19.07 L6.34,17.66 M17.66,6.34 L19.07,4.93 M8,12 A4,4 0 1 0 16,12 A4,4 0 1 0 8,12", string.Empty),
        "Focus" => new("M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M12,6 A6,6 0 1 0 12,18 A6,6 0 1 0 12,6 M12,10 A2,2 0 1 0 12,14 A2,2 0 1 0 12,10", string.Empty),
        "Frustration" => new("M12,22 C7,22 4,18.5 4,14 C4,10 7,7 9,4 C9.5,7 11,8 12,8 C14,6 15,4 15,2 C19,5 21,9 20,14 C20,18.5 17,22 12,22 M12,22 C10,22 8.5,20.5 8.5,18.5 C8.5,16.5 10,15 12,13 C14,15 15.5,16.5 15.5,18.5 C15.5,20.5 14,22 12,22", string.Empty),
        "Anxiety" => new("M3,12 L7,12 L9,7 L13,17 L15,12 L21,12", string.Empty),
        _ => new(string.Empty, "⚖")
    };

    private static MoodFaceDisplay CreateMoodFace(EmotionState emotions)
    {
        var score =
            emotions.Confidence * 0.30 +
            emotions.Joy * 0.25 +
            emotions.Focus * 0.10 +
            (100 - emotions.Frustration) * 0.175 +
            (100 - emotions.Anxiety) * 0.175;

        return score switch
        {
            >= 80 => new("😁", "Very Happy", "#22C55E", "#FFFFFF"),
            >= 65 => new("🙂", "Happy", "#BBF7D0", "#166534"),
            >= 45 => new("😐", "Neutral", "#FDE047", "#713F12"),
            >= 30 => new("🙁", "Unhappy", "#FB923C", "#7C2D12"),
            _ => new("😠", "Angry", "#EF4444", "#FFFFFF")
        };
    }

    private sealed record BadgeDisplay(string Background, string Foreground);
    private sealed record EmotionIconDisplay(string PathData, string Symbol);
    private sealed record MoodFaceDisplay(string Face, string Label, string Background, string Foreground);
    private sealed record FlaggedPlayerDisplay(string Name, string FlagImagePath, string NationalityName, Visibility FlagVisibility)
    {
        public static FlaggedPlayerDisplay Message(string text) => new(text, string.Empty, string.Empty, Visibility.Collapsed);
    }
    private sealed record CliqueDisplay(string Name, IReadOnlyList<FlaggedPlayerDisplay> Members);
    private sealed record ChemistryHintDisplay(FlaggedPlayerDisplay Player, string Description, Visibility PairVisibility);

    private sealed record PlayerMoodRow(
        Player Player,
        string Name,
        string NationalityFlagImagePath,
        string NationalityName,
        string Archetype,
        string SecondaryTag,
        string ArchetypeBadgeBackground,
        string ArchetypeBadgeForeground,
        string Mood,
        string MoodFace,
        string MoodBadgeBackground,
        string MoodBadgeForeground,
        string KeyEmotion,
        string EmotionIconData,
        string EmotionSymbol,
        string EmotionBadgeBackground,
        string EmotionBadgeForeground);
}
