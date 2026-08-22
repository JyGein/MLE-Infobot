using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

/// <summary>
/// just for copy pasting to make more comamnds
/// </summary>
internal class Unsubstitute : CommandBase
{
    const string COMMANDNAME = "unsubstitute";

    const string TEAMROLEOPTIONNAME = "team-role";
    const string SQUADNNUMBEROPTIONNAME = "squad-number";
    const string SUBOPTIONNAME = "substitute";
    const string WEEKNUMBEROPTIONNAME = "week-number";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Unsubstitutes a player for the next week. {Messages.REQUIRESADMIN}")
            .AddOption(TEAMROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The role of the team of the squad.", isRequired: true)
            .AddOption(SQUADNNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of the squad.", isRequired: true)
            .AddOption(SUBOPTIONNAME, ApplicationCommandOptionType.User, "The substitute to be unsubbed.", isRequired: true)
            .AddOption(WEEKNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The week to unsub. Defaults to the next week.")
            .Build());
    }

    internal async Task CommandExecuted(SocketSlashCommand slashCommand)
    {
        Program.BotLog("meow");
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
            if (await dBContext.Seasons.Include(s => s.SeasonWeeks).Include(s => s.PlayoffWeeks).FirstOrDefaultAsync(s => s.State == Season.SeasonState.Unpublished) is not Season nextSeason)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "There is no current or unpublished season!";
                });
                await dBContext.DisposeAsync();
                return;
            }
            season = nextSeason;
        }

        Week week = null!;
        if (slashCommand.Data.Options.FirstOrDefault(o => o.Name == WEEKNUMBEROPTIONNAME) is SocketSlashCommandDataOption weekNumberOption)
        {
            if (season.AllWeeks.FirstOrDefault(w => w.WeekNumber == (long)weekNumberOption.Value) is not Week w)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "That is not a valid week number.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            week = w;
        }
        else
        {
            if (season.AllWeeks.FirstOrDefault(w => w.State == Week.WeekState.Current) is Week currentWeek)
            {
                if (season.AllWeeks.FirstOrDefault(w => w.WeekNumber == currentWeek.WeekNumber + 1) is not Week nextWeek)
                {
                    await slashCommand.ModifyOriginalResponseAsync((mp) =>
                    {
                        mp.Content = "There is no next week, generate it first!";
                    });
                    await dBContext.DisposeAsync();
                    return;
                }
                week = nextWeek;
            }
            else
            {
                if (season.AllWeeks.FirstOrDefault(w => w.WeekNumber == 1) is not Week firstWeek)
                {
                    await slashCommand.ModifyOriginalResponseAsync((mp) =>
                    {
                        mp.Content = "There are no weeks.";
                    });
                    await dBContext.DisposeAsync();
                    return;
                }
                week = firstWeek;
            }
        }

        IRole teamRole = (IRole)slashCommand.Data.Options.First(o => o.Name == TEAMROLEOPTIONNAME).Value;
        if (await dBContext.Teams.FirstOrDefaultAsync(t => t.TeamRoleID == teamRole.Id) is not Team team)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That role is not linked to a team.";
            });
            await dBContext.DisposeAsync();
            return;
        }

        await dBContext.Entry(week).Collection(w => w.Matches).Query().Include(m => m.HomeSquad).Include(m => m.AwaySquad).LoadAsync();
        long squadNumber = (long)slashCommand.Data.Options.First(o => o.Name == SQUADNNUMBEROPTIONNAME).Value;
        if (week.Matches.FirstOrDefault(m => m.Squads.Any(s => s.TeamId == team.TeamId && s.SquadNumber == squadNumber)) is not Match match)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That is not a valid squad number!";
            });
            await dBContext.DisposeAsync();
            return;
        }
        Squad squad = match.Squads.First(s => s.TeamId == team.TeamId && s.SquadNumber == squadNumber);

        await dBContext.Entry(squad).Collection(s => s.PlayerIDs).LoadAsync();
        await dBContext.Entry(squad).Collection(s => s.SubstituteIDs).LoadAsync();
        await dBContext.Entry(match).Collection(m => m.Substitutions).LoadAsync();
        await dBContext.Entry(match).Collection(m => m.Games).LoadAsync();
        IUser sub = (IUser)slashCommand.Data.Options.First(o => o.Name == SUBOPTIONNAME).Value;

        if (match.Substitutions.FirstOrDefault(s => s.SubstituteID == sub.Id) is not Substitution substitution)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That sub is not subbed in!";
            });
            await dBContext.DisposeAsync();
            return;
        }

        dBContext.Entry(substitution).State = EntityState.Deleted;

        await dBContext.SaveChangesAsync();

        string message = $"{sub.GlobalName ?? sub.Username} has been unsubbed in Week {week.WeekNumber}.";
        Console.WriteLine(message);
        Embed[] embeds = [(await match.GetDefaultEmbed()).Build()];
        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Content = message;
            mp.Embeds = embeds;
        });
        await dBContext.DisposeAsync();
    }
}
