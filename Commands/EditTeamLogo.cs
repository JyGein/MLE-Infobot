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

    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        client.SlashCommandExecuted += CommandExecuted;
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription("Edit's an existing team's logo. Will fail if you are not a league admin.")
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

        IRole teamRole = (IRole)slashCommand.Data.Options.First(o => o.Name == TEAMROLEOPTIONNAME).Value;
        if (Program.LeagueDatabase.Teams.FirstOrDefault(team => team.TeamRoleID == teamRole.Id) is not Team team)
        {
            await slashCommand.RespondAsync("That role is not linked to a team!", ephemeral: true);
            return;
        }

        IAttachment teamLogo = (IAttachment)slashCommand.Data.Options.First(o => o.Name == TEAMLOGOOPTIONNAME).Value;
        if (!teamLogo.ContentType.Contains("image"))
        {
            await slashCommand.RespondAsync("The team-logo must be an image!\nThe team was not created.", ephemeral: true);
            return;
        }
        string oldTeamLogo = team.TeamLogoURL;

        team.TeamLogoURL = teamLogo.Url;
        await Program.LeagueDatabase.SaveChangesAsync();

        Console.WriteLine($"{team.TeamName} logo was changed from {oldTeamLogo} to {teamLogo.Url}.");
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Content = $"Successfully changed their logo!";
            mp.Embeds = new Embed[] { (await team.GetDefaultEmbed()).Build() };
        });
    }
}
