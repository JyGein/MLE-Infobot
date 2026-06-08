using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

/// <summary>
/// just for copy pasting to make more comamnds
/// </summary>
internal class SubmitMatch : CommandBase
{
    const string COMMANDNAME = "submit-match";

    const string TEAMROLEOPTIONNAME = "team-role";
    const string SQUADNUMBEROPTIONNAME = "squad-number";
    const string WEEKNUMBEROPTIONNAME = "week-number";
    
    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
        client.ButtonExecuted += ButtonClicked;
        client.ModalSubmitted += FormSubmitted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Submit the match results of a squads match. {Messages.REQUIRESADMIN}")
            .AddOption(TEAMROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The team role of a squad that is in the match.", isRequired: true)
            .AddOption(SQUADNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of the same squad.", isRequired: true)
            .AddOption(WEEKNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of the match's week. Defaults to the current week.")
            .Build());
    }

    internal async Task CommandExecuted(SocketSlashCommand slashCommand)
    {
        if (slashCommand.Data.Name != COMMANDNAME) return;
        if (!IsAdmin(slashCommand))
        {
            await slashCommand.RespondAsync("You must be an admin to run this command!", ephemeral: true);
            return;
        }
        await slashCommand.DeferAsync(ephemeral: true);

        LeagueDBContext dBContext = new();

        if (await dBContext.Seasons.Include(s => s.SeasonWeeks).Include(s => s.PlayoffWeeks).FirstOrDefaultAsync(s => s.State == Season.SeasonState.Started) is not Season season)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "There is no current season.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        Week week = null!;
        if (slashCommand.Data.Options.FirstOrDefault(o => o.Name == WEEKNUMBEROPTIONNAME) is SocketSlashCommandDataOption weekNumberOption)
        {
            if (season.AllWeeks.FirstOrDefault(w => w.WeekNumber == (long)weekNumberOption.Value && week.HasBeenGenerated) is not Week chosenWeek)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "That is not a valid week for the current season.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            week = chosenWeek;
        }
        else
        {
            if (season.AllWeeks.FirstOrDefault(w => w.State == Week.WeekState.Current) is not Week currentWeek)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "There is no current week.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            week = currentWeek;
        }

        IRole teamRole = (IRole)slashCommand.Data.Options.First(o => o.Name == TEAMROLEOPTIONNAME).Value;
        if (dBContext.Teams.FirstOrDefault(team => team.TeamRoleID == teamRole.Id) is not Team team)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That role is not linked to a team!";
            });
            await dBContext.DisposeAsync();
            return;
        }

        await dBContext.Entry(week).Collection(w => w.Matches).Query().Include(m => m.HomeSquad).Include(m => m.AwaySquad).LoadAsync();
        long squadNumber = (long)slashCommand.Data.Options.First(o => o.Name == SQUADNUMBEROPTIONNAME).Value;
        if (week.Matches.FirstOrDefault(m => m.Squads.Any(s => s.TeamId == team.TeamId && s.SquadNumber == squadNumber)) is not Match match)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That is not a valid squad number.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        Squad squad = match.Squads.First(s => s.TeamId == team.TeamId && s.SquadNumber == squadNumber);

        ModalBuilder modal = new ModalBuilder()
            .WithTitle($"Submit {team.TeamName} Squad {squad.SquadNumber}'s Match")
            .WithCustomId($"{COMMANDNAME}:{slashCommand.Id}")
            .AddTextDisplay(new TextDisplayBuilder().WithContent($"Season {season.SeasonNumber} Week {week.WeekNumber}"))
            .AddTextDisplay(new TextDisplayBuilder().WithContent("Must be submitted within 10 minutes of sending the command."));

        bool isHomeSquad = match.HomeSquad == squad;
        await dBContext.Entry(match).Collection(m => m.Games).LoadAsync();
        await dBContext.Entry(match).Collection(m => m.Substitutions).LoadAsync();
        int count = 0;
        foreach (Game game in match.Games.OrderBy(g => g.GameId))
        {
            PlayerName playerName = await dBContext.PlayerNames.FirstAsync(pn => pn.PlayerUserID == (isHomeSquad ? game.HomePlayerIDWithSub : game.AwayPlayerIDWithSub));
            List<SelectMenuOptionBuilder> menuOptions = GetMatchResultMenuOptions();
            if (week is PlayoffWeek) menuOptions.Remove(menuOptions.Last()); //removes the double loss if it's a playoff week as that causes ties.
            modal.AddSelectMenu($"Enter {string.Join("", playerName.GetPlayerName().Take(25))}'s Game Score",
                $"{COMMANDNAME}:{count}",
                menuOptions,
                "The game score.");
            count++;
        }
        InteractionCache[slashCommand.Id] = (slashCommand, modal, match.MatchId, squad.SquadId);

        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = "Click to open the form:";
            mp.Components = new ComponentBuilder()
                .WithButton("Open Form", $"{COMMANDNAME}:{slashCommand.Id}")
                .Build();
        });

        new Task(async () => {
            await Task.Delay(TimeSpan.FromMinutes(10));
            if (InteractionCache.TryGetValue(slashCommand.Id, out _))
            {
                InteractionCache.Remove(slashCommand.Id);
                string existingMessage = (await slashCommand.GetOriginalResponseAsync()).Content;
                await slashCommand.ModifyOriginalResponseAsync(mp => { mp.Components = null; mp.Content = existingMessage + "\n[Timed out]"; });
            }
        }).Start();

        await dBContext.DisposeAsync();
    }

    /// <summary>
    /// Id, (Slash Command, Modal Builder, MatchId, SquadId)
    /// </summary>
    internal static Dictionary<ulong, (SocketSlashCommand, ModalBuilder, int, int)> InteractionCache = [];

    internal static List<SelectMenuOptionBuilder> GetMatchResultMenuOptions()
        => [
            new SelectMenuOptionBuilder().WithLabel("2-1").WithValue("2-1").WithDescription("Player wins."),
            new SelectMenuOptionBuilder().WithLabel("2-0").WithValue("2-0").WithDescription("Player wins."),
            new SelectMenuOptionBuilder().WithLabel("1-2").WithValue("1-2").WithDescription("Opponent wins."),
            new SelectMenuOptionBuilder().WithLabel("0-2").WithValue("0-2").WithDescription("Opponent wins."),
            new SelectMenuOptionBuilder().WithLabel("0-0").WithValue("0-0").WithDescription("Double Loss.")
        ];

    internal async Task ButtonClicked(SocketMessageComponent messageComponent)
    {
        if (!messageComponent.Data.CustomId.Contains(COMMANDNAME)) return;
        System.Text.RegularExpressions.Match m = SubmitMatchButtonIDPattern().Match(messageComponent.Data.CustomId);
        ulong interactionKey = ulong.Parse(m.Groups[1].Value);
        if (!InteractionCache.TryGetValue(interactionKey, out (SocketSlashCommand, ModalBuilder, int, int) interactionInfo))
        {
            await messageComponent.RespondAsync("This interaction has expired!", ephemeral: true);
            return;
        }
        Modal modal = interactionInfo.Item2.Build();
        await messageComponent.RespondWithModalAsync(modal);
        SocketSlashCommand slashCommand = interactionInfo.Item1;
        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = "Form Opened.";
        });
    }

    internal async Task FormSubmitted(SocketModal modal)
    {
        if (!modal.Data.CustomId.Contains(COMMANDNAME)) return;
        await modal.DeferAsync();
        LeagueDBContext dBContext = new();
        System.Text.RegularExpressions.Match m = SubmitMatchModalIDPattern().Match(modal.Data.CustomId);
        ulong interactionKey = ulong.Parse(m.Groups[1].Value);
        if (!InteractionCache.TryGetValue(interactionKey, out (SocketSlashCommand, ModalBuilder, int, int) interactionInfo))
        {
            await modal.ModifyOriginalResponseAsync(mp =>
            {
                mp.Content = "This Form has expired!";
                mp.Components = null;
            });
            await dBContext.DisposeAsync();
            return;
        }
        SocketSlashCommand slashCommand = interactionInfo.Item1;
        int matchId = interactionInfo.Item3;
        Match match = (Match)(await dBContext.FindAsync(typeof(Match), matchId))!;
        int squadId = interactionInfo.Item4;
        Squad squad = (Squad)(await dBContext.FindAsync(typeof(Squad), squadId))!;

        await dBContext.Entry(match).Collection(m => m.Games).LoadAsync();
        await dBContext.Entry(match).Collection(m => m.Substitutions).LoadAsync();

        int count = 0;
        bool isHomeSquad = match.HomeSquadId == squad.SquadId;
        foreach (Game game in match.Games.OrderBy(g => g.GameId))
        {
            string gameRecord = modal.Data.Components.First(smcd => smcd.CustomId.Contains(count.ToString())).Values.First();
            if (isHomeSquad)
            {
                game.HomePlayerWins = gameRecord.First() - '0';
                game.AwayPlayerWins = gameRecord.Last() - '0';
            }
            else
            {
                game.HomePlayerWins = gameRecord.Last() - '0';
                game.AwayPlayerWins = gameRecord.First() - '0';
            }
            game.State = game.HomePlayerWins == 2 ? Game.GameState.Home : game.AwayPlayerWins == 2 ? Game.GameState.Away : Game.GameState.DoubleLoss;
            count++;
        }
        match.Winner = match.HomeGameWins > match.AwayGameWins ? Match.MatchState.Home : match.AwayGameWins > match.HomeGameWins ? Match.MatchState.Away : Match.MatchState.Tie;

        await dBContext.SaveChangesAsync();
        await dBContext.Entry(squad).Reference(s => s.Team).LoadAsync();
        await dBContext.Entry(match).Reference(m => m.Week).LoadAsync();
        string message = $"Successfully reported {squad.Team.TeamName} squad {squad.SquadNumber}'s match in week {match.Week.WeekNumber} as a {(match.Winner == Match.MatchState.Tie ? "tie" : $"win for the {match.Winner.ToString().ToLower()} squad")}.";
        Console.WriteLine(message);
        Embed[] embeds = [(await match.GetDefaultEmbed()).Build()];
        await modal.ModifyOriginalResponseAsync(mp =>
        {
            mp.Content = message;
            mp.Embeds = embeds;
            mp.Components = null;
        });
        InteractionCache.Remove(slashCommand.Id);
        await dBContext.DisposeAsync();
    }
}
