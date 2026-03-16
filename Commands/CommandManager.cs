using Discord;
using Discord.Net;
using Discord.WebSocket;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

internal class CommandManager
{
    public static List<CommandBase> Commands = [
        new AddTeam(),
        new EditTeamName(),
        new EditTeamLogo(),
        new EditTeamCaptain(),
        new RemoveTeam()
        ];
    public static async Task CreateCommands(DiscordSocketClient client)
    {
        //We only care about the server that this bot will be deployed into, the released bot will have the MLE server id in their environment.
        SocketGuild guild = Program.Guild;
        await guild.DeleteApplicationCommandsAsync();
        
        foreach (CommandBase command in Commands)
        {
            try
            {
                // Now that we have our builder, we can call the CreateApplicationCommandAsync method to make our slash command.
                await command.RegisterCommand(client, guild);
                // Using the ready event is a simple implementation for the sake of the example. Suitable for testing and development.
                // For a production bot, it is recommended to only run the CreateGlobalApplicationCommandAsync() once for each command.
            }
            catch (HttpException exception)
            {
                // If our command was invalid, we should catch an ApplicationCommandException. This exception contains the path of the error as well as the error message. You can serialize the Error field in the exception to get a visual of where your error is.
                var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);

                // You can send this error somewhere or just print it to the console, for this example we're just going to print it.
                Console.WriteLine(json);
            }
        }
    }
}
