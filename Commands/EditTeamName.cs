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

    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        client.SlashCommandExecuted += CommandExecuted;
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

        IRole teamRole = (IRole)slashCommand.Data.Options.First(o => o.Name == TEAMROLEOPTIONNAME).Value;
        if (Program.LeagueDatabase.Teams.FirstOrDefault(team => team.TeamRoleID == teamRole.Id) is not Team team)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That role is not linked to a team!";
            });
            return;
        }

        string teamName = (string)slashCommand.Data.Options.First(o => o.Name == TEAMNAMEOPTIONNAME).Value;
        string oldTeamName = team.TeamName;

        team.TeamName = teamName;
        await Program.LeagueDatabase.SaveChangesAsync();

        Console.WriteLine($"{oldTeamName} name was changed to {teamName}.");
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Content = $"Successfully changed their logo!";
            mp.Embeds = new Embed[] { (await team.GetDefaultEmbed()).Build() };
        });
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Content = $"Successfully changed {oldTeamName} to {teamName}";
            mp.Embeds = new Embed[] { (await team.GetDefaultEmbed()).Build() };
        });
    }
}
