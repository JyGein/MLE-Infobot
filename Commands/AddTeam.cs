using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
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

    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        client.SlashCommandExecuted += CommandExecuted;
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription("Adds a team to the league. Will fail if you are not a league admin.")
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
        //Defering is needed if it takes longer than 3 seconds to process the command, does require using slashCommand.ModifyOriginalResponseAsync()
        //await slashCommand.DeferAsync(ephemeral: true);
        IRole teamRole = (IRole)slashCommand.Data.Options.First(o => o.Name == TEAMROLEOPTIONNAME).Value;
        if (Program.LeagueDatabase.Teams.Any(team => team.TeamRoleID == teamRole.Id))
        {
            await slashCommand.RespondAsync("That role is already linked to a team!", ephemeral: true);
            return;
        }
        string teamName = (string)slashCommand.Data.Options.First(o => o.Name == TEAMNAMEOPTIONNAME).Value;
        IAttachment teamLogo = (IAttachment)slashCommand.Data.Options.First(o => o.Name == TEAMLOGOOPTIONNAME).Value;
        if (!teamLogo.ContentType.Contains("image"))
        {
            await slashCommand.RespondAsync("The team-logo must be an image!\nThe team was not created.", ephemeral: true);
            return;
        }
        IUser teamCaptain = (IUser)slashCommand.Data.Options.First(o => o.Name == TEAMCAPTAINOPTIONNAME).Value;
        await Program.LeagueDatabase.AddAsync(new Team { TeamCaptainID = teamCaptain.Id, TeamName = teamName, TeamLogoURL = teamLogo.Url, TeamRoleID = teamRole.Id });
        await Program.LeagueDatabase.SaveChangesAsync();
        Console.WriteLine("New team created");
        Console.WriteLine($"Team name: {teamName}\nTeam logo: {teamLogo.Url}\nTeam captain: {teamCaptain.GlobalName}");
        await slashCommand.RespondAsync("New team added to the league!", ephemeral: true, embed: new EmbedBuilder()
            .WithTitle(teamRole.Mention)
            .WithColor(teamRole.Color)
            .WithImageUrl(teamLogo.Url)
            .Build());
    }
}
