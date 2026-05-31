using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

internal class EditSquadDivision : CommandBase
{
    const string COMMANDNAME = "edit-squad-division";

    const string TEAMROLEOPTIONNAME = "team-role";
    const string SQUADNUMBEROPTIONNAME = "squad-number";
    const string DIVISIONNAMEOPTIONNAME = "division-name";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Edit a squad's division. {Messages.REQUIRESADMIN}")
            .AddOption(TEAMROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The discord role of the squad's team.", isRequired: true)
            .AddOption(SQUADNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of the squad.", isRequired: true)
            .AddOption(DIVISIONNAMEOPTIONNAME, ApplicationCommandOptionType.String, "The new division name of the squad.", isRequired: true)
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

        string newDivisionName = ((string)slashCommand.Data.Options.First(o => o.Name == DIVISIONNAMEOPTIONNAME).Value).Trim();
        if ((await dBContext.Entry(season).Collection(s => s.Divisions).Query().ToListAsync()).FirstOrDefault(d => d.DivisionName.Equals(newDivisionName, StringComparison.CurrentCultureIgnoreCase)) is not Division division)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That is not a valid division name";
            });
            await dBContext.DisposeAsync();
            return;
        }

        squad.Division = division;

        await dBContext.SaveChangesAsync();

        Console.WriteLine($"Squad number {squadNumber} on team {team.TeamName} change to division {division.DivisionName}.");
        (EmbedBuilder embedBuilder, FileAttachment teamLogo) = await squad.GetDefaultEmbed(true);
        Embed[] embed = [embedBuilder.Build()];
        List<FileAttachment> teamLogos = [teamLogo];
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Content = $"Squad number {squadNumber} on team {team.TeamName} change to division {division.DivisionName}.";
            mp.Embeds = embed;
            mp.Attachments = teamLogos;
        });

        await dBContext.DisposeAsync();
    }
}
