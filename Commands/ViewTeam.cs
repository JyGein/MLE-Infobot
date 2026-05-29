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
internal class ViewTeam : CommandBase
{
    const string COMMANDNAME = "view-team";

    const string TEAMROLEOPTIONNAME = "team-role";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Views a team's information.")
            .AddOption(TEAMROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The role of the team you want to view.", isRequired: true)
            .Build());
    }

    internal async Task CommandExecuted(SocketSlashCommand slashCommand)
    {
        if (slashCommand.Data.Name != COMMANDNAME) return;
        await slashCommand.DeferAsync(ephemeral: true);

        LeagueDBContext dBContext = new();

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

        //Console.WriteLine($"");
        (EmbedBuilder embedbuilder, FileAttachment teamLogoAttachment) = await team.GetDefaultEmbed();
        Embed[] embed = [embedbuilder.Build()];
        List<FileAttachment> fileAttachments = [teamLogoAttachment];
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Attachments = fileAttachments;
            mp.Embeds = embed;
        });

        await dBContext.DisposeAsync();
    }
}
