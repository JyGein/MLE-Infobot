using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

/// <summary>
/// just for copy pasting to make more comamnds
/// </summary>
internal class CommandTemplate : CommandBase
{
    const string COMMANDNAME = "commandName";

    const string OPTIONNAME = "optionName";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"commandDescription. {Messages.REQUIRESADMIN}")
            .AddOption(OPTIONNAME, ApplicationCommandOptionType.String, "optionDescription", isRequired: true)
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

        string input = (string)slashCommand.Data.Options.First(o => o.Name == OPTIONNAME).Value;


        await dBContext.SaveChangesAsync();
        await dBContext.DisposeAsync();

        //Console.WriteLine($"");
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Content = "output";
        });
    }
}
