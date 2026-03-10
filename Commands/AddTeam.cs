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
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        client.SlashCommandExecuted += CommandExecuted;
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription("Adds a team to the league. Will fail if you are not a league admin.")
            .AddOption("team-name", ApplicationCommandOptionType.Role, "The discord role of the new team.", isRequired: true)
            .AddOption("team-logo", ApplicationCommandOptionType.Attachment, "The logo of the new team.", isRequired: true)
            .AddOption("team-captain", ApplicationCommandOptionType.User, "The discord user who is the captain of the new team.", isRequired: true)
            .Build());
    }

    internal async Task CommandExecuted(SocketSlashCommand slashCommand)
    {
        if (slashCommand.Data.Name != COMMANDNAME) return;
        //await slashCommand.DeferAsync(ephemeral: true);

        IRole teamRole = (IRole)slashCommand.Data.Options.ToList()[0].Value;
        IAttachment teamLogo = (IAttachment)slashCommand.Data.Options.ToList()[1].Value;
        if (!teamLogo.ContentType.Contains("image"))
        {
            Console.WriteLine(teamLogo.ContentType);
            await slashCommand.RespondAsync("The team-logo must be an image!\nThe team was not created.", ephemeral: true);
            return;
        }
        IUser teamCaptain = (IUser)slashCommand.Data.Options.ToList()[2].Value;
        await Program.LeagueDatabase.AddAsync(new Team { TeamCaptainID = teamCaptain.Id, TeamLogoURL = teamLogo.Url, TeamRoleID = teamRole.Id });
        await Program.LeagueDatabase.SaveChangesAsync();
        Console.WriteLine("New team created");
        Console.WriteLine($"Team name: {teamRole.Name}\nTeam logo: {teamLogo.Url}\nTeam captain: {teamCaptain.GlobalName}");
        await slashCommand.RespondAsync("New team added to the league!", ephemeral: true, embed: new EmbedBuilder()
            .WithTitle(teamRole.Mention)
            .WithColor(teamRole.Color)
            .WithImageUrl(teamLogo.Url)
            .Build());
    }
}
