using Discord;
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
internal class SwapHomeAway : CommandBase
{
    const string COMMANDNAME = "swap-home-away";

    const string WEEKNUMBEROPTIONNAME = "week-number";
    const string TEAMROLEOPTIONNAME = "team-role";
    const string SQUADNUMBEROPTIONNAME = "squad-number";
    const string SEASONNUMBEROPTIONNAME = "season-number";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Swaps whos home and whos away for a squad's match. {Messages.REQUIRESADMIN}")
            .AddOption(WEEKNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of the week to edit.", isRequired: true)
            .AddOption(TEAMROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The role of the squad's team.", isRequired: true)
            .AddOption(SQUADNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of the squad.", isRequired: true)
            .AddOption(SEASONNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of the season to edit. Defaults to the unpublished season then current season.")
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

        Season season = null!;
        if (slashCommand.Data.Options.FirstOrDefault(o => o.Name == SEASONNUMBEROPTIONNAME) is SocketSlashCommandDataOption seasonNumberOption)
        {
            if (await dBContext.Seasons.FirstOrDefaultAsync(s => s.SeasonNumber == (long)seasonNumberOption.Value) is not Season s)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "That season does not exist!";
                });
                await dBContext.DisposeAsync();
                return;
            }
            season = s;
        }
        else
        {
            if (await dBContext.Seasons.FirstOrDefaultAsync(s => s.State == Season.SeasonState.Unpublished) is not Season s)
            {
                if (await dBContext.Seasons.FirstOrDefaultAsync(s => s.State == Season.SeasonState.Started) is not Season cs)
                {
                    await slashCommand.ModifyOriginalResponseAsync((mp) =>
                    {
                        mp.Content = "There is no unpublished or current season.";
                    });
                    await dBContext.DisposeAsync();
                    return;
                }
                s = cs;
            }
            season = s;
        }

        await dBContext.Entry(season).Collection(s => s.SeasonWeeks).LoadAsync();
        await dBContext.Entry(season).Collection(s => s.PlayoffWeeks).LoadAsync();
        long weekNumber = (long)slashCommand.Data.Options.First(o => o.Name == WEEKNUMBEROPTIONNAME).Value;
        if (season.AllWeeks.FirstOrDefault(w => w.WeekNumber == weekNumber) is not Week week)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That is not a valid week number.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        IRole teamRole = (IRole)slashCommand.Data.Options.First(o => o.Name == TEAMROLEOPTIONNAME).Value;
        if (await dBContext.Teams.FirstOrDefaultAsync(t => t.TeamRoleID == teamRole.Id) is not Team team)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "The role entered for the squad's team is not linked to a team.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        await dBContext.Entry(week).Collection(w => w.Matches).Query().Include(m => m.HomeSquad).Include(m => m.AwaySquad).Include(m => m.Games).LoadAsync();
        long squadNumber = (long)slashCommand.Data.Options.First(o => o.Name == SQUADNUMBEROPTIONNAME).Value;
        if (week.Matches.SelectMany(m => m.Squads).FirstOrDefault(s => s.TeamId == team.TeamId && s.SquadNumber == squadNumber) is not Squad squad)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "The number entered for the squad is not a valid squad number.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        Match match = week.Matches.First(m => m.Squads.Contains(squad));
        bool wasHomeSquad = match.HomeSquad == squad;
        
        //swap squads in match
        if (wasHomeSquad)
        {
            match.HomeSquad = match.AwaySquad;
            match.AwaySquad = squad;
        }
        else
        {
            match.AwaySquad = match.HomeSquad;
            match.HomeSquad = squad;
        }
        
        //swap winning state if either had won
        if (match.Winner == Match.MatchState.Home) match.Winner = Match.MatchState.Away; else if (match.Winner == Match.MatchState.Away) match.Winner = Match.MatchState.Home;

        //swap game info
        foreach (Game game in match.Games)
        {
            //swap winning state if either had won
            if (game.State == Game.GameState.Home) game.State = Game.GameState.Away; else if (game.State == Game.GameState.Away) game.State = Game.GameState.Home;
            //swap ids of players
            (game.AwayPlayerID, game.HomePlayerID) = (game.HomePlayerID, game.AwayPlayerID);
            //swap wins of players
            (game.AwayPlayerWins, game.HomePlayerWins) = (game.HomePlayerWins, game.AwayPlayerWins);
        }

        await dBContext.SaveChangesAsync();

        Console.WriteLine($"Swapped home and away for {team.TeamName} squad {squad.SquadNumber}'s match in season {season.SeasonNumber} Week {week.WeekNumber}.");
        Embed[] embeds = [(await week.GetDefaultEmbed()).Build()];
        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = $"Swapped home and away for {team.TeamName} squad {squad.SquadNumber}'s match in season {season.SeasonNumber} Week {week.WeekNumber}.";
            mp.Embeds = embeds;
        });
        await dBContext.DisposeAsync();
    }
}
