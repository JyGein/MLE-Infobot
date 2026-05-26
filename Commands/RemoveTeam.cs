using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MLE_Infobot.Commands;

internal partial class RemoveTeam : CommandBase
{
    const string COMMANDNAME = "remove-team";

    const string TEAMROLEOPTIONNAME = "team-role";

    public override async Task SubscribeCommand(DiscordSocketClient client)
    {
        client.SlashCommandExecuted += CommandExecuted;
        client.ButtonExecuted += ButtonClicked;
    }
    public override async Task RegisterCommand(DiscordSocketClient client, SocketGuild guild)
    {
        await guild.CreateApplicationCommandAsync(new SlashCommandBuilder()
            .WithName(COMMANDNAME)
            .WithDescription($"Unlinks the team from their role. {Messages.REQUIRESADMIN}")
            .AddOption(TEAMROLEOPTIONNAME, ApplicationCommandOptionType.Role, "The discord role of the team.", isRequired: true)
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
        LeagueDBContext dBContext = new();
        await slashCommand.DeferAsync(ephemeral: true);

        IRole teamRole = (IRole)slashCommand.Data.Options.First(o => o.Name == TEAMROLEOPTIONNAME).Value;
        if (dBContext.Teams.FirstOrDefault(team => team.TeamRoleID == teamRole.Id) is not Team team)
        {
            await slashCommand.ModifyOriginalResponseAsync((mp) =>
            {
                mp.Content = "That role is not linked to a team!";
            });
            return;
        }

        IUserMessage confirmationMessage = await slashCommand.Channel.SendMessageAsync(
            text: "Careful! This is a dangerous command!\n" +
                "If the team is not in a published season the team will just removed and the associated role will be freed.\n" +
                "Otherwise, if the team is in a publish season the team will auto-lose all of their matches and will no longer be accessable through commands in order to unlink the role from the team.\n" +
                "Are you sure you want to do this?",
            components: new ComponentBuilder()
            .WithButton("Yes", COMMANDNAME + ":yes:" + slashCommand.Id, ButtonStyle.Success)
            .WithButton("No", COMMANDNAME + ":no:" + slashCommand.Id, ButtonStyle.Danger)
            .Build()
            );
        InteractionCache[slashCommand.Id] = (slashCommand, confirmationMessage, team.TeamId);
        await slashCommand.DeleteOriginalResponseAsync();
        new Task(async () => {
            await Task.Delay(TimeSpan.FromMinutes(10));
            if (InteractionCache.TryGetValue(slashCommand.Id, out _))
            {
                InteractionCache.Remove(slashCommand.Id);
                await confirmationMessage.ModifyAsync(mp => { mp.Components = null; mp.Content = confirmationMessage.Content + "\n[Timed out]"; });
            }
        }).Start();
        await dBContext.DisposeAsync();
    }

    /// <summary>
    /// Id, (Slash Command, Confirmation Message, ID of Team to Remove)
    /// </summary>
    internal static Dictionary<ulong, (SocketSlashCommand, IUserMessage, int)> InteractionCache = []; 

    internal async Task ButtonClicked(SocketMessageComponent messageComponent)
    {
        if (!messageComponent.Data.CustomId.Contains(COMMANDNAME)) return;
        System.Text.RegularExpressions.Match m = RemoveTeamInteractionIdPattern().Match(messageComponent.Data.CustomId);
        string confirmationKey = m.Groups[1].Value;
        ulong interactionKey = ulong.Parse(m.Groups[2].Value);
        if (!InteractionCache.TryGetValue(interactionKey, out (SocketSlashCommand, IUserMessage, int) interactionInfo))
        {
            await messageComponent.RespondAsync("This interaction has expired!", ephemeral: true);
            return;
        }
        LeagueDBContext dBContext = new();
        SocketSlashCommand slashCommand = interactionInfo.Item1;
        IUserMessage confirmationMessage = interactionInfo.Item2;
        Team team = await dBContext.Teams.FirstAsync((t) => t.TeamId == interactionInfo.Item3);
        if (slashCommand.User.Id != messageComponent.User.Id)
        {
            await messageComponent.RespondAsync("This is not your interaction!", ephemeral: true);
            return;
        }
        if (team.Unlinked == true)
        {
            await messageComponent.RespondAsync("Team already unlinked.", ephemeral: true);
            return;
        }

        if (confirmationKey == "no")
        {
            await messageComponent.RespondAsync("Did not unlink the team.");
            await confirmationMessage.ModifyAsync(mp => { mp.Components = null; mp.Content = confirmationMessage.Content + "\n[Cancelled]"; });
            InteractionCache.Remove(interactionKey);
            return;
        }

        await messageComponent.RespondAsync("Removing team...");

        //Im defo not using and SQL database correctly with this, it's just the best that i can right now with my knowledge, feel free to improve
        //Checks if any match in any published season has a squad from the team, if not it'll just remove the team from the database and any unpublished seasons, otherwise it'll auto-lose matches for that team in the current season and do a regular role unlink.
        bool fullyRemovedTeamFlag = false;
        bool randomizedUnpublishedSeasonMatches = false;
        if (await dBContext.Seasons.Include(s => s.SeasonWeeks).Include(s => s.PlayoffWeeks).AnyAsync(s => s.State != Season.SeasonState.Unpublished && s.SeasonWeeks.Cast<Week>().Concat(s.PlayoffWeeks).Any(w => dBContext.Entry(w).Collection(w => w.Matches).Query().Any(m => dBContext.Entry(m).Reference(m => m.HomeSquad).Query().Include(s => s.Team).Single().Team == team || dBContext.Entry(m).Reference(m => m.AwaySquad).Query().Include(s => s.Team).Single().Team == team))))
        {
            if (await dBContext.Seasons.FirstOrDefaultAsync(season => season.State == Season.SeasonState.Started) is Season season)
            {
                foreach (Week week in dBContext.Entry(season).Collection(s => s.SeasonWeeks).Query().Cast<Week>().Concat(dBContext.Entry(season).Collection(s => s.PlayoffWeeks).Query()))
                {
                    foreach (Match match in dBContext.Entry(week).Collection(w => w.Matches).Query().Where(m => dBContext.Entry(m).Reference(m => m.HomeSquad).Query().Include(s => s.Team).Single().Team == team || dBContext.Entry(m).Reference(m => m.AwaySquad).Query().Include(s => s.Team).Single().Team == team))
                    {
                        if (match.Winner == Match.MatchState.Undecided) match.Winner = match.HomeSquad.Team == team ? Match.MatchState.Away : Match.MatchState.Home;
                    }
                }
            }
            if (await dBContext.Seasons.Include(s => s.Divisions).ThenInclude(d => d.Squads).ThenInclude(s => s.Team).FirstOrDefaultAsync(season => season.State == Season.SeasonState.Unpublished) is Season unpublishedSeason)
            {
                randomizedUnpublishedSeasonMatches = true;
                unpublishedSeason.Squads.RemoveAll(s => s.Team == team);
                await unpublishedSeason.RandomizeGuaranteedMatches();
            }
            team.TeamRoleID = 0;
            team.Unlinked = true;
        }
        else
        {
            fullyRemovedTeamFlag = true;
            if (await dBContext.Seasons.Include(s => s.Divisions).ThenInclude(d => d.Squads).ThenInclude(s => s.Team).FirstOrDefaultAsync(season => season.State == Season.SeasonState.Unpublished) is Season season)
            {
                randomizedUnpublishedSeasonMatches = true;
                season.Squads.RemoveAll(s => s.Team == team);
                await season.RandomizeGuaranteedMatches();
            }
            dBContext.Remove(team);
        }
        
        await dBContext.SaveChangesAsync();

        InteractionCache.Remove(interactionKey);
        await messageComponent.ModifyOriginalResponseAsync(mp => { mp.Content = fullyRemovedTeamFlag ? $"Team {team.TeamName} was successfully deleted from the league." + (randomizedUnpublishedSeasonMatches ? " Randomization of the unpublished season was required and executed." : "") : $"Team {team.TeamName} was successfully unlinked from it's role."; });
        await confirmationMessage.ModifyAsync(mp => { mp.Components = null; mp.Content = confirmationMessage.Content + "\n[Confirmed]"; });

        await dBContext.DisposeAsync();
    }
}
