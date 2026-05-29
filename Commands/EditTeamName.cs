using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

internal class EditTeamName : CommandBase
{
    const string COMMANDNAME = "edit-team-name";

    const string TEAMROLEOPTIONNAME = "team-role";
    const string TEAMNAMEOPTIONNAME = "team-name";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Edit's an existing team's name. {Messages.REQUIRESADMIN}")
            .AddOption(TEAMROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The discord role of the team.", isRequired: true)
            .AddOption(TEAMNAMEOPTIONNAME, ApplicationCommandOptionType.String, "The new name of the team.", isRequired: true)
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

        string teamName = (string)slashCommand.Data.Options.First(o => o.Name == TEAMNAMEOPTIONNAME).Value;
        string oldTeamName = team.TeamName;

        team.TeamName = teamName;
        await dBContext.SaveChangesAsync();
        await dBContext.DisposeAsync();

        Console.WriteLine($"{oldTeamName} name was changed to {teamName}.");
        (EmbedBuilder embedbuilder, FileAttachment teamLogoAttachment) = await team.GetDefaultEmbed();
        Embed[] embed = [embedbuilder.Build()];
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Content = $"Successfully changed {oldTeamName} to {teamName}";
            mp.Embeds = embed;
            mp.Attachments = new List<FileAttachment> { teamLogoAttachment };
        });
    }
}
