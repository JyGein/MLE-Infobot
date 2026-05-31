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
internal class RemoveSquad : CommandBase
{
    const string COMMANDNAME = "remove-squad";

    const string TEAMROLEOPTIONNAME = "team-role";
    const string SQUADNUMBEROPTIONNAME = "squad-number";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Removes a squad from the unpublished season. {Messages.REQUIRESADMIN}")
            .AddOption(TEAMROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The discord role of the squad's team.", isRequired: true)
            .AddOption(SQUADNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of the squad.", isRequired: true)
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

        if (await dBContext.Seasons.FirstOrDefaultAsync(s => s.State == Season.SeasonState.Unpublished) is not Season season)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "There is not an unpublished season.";
            });
            await dBContext.DisposeAsync();
            return;
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

        long squadNumber = (long)slashCommand.Data.Options.First(o => o.Name == SQUADNUMBEROPTIONNAME).Value;
        if ((await dBContext.Entry(season).Collection(s => s.Divisions).Query().Include(d => d.Squads).ToListAsync()).SelectMany(d => d.Squads).FirstOrDefault(s => s.TeamId == team.TeamId && s.SquadNumber == squadNumber) is not Squad squad)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That is not a valid squad number";
            });
            await dBContext.DisposeAsync();
            return;
        }

        await DeleteSquad(squad);

        await dBContext.SaveChangesAsync();

        await season.RandomizeGuaranteedMatches();

        await dBContext.SaveChangesAsync();

        Console.WriteLine($"Removed squad number {squadNumber} on team {team.TeamName} from the unpublished season.");
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Content = $"Removed squad number {squadNumber} on team {team.TeamName} from the unpublished season.";
        });
        await dBContext.DisposeAsync();
    }

    public static async Task DeleteSquad(Squad yourContextSquad)
    {
        LeagueDBContext dBContext = new();
        Squad squad = (Squad)(await dBContext.FindAsync(yourContextSquad.GetType(), yourContextSquad.SquadId))!;
        Season season = (await dBContext.Entry(squad).Reference(s => s.Division).Query().Include(d => d.Season).SingleAsync()).Season;
        if ((await dBContext.Entry(season).Collection(s => s.Divisions).Query().Include(d => d.Squads).ToListAsync()).SelectMany(d => d.Squads).Where(s => s.TeamId == squad.TeamId && s.SquadId != squad.SquadId).OrderBy(s => s.SquadNumber).ToList() is List<Squad> otherSquads && otherSquads.Count > 0)
        {
            //otherSquads.ForEach(s => Console.WriteLine($"squad: {s.SquadNumber}"));
            for (int i = 1; i <= otherSquads.Count; i++)
            {
                otherSquads[i - 1].SquadNumber = i;
            }
        }
        dBContext.Entry(squad).State = EntityState.Deleted;
        await dBContext.SaveChangesAsync();
        await dBContext.DisposeAsync();
    }
}
