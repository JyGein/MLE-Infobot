using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

internal class EditTeamLogo : CommandBase
{
    const string COMMANDNAME = "edit-team-logo";

    const string TEAMROLEOPTIONNAME = "team-role";
    const string TEAMLOGOOPTIONNAME = "team-logo";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Edit's an existing team's logo. {Messages.REQUIRESADMIN}")
            .AddOption(TEAMROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The discord role of the team.", isRequired: true)
            .AddOption(TEAMLOGOOPTIONNAME, ApplicationCommandOptionType.Attachment, "The new logo of the team.", isRequired: true)
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

        IAttachment teamLogo = (IAttachment)slashCommand.Data.Options.First(o => o.Name == TEAMLOGOOPTIONNAME).Value;
        if (!teamLogo.ContentType.Contains("image"))
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "The team-logo must be an image!\nThe logo was not changed.";
            });
            await dBContext.DisposeAsync();
            return;
        }
        string teamLogoPath = await Program.SaveImage(teamLogo.Url, team.TeamName + "Logo");

        team.TeamLogoURL = teamLogoPath;
        await dBContext.SaveChangesAsync();

        Console.WriteLine($"{team.TeamName} logo was changed.");
        (EmbedBuilder embedbuilder, FileAttachment teamLogoAttachment) = await team.GetDefaultEmbed();
        Embed[] embed = [embedbuilder.Build()];
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Content = $"Successfully changed their logo!";
            mp.Embeds = embed;
            mp.Attachments = new List<FileAttachment> { teamLogoAttachment };
        });

        await dBContext.DisposeAsync();
    }
}
