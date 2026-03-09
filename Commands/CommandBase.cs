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
}
