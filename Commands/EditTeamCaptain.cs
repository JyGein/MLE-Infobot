using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

internal class EditTeamCaptain : CommandBase
{
    const string COMMANDNAME = "edit-team-captain";

    const string TEAMROLEOPTIONNAME = "team-role";
    const string TEAMCAPTAINOPTIONNAME = "team-captain";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Edit's an existing team's captain. {Messages.REQUIRESADMIN}")
            .AddOption(TEAMROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The discord role of the team.", isRequired: true)
            .AddOption(TEAMCAPTAINOPTIONNAME, ApplicationCommandOptionType.User, "The discord user who is the captain of the new team.", isRequired: true)
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

        IUser teamCaptain = (IUser)slashCommand.Data.Options.First(o => o.Name == TEAMCAPTAINOPTIONNAME).Value;

        await dBContext.UpdateUserEntry(teamCaptain);
        string oldTeamCaptainUsername = (await dBContext.PlayerNames.FirstAsync(pn => pn.PlayerUserID == team.TeamCaptainID)).GetPlayerName();

        team.TeamCaptainID = teamCaptain.Id;
        await dBContext.SaveChangesAsync();
        await dBContext.DisposeAsync();

        Console.WriteLine($"{team.TeamName} captain was changed from {oldTeamCaptainUsername ?? teamCaptain.Username} to {teamCaptain.GlobalName ?? teamCaptain.Username}.");
        (EmbedBuilder embedbuilder, FileAttachment teamLogoAttachment) = await team.GetDefaultEmbed();
        Embed[] embed = [embedbuilder.Build()];
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Content = $"Successfully changed {team.TeamName} captain from {oldTeamCaptainUsername ?? teamCaptain.Username} to {teamCaptain.GlobalName ?? teamCaptain.Username}.";
            mp.Embeds = embed;
            mp.Attachments = new List<FileAttachment> { teamLogoAttachment };
        });
    }
}
