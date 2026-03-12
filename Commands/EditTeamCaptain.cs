using Discord;
using Discord.WebSocket;
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

    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        client.SlashCommandExecuted += CommandExecuted;
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription("Edit's an existing team's captain. Will fail if you are not a league admin.")
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

        IRole teamRole = (IRole)slashCommand.Data.Options.First(o => o.Name == TEAMROLEOPTIONNAME).Value;
        if (Program.LeagueDatabase.Teams.FirstOrDefault(team => team.TeamRoleID == teamRole.Id) is not Team team)
        {
            await slashCommand.RespondAsync("That role is not linked to a team!", ephemeral: true);
            return;
        }

        IUser teamCaptain = (IUser)slashCommand.Data.Options.First(o => o.Name == TEAMCAPTAINOPTIONNAME).Value;

        ulong oldTeamCaptainId = team.TeamCaptainID;
        SocketGuildUser oldTeamCaptain = Program.Guild.GetUser(oldTeamCaptainId);

        team.TeamCaptainID = teamCaptain.Id;
        await Program.LeagueDatabase.SaveChangesAsync();

        Console.WriteLine($"{team.TeamName} captain was changed from {oldTeamCaptain.GlobalName ?? teamCaptain.Username} to {teamCaptain.GlobalName ?? teamCaptain.Username}.");
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Content = $"Successfully changed {team.TeamName} captain from {oldTeamCaptain.GlobalName ?? teamCaptain.Username} to {teamCaptain.GlobalName ?? teamCaptain.Username}.";
            mp.Embeds = new Embed[] { (await team.GetDefaultEmbed()).Build() };
        });
    }
}
