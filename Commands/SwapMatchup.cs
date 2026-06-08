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
internal class SwapMatchup : CommandBase
{
    const string COMMANDNAME = "swap-matchup";

    const string WEEKNUMBEROPTIONNAME = "week-number";
    const string TEAM1ROLEOPTIONNAME = "team1-role";
    const string SQUAD1NUMBEROPTIONNAME = "squad1-number";
    const string TEAM2ROLEOPTIONNAME = "team2-role";
    const string SQUAD2NUMBEROPTIONNAME = "squad2-number";
    const string SEASONNUMBEROPTIONNAME = "season-number";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Swaps two matchups in a week. {Messages.REQUIRESADMIN}")
            .AddOption(WEEKNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of the week to edit.", isRequired: true)
            .AddOption(TEAM1ROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The role of the first squad's team.", isRequired: true)
            .AddOption(SQUAD1NUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of the first squad.", isRequired: true)
            .AddOption(TEAM2ROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The role of the second squad's team.", isRequired: true)
            .AddOption(SQUAD2NUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of the second squad.", isRequired: true)
            .AddOption(SEASONNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of the season to edit. Defaults to the unpublished season then current season.")
            .Build());
    }

    internal async Task CommandExecuted(SocketSlashCommand slashCommand)
    {
        if (slashCommand.Data.Name != COMMANDNAME) return;
        bool isAdmin = IsAdmin(slashCommand);
        if (!isAdmin)
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

        IRole team1Role = (IRole)slashCommand.Data.Options.First(o => o.Name == TEAM1ROLEOPTIONNAME).Value;
        if (await dBContext.Teams.FirstOrDefaultAsync(t => t.TeamRoleID == team1Role.Id) is not Team team1)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "The role entered for squad 1's team is not linked to a team.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        await dBContext.Entry(week).Collection(w => w.Matches).Query().Include(m => m.HomeSquad).Include(m => m.AwaySquad).Include(m => m.Substitutions).Include(m => m.Games).LoadAsync();
        long squad1Number = (long)slashCommand.Data.Options.First(o => o.Name == SQUAD1NUMBEROPTIONNAME).Value;
        if (week.Matches.SelectMany(m => m.Squads).FirstOrDefault(s => s.TeamId == team1.TeamId && s.SquadNumber == squad1Number) is not Squad squad1)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "The number entered for squad 1 is not a valid squad number.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        IRole team2Role = (IRole)slashCommand.Data.Options.First(o => o.Name == TEAM2ROLEOPTIONNAME).Value;
        if (await dBContext.Teams.FirstOrDefaultAsync(t => t.TeamRoleID == team2Role.Id) is not Team team2)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "The role entered for squad 2's team is not linked to a team.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        long squad2Number = (long)slashCommand.Data.Options.First(o => o.Name == SQUAD2NUMBEROPTIONNAME).Value;
        if (week.Matches.SelectMany(m => m.Squads).FirstOrDefault(s => s.TeamId == team2.TeamId && s.SquadNumber == squad2Number) is not Squad squad2)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "The number entered for squad 2 is not a valid squad number.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        Match match1 = week.Matches.First(m => m.Squads.Contains(squad1));
        bool squad1IsHomeSquad = match1.HomeSquad == squad1;
        Match match2 = week.Matches.First(m => m.Squads.Contains(squad2));
        bool squad2IsHomeSquad = match2.HomeSquad == squad2;
        match1.Winner = Match.MatchState.Undecided;
        match2.Winner = Match.MatchState.Undecided;

        if (squad1IsHomeSquad) match1.HomeSquad = squad2; else match1.AwaySquad = squad2;
        if (squad2IsHomeSquad) match2.HomeSquad = squad1; else match2.AwaySquad = squad1;

        foreach (Substitution sub in match1.Substitutions.Where(sub => dBContext.Entry(squad1).Collection(s => s.PlayerIDs).Query().Select(psp => psp.PlayerID).Contains(sub.PlayerID)))
        {
            match1.Substitutions.Remove(sub);
            sub.Match = match2;
            match2.Substitutions.Add(sub);
        }
        foreach (Substitution sub in match2.Substitutions.Where(sub => dBContext.Entry(squad2).Collection(s => s.PlayerIDs).Query().Select(psp => psp.PlayerID).Contains(sub.PlayerID)))
        {
            match2.Substitutions.Remove(sub);
            sub.Match = match1;
            match1.Substitutions.Add(sub);
        }

        if (week.HasBeenGenerated)
        {
            foreach (Match match in new List<Match> { match1, match2 })
            {
                match.Squads.ForEach(async s => await dBContext.Entry(s).Collection(s => s.PlayerIDs).LoadAsync());
                match.Games.ForEach(g => dBContext.Entry(g).State = EntityState.Deleted);
                match.Games.Clear();
                foreach (int i in week.Players123Mappings)
                {
                    match.Games.Add(new() { Match = match, HomePlayerID = match.HomeSquad.PlayerIDs[match.Games.Count], AwayPlayerID = match.AwaySquad.PlayerIDs[i-1] });
                }
            }
        }

        await dBContext.SaveChangesAsync();

        Console.WriteLine($"Swapped {team1.TeamName} squad {squad1.SquadNumber} and {team2.TeamName} squad {squad2.SquadNumber}'s matchups in week {week.WeekNumber} of season {season.SeasonNumber}.");
        Embed[] embeds = [.. (await week.GetEmbed()).Select(eb => eb.Build())];
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Content = $"Swapped {team1.TeamName} squad {squad1.SquadNumber} and {team2.TeamName} squad {squad2.SquadNumber}'s matchups in week {week.WeekNumber} of season {season.SeasonNumber}.";
            mp.Embeds = embeds;
        });
        await dBContext.DisposeAsync();
    }
}
