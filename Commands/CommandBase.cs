using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

internal abstract class CommandBase
{
    public abstract Task RegisterCommand(DiscordSocketClient client, SocketGuild guild);

    public static bool IsAdmin(SocketSlashCommand slashCommand)
    {
        SocketGuild guild = Program.Client.GetGuild(ulong.Parse(Environment.GetEnvironmentVariable("GUILD_ID")!));
        SocketGuildUser user = guild.GetUser(slashCommand.User.Id);
        if (user.GuildPermissions.Administrator) return true;
        if (user.Roles.Any(role => role.Id == ulong.Parse(Environment.GetEnvironmentVariable("ADMIN_ROLE")!))) return true;
        return false;
    }
}
