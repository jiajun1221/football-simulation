using FootballSimulation.Models;
using FootballSimulation.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<PremierLeagueDataService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactDevelopment", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("ReactDevelopment");

app.MapGet("/api/health", () => Results.Ok(new { status = "ready" }))
    .WithName("GetHealth");

app.MapGet("/api/teams", (PremierLeagueDataService dataService) =>
{
    var teams = dataService.LoadTeams()
        .Select(ToTeamSummary)
        .OrderBy(team => team.Name)
        .ToList();

    return Results.Ok(teams);
})
.WithName("GetTeams");

app.MapGet("/api/teams/{teamName}", (string teamName, PremierLeagueDataService dataService) =>
{
    var team = dataService.LoadTeams()
        .FirstOrDefault(team => string.Equals(team.Name, teamName, StringComparison.OrdinalIgnoreCase));

    return team is null
        ? Results.NotFound(new { message = $"Team '{teamName}' was not found." })
        : Results.Ok(ToTeamDetail(team));
})
.WithName("GetTeam");

app.Run();

static TeamSummaryDto ToTeamSummary(Team team)
{
    var squad = team.Players.Concat(team.Substitutes).ToList();
    var averageRating = squad.Count == 0
        ? 0
        : Math.Round(squad.Average(player => player.OverallRating), 1);

    return new TeamSummaryDto(
        team.Name,
        team.Venue,
        team.StadiumName,
        team.Formation,
        team.Players.Count,
        team.Substitutes.Count,
        averageRating);
}

static TeamDetailDto ToTeamDetail(Team team)
{
    return new TeamDetailDto(
        ToTeamSummary(team),
        team.Players.Select(ToPlayer).ToList(),
        team.Substitutes.Select(ToPlayer).ToList());
}

static PlayerDto ToPlayer(Player player)
{
    return new PlayerDto(
        player.PlayerId,
        player.Name,
        player.SquadNumber,
        player.Position.ToString(),
        player.PreferredPosition,
        player.NationalityName,
        player.Age,
        player.OverallRating,
        player.Pace,
        player.Shooting,
        player.Passing,
        player.Dribbling,
        player.Defending,
        player.Physical);
}

record TeamSummaryDto(
    string Name,
    string Venue,
    string StadiumName,
    string Formation,
    int StarterCount,
    int SubstituteCount,
    double AverageRating);

record TeamDetailDto(
    TeamSummaryDto Team,
    IReadOnlyList<PlayerDto> Starters,
    IReadOnlyList<PlayerDto> Substitutes);

record PlayerDto(
    string PlayerId,
    string Name,
    int SquadNumber,
    string Position,
    string PreferredPosition,
    string Nationality,
    int? Age,
    int OverallRating,
    int Pace,
    int Shooting,
    int Passing,
    int Dribbling,
    int Defending,
    int Physical);
