using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

internal abstract partial class CommandBase
{
    public abstract Task RegisterCommand(DiscordSocketClient client, SocketGuild guild);
    public abstract Task SubscribeCommand(DiscordSocketClient client);

    public static bool IsAdmin(SocketSlashCommand slashCommand)
    {
        SocketGuildUser user = Program.Guild.GetUser(slashCommand.User.Id);
        return IsAdmin(user);
    }

    public static bool IsAdmin(SocketMessageComponent messageComponent)
    {
        SocketGuildUser user = Program.Guild.GetUser(messageComponent.User.Id);
        return IsAdmin(user);
    }

    public static bool IsAdmin(SocketGuildUser user)
    {
        if (user.GuildPermissions.Administrator) return true;
        if (user.Roles.Any(role => role.Id == ulong.Parse(Environment.GetEnvironmentVariable("ADMIN_ROLE")!))) return true;
        return false;
    }

    [GeneratedRegex("[\\w-]+:(yes|no):(\\d+)")]
    internal static partial Regex RemoveTeamInteractionIdPattern();

    [GeneratedRegex("\\w+:(\\d+):(\\d+)")]
    internal static partial Regex ViewSeasonInteractionIDPattern();

    internal class Messages
    {
        internal const string REQUIRESADMIN = "Requires league administrator privileges.";
    }
}
