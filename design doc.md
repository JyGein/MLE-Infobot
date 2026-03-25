# General commands - Anyone can run them to get information about the league

**my-match**  
Privately tells user who they are playing this week.  
Throws if the user is not on a squad.

**player-match <player: discord user>**
Privately tells the user who that player is playing this week.  
Throws if player is not playing this week.

**squad-matches <team-role: discord role> <squad-number: int>**  
Privately tells the user who that squad is playing this week.  
Throws if squad does not exist.

**team-matches <team-role: discord role>**  
Privately tells the user who that team is playing this week.  
Throws if team does not exist.

**team-season [team-role: discord role]**  
Privately tells the user (their team/inputted team)'s season matchups.  
Throws if (user is not on a team/role is not connected to a team).

**view-season [season-number: int]**  
Shows all of the matches for the season. Defaults to current season.  
Admin: Requires admin perms to display a season not published.  
Throws if season does not exist.

### FUTURE:
Commands that tell the user various game records.
Commands to show previous weeks/seasons.

# Team Captain commands - require TEAM_CAPTAIN_ROLE

-- None for now!

# Admin commands - require ADMIN_ROLE or guild administator privledges.

## Team management:
**add-team <team-role: discord role> <team-name: string> <team-logo: image link> <team-captain: discord user>**  
Adds a team to the league.
Throws if team already exists.

**edit-team-name <team-role: discord role> <team-name: string>**  
Edit's an existing team's name.  
Throws if role is not attached to a team.

**edit-team-logo <team-role: discord role> <team-logo: image link>**  
Edit's an existing team's logo.  
Throws if role is not attached to a team.

**edit-team-captain <team-role: discord role> <team-captain: discord user>**  
Edit's an existing team's captain.  
Throws if role is not attached to a team.

**add-squad <team-role: discord role> <player1: discord user> <player2: discord user> <player3: discord user> [sub1: discord user] [sub2: discord user]**  
Adds a squad to a team with the listed players to the current unpublished season. Everytime a new squad is added it will randomize the matches.  
Throws if team doesn't exist or if there isn't an unpublished season.

**edit-squad <team-role: discord role> <squad-number: int> <player1: discord user> <player2: discord user> <player3: discord user> [sub1: discord user] [sub2: discord user]**  
Changes the players on a team's squad in the current unpublished season.

**subsitute <team-role: discord role> <squad-number: int> <player: discord user> <sub: discord user> [week-number: int]**  
Substitutes the player with the sub for the current season. Defaults to the next week.  
Throws if the sub or player are not in that squad and team.

**remove-squad <team-role: discord role> <squad-number: int>**  
Removes the squad from the unpublished season.  
Throws if squad or team dont exist.

**remove-team <team-role: discord role>**  
Unattaches the team from their role, the team will auto lose all their matches and the role will be freed up.  
Dangerous; public confirmation message.  
Throws if team doesn't exist.

## League management:
**create-season <number-of-weeks: int>**.  
Creates a new blank season that squads can be added to.
Throws if there is currently an unpublished season.
Notes: This just assumes the playoffs will be Top 8 single elim can add more options in the future for config.

**swap-matchup <season-number: int> <week-number: int> <team1: discord role> <squad1: int> <team2: discord role> <squad2: int>**  
Swaps two squad's matchup for that season and week.  
Throws if season/week/team/squad doesn't exist.
Note: This is technically all you need to manually enter the matches from a randomized season, but can add more commands to make manual input easier.

**swap-home-away <season-number: int> <week-number: int> <team: discord role> <squad: int>**  
Swaps who is home and who is away for that squad's match for that season and week.  
Throws if season/week/team/squad doesn't exist.

**publish-season [publish-channel: discord channel]**  
Publishes the current unpublished season to be the current season and it's first week to be the current week. Displays "New season!" message in publish-channel, defaults to where the command is ran.  
Dangerous; public confirmation message.

**publish-week [publish-channel: discord channel]**  
Publishes the next week of the current season. Displays "New week!" message in publish-channel, defaults to channel where message is sent.  
Semi-Dangerous; private confirmation message.

**submit-matches <team: discord role> <squad: int>**  
Gives an interaction prompting you to fill out the number of wins and loses for each member of the squad.  
Throws if squad doesn't exist.


### Formatting:

**command-name <required-argument: arguement type> [optional-argument: arguement type]**  
Command description.  
Throws (why it'll show an error of the command not working)./  
Dangerous; Sends a regular message in the channel where the command is ran asking for confirmation so at least the other admins know you ran a dangerous command.  
Semi-Dangerous; Shows a private message asking for confirmation.  
Admin: when the command might need admin perms based on arguements.  
Notes: extraneous notes about development
