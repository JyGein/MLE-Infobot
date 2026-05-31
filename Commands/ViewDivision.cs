using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

/// <summary>
/// just for copy pasting to make more comamnds
/// </summary>
internal class ViewDivision : CommandBase
{
    const string COMMANDNAME = "view-division";

    const string SEASONNUMBEROPTIONNAME = "season-number";
    const string DIVISIONNAMEOPTIONNAME = "division-name";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"View all divisions, or the squads on a division.")
            .AddOption(SEASONNUMBEROPTIONNAME, ApplicationCommandOptionType.Integer, "The number of the season you want to view. Defaults to current season.")
            .AddOption(DIVISIONNAMEOPTIONNAME, ApplicationCommandOptionType.String, "The name of the division you want to view.")
            .Build());
    }

    internal async Task CommandExecuted(SocketSlashCommand slashCommand)
    {
        if (slashCommand.Data.Name != COMMANDNAME) return;
        await slashCommand.DeferAsync(ephemeral: true);
        bool isAdmin = IsAdmin(slashCommand);

        LeagueDBContext dBContext = new();

        Season season = null!;
        if (slashCommand.Data.Options.FirstOrDefault(o => o.Name == SEASONNUMBEROPTIONNAME) is SocketSlashCommandDataOption seasonNumberOption)
        {
            if (dBContext.Seasons.FirstOrDefault(s => s.SeasonNumber == (long)seasonNumberOption.Value) is not Season s || (s.State == Season.SeasonState.Unpublished && !isAdmin))
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "That season does not exist!";
                });
                await dBContext.DisposeAsync();
                return;
            }
            season = s;
        }
        else
        {
            List<Season> potentialSeasons = [.. dBContext.Seasons.Where(s => (isAdmin && s.State == Season.SeasonState.Unpublished) || s.State == Season.SeasonState.Started).OrderByDescending(s => s.SeasonNumber)];
            if (potentialSeasons.Count == 0)
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "There is no season to view.";
                });
                await dBContext.DisposeAsync();
                return;
            }
            season = potentialSeasons.First();
        }
        await dBContext.Entry(season)
            .Collection(s => s.Divisions)
            .LoadAsync();
        Division? division = null;
        if (slashCommand.Data.Options.FirstOrDefault(o => o.Name == DIVISIONNAMEOPTIONNAME) is SocketSlashCommandDataOption divisionNameOption)
        {
            if (season.Divisions.FirstOrDefault(d => d.DivisionName.Equals(((string)divisionNameOption.Value).Trim(), StringComparison.CurrentCultureIgnoreCase)) is Division d)
            {
                division = d;
            }
            else
            {
                await slashCommand.ModifyOriginalResponseAsync((mp) =>
                {
                    mp.Content = "That is not a valid division name.";
                });
                await dBContext.DisposeAsync();
                return;
            }
        }
        Embed[] embeds = [];
        List<FileAttachment> teamLogos = [];
        if (division != null)
        {
            (List<EmbedBuilder> divisionEmbeds, List<FileAttachment> divisionTeamLogos) = await division.GetSquadsEmbeds();
            embeds = [.. embeds.Concat(divisionEmbeds.Select(eb => eb.Build()))];
            teamLogos = [.. teamLogos.Concat(divisionTeamLogos)];
        }
        else
        {
            embeds = [.. embeds.Append((await season.GetDivisionsEmbed()).Build())];
        }

        //Console.WriteLine($"");
        await slashCommand.ModifyOriginalResponseAsync(async (mp) =>
        {
            mp.Embeds = embeds;
            if (teamLogos.Count > 0) mp.Attachments = teamLogos;
        });

        await dBContext.DisposeAsync();
    }
}
