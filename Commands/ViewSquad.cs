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
internal class ViewSquad : CommandBase
{
    const string COMMANDNAME = "view-squad";

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
            .WithDescription($"Views a squad from the current season.")
            .AddOption(TEAMROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The discord role of the squad's team.", isRequired: true)
            .AddOption(SQUADNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of the squad.", isRequired: true)
            .Build());
    }

    internal async Task CommandExecuted(SocketSlashCommand slashCommand)
    {
        if (slashCommand.Data.Name != COMMANDNAME) return;
        await slashCommand.DeferAsync(ephemeral: true);
        bool isAdmin = IsAdmin(slashCommand);

        LeagueDBContext dBContext = new();

        if (await dBContext.Seasons.FirstOrDefaultAsync(s => s.State == Season.SeasonState.Started) is not Season season)
        {
            if (!isAdmin)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = $"There is no current season!";
                });
                await dBContext.DisposeAsync();
                return;
            }
            if (await dBContext.Seasons.FirstOrDefaultAsync(s => s.State == Season.SeasonState.Unpublished) is not Season unpublishedSeason)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = $"There is no current or unpublished season!";
                });
                await dBContext.DisposeAsync();
                return;
            }
            season = unpublishedSeason;
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

        //Console.WriteLine($"");
        (EmbedBuilder embed, FileAttachment teamLogo) = await squad.GetDefaultEmbed(true, isAdmin);
        Embed[] embeds = [embed.Build()];
        List<FileAttachment> teamLogos = [teamLogo];
        await slashCommand.ModifyOriginalResponseAsync((mp) =>
        {
            mp.Embeds = embeds;
            mp.Attachments = teamLogos;
        });
        await dBContext.DisposeAsync();
    }
}
