using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

internal class AddTeam : CommandBase
{
    const string COMMANDNAME = "add-team";

    const string TEAMROLEOPTIONNAME = "team-role";
    const string TEAMNAMEOPTIONNAME = "team-name";
    const string TEAMLOGOOPTIONNAME = "team-logo";
    const string TEAMCAPTAINOPTIONNAME = "team-captain";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Adds a team to the league. {Messages.REQUIRESADMIN}")
            .AddOption(TEAMROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The discord role of the new team.", isRequired: true)
            .AddOption(TEAMNAMEOPTIONNAME, ApplicationCommandOptionType.String, "The name of the new team.", isRequired: true)
            .AddOption(TEAMLOGOOPTIONNAME, ApplicationCommandOptionType.Attachment, "The logo of the new team.", isRequired: true)
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
        if (dBContext.Teams.Any(team => team.TeamRoleID == teamRole.Id))
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That role is already linked to a team!";
            });
            return;
        }
        string teamName = (string)slashCommand.Data.Options.First(o => o.Name == TEAMNAMEOPTIONNAME).Value;
        IAttachment teamLogo = (IAttachment)slashCommand.Data.Options.First(o => o.Name == TEAMLOGOOPTIONNAME).Value;
        if (!teamLogo.ContentType.Contains("image"))
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "The team-logo must be an image!\nThe team was not created.";
            });
            return;
        }
        string teamLogoPath = await Program.SaveImage(teamLogo.Url, teamName + "Logo");
        IUser teamCaptain = (IUser)slashCommand.Data.Options.First(o => o.Name == TEAMCAPTAINOPTIONNAME).Value;

        await dBContext.UpdateUserEntry(teamCaptain);
        Team team = new() { TeamCaptainID = teamCaptain.Id, TeamName = teamName, TeamLogoURL = teamLogoPath, TeamRoleID = teamRole.Id };
        await dBContext.AddAsync(team);
        await dBContext.SaveChangesAsync();

        Console.WriteLine($"New team created:\nTeam name: {teamName}\nTeam logo: {teamLogoPath}\nTeam captain: {teamCaptain.GlobalName ?? teamCaptain.Username}");
        (EmbedBuilder embedbuilder, FileAttachment teamLogoAttachment) = await team.GetDefaultEmbed();
        Embed[] embed = [embedbuilder.Build()];
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Content = "New team added to the league!";
            mp.Embeds = embed;
            mp.Attachments = new List<FileAttachment> { teamLogoAttachment };
        });

        await dBContext.DisposeAsync();
    }
}
