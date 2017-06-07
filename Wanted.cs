using System;
using System.Collections.Generic;
using UnityEngine;
using Facepunch;
using Rust;
using Oxide.Core;
using Oxide.Core.Libraries;
using System.Linq;
using Oxide.Core.Plugins;

// Requires: Civility

namespace Oxide.Plugins
{
    [Info( "Civility Punishment: Bounty Hunting", "ichaleynbin", "1.0.1")]
    [Description( "Allow wanted players to be hunted by law abiding citizens.")]

    class Wanted : Civility
    {
	[PluginReference] Plugin ServerRewards;
	[PluginReference] Civility Civility;

	private ConfigData configData;
        private System.Random random = new System.Random();

	private Dictionary<ulong,TrackingInformation> TrackingData = 
	    new Dictionary<ulong,TrackingInformation>();


	class TrackingInformation
	{
	    public List<ulong> Trackers {get; set; }
	    public TrackingLocation Location { get; set; }	    
	}

	class TrackingLocation
	{
	    public int minX {get; set;}
	    public int maxX {get; set;}
	    public int minY {get; set;}
	    public int maxY {get; set;}
//	    public int minZ {get; set;}
//	    public int maxZ {get; set;}
	    public string location {get; set;}
	}
	
	class ConfigData
	{
	    public VersionNumber Version { get; set; }
	    public SettingsData Settings { get; set; }
	    public Dictionary<string,Punishment> Punishments { get; set; }
	}

	class SettingsData
	{
	    public string Prefix { get; set; }
	    public Dictionary<int,TrackingLevel> TrackingSettings {get; set; }
	    public int ElevationBlocksize { get; set;} 
	}

	class TrackingLevel
	{
	    public int Precision {get; set;}
	    public Dictionary<string, int> CrimesRequired {get; set;}
	}
	
	public override string CivilityType()
	{
	    return "Punishment";
	}

	public override string CivilityName()
	{
	    return "Murder";
	}

	public override void RegisterLaw(string law)
	{
	    if (!configData.Punishments.ContainsKey(law))
	    {
		Punishment registering = configData.Punishments[law] = new Punishment();
		Punishment defaultPunishment = configData.Punishments["Default"];
		registering.Type = defaultPunishment.Type;
		registering.Active = defaultPunishment.Active;
		registering.Amount = defaultPunishment.Amount;
	    }
//	    if (!	    
	    foreach(KeyValuePair<int,TrackingLevel> pair in configData.Settings.TrackingSettings)
	    {
		int crimes = pair.Key;
		Dictionary<string,int> crimesReq = pair.Value.CrimesRequired;
		crimesReq[law] = crimes;
	    }
	    

	}

	public override void PunishPlayer(PlayerData player, string crime) 
	{
	    Punishment punishment = configData.Punishments[crime];
	    if (punishment.Active)
	    {
		player.canBeKilled = true;
		if (punishment.Type == "Flat")
		    player.deathsLeft += (int)punishment.Amount;
		else
		{
		    player.deathsLeft += (int)(player.crimes[crime][0] * punishment.Amount);
		}
	    }
	}

	public override bool PunishmentComplete(PlayerData criminal)
	{
	    if (!criminal.canBeKilled || criminal.deathsLeft <= 0)
	    {
		criminal.canBeKilled = false;
		criminal.deathsLeft = 0;
		foreach(KeyValuePair<string,Punishment> pair in configData.Punishments)
		{
		    if (pair.Value.Active)
		    {
			Pardon(criminal,pair.Key);
		    }
		}
		return true;
	    }
	    else 
		return false;
	}
	
	public void PostDeathProcessing(PlayerData criminal)
	{
	    criminal.deathsLeft -= 1;
	    if (PunishmentComplete(criminal))
	    {
	    }
//		Pardon(criminal,;
	}

	
	public override void Forgive(PlayerData criminal, ulong victim_id, string crime)
	{
	    Punishment punishment = configData.Punishments[crime];
	    if (punishment.Active)
	    {
		int counts = criminal.crimes[crime][victim_id];
		int deathsLeft;
		if (punishment.Type =="Flat")
		    criminal.deathsLeft -= (int)(counts * punishment.Amount);
		else
		{
		    int totalCounts = criminal.crimes[crime][0];
		    for(int i = 0; i < counts; i++)
		    {
			criminal.deathsLeft -= (int)((totalCounts - i) * punishment.Amount);
		    }
		}
                criminal.crimes[crime][0] -= counts;
//                criminal.crimes["Total"] -= counts;
                criminal.crimes[crime].Remove(victim_id);
		PostDeathProcessing(criminal);
	    }
	}

	string UnWantedString(string murderer)
	{
	    string prefix = configData.Settings.Prefix;
	    if (murderer.Contains(prefix))
		return murderer.Replace(prefix, "").Replace("  "," ");
	    else
		return murderer;
	}

	public override bool Pardon(PlayerData criminal, string crime)
	{
	    if (configData.Punishments[crime].Active)
	    {
		bool wasKillable = criminal.canBeKilled;
		bool stillKillable = false;
		criminal.crimes.Remove(crime);
		try
		{
		    foreach (string remainingCrime in criminal.crimes.Keys)
		    {
			if (configData.Punishments[remainingCrime].Active
			    && (criminal.crimes[remainingCrime][0] > 0))
			    stillKillable = true;
		    }
		}
		catch {}
		criminal.canBeKilled = stillKillable;
		if (!stillKillable)
		    criminal.deathsLeft = 0;
		return (wasKillable == stillKillable);
	    }
	    else
		return false;
	}

	int TrackingOffenseLevel(PlayerData player)
	{
	    for (int i = 0; i < 6; i++)
	    {
		TrackingLevel tracking = configData.Settings.TrackingSettings[i];
		foreach (KeyValuePair<string,int> pair in tracking.CrimesRequired)
		{
//                    if (pair.Key == "Total" && player.crimes["Total"] >= pair.Value)
  //                      return i;
		    if (player.crimes.ContainsKey(pair.Key) && player.crimes[pair.Key][0] >= pair.Value)
			return i;
		}
	    }
	    return 5;
	}

	void Track(PlayerData player) 
	{
	    if (player.canBeKilled)
	    {
		if (player.playa != null)
		{
		    BasePlayer gamePlayer = player.playa; 
		    TrackingInformation tracking;
		    if (!TrackingData.ContainsKey(player.playa.userID))
		    {
			tracking = TrackingData[player.playa.userID] = new TrackingInformation();
			tracking.Trackers = new List<ulong>();
			tracking.Location = new TrackingLocation();
		    }
		    tracking = TrackingData[player.playa.userID];
		    FillTrackingLocation(tracking,player.playa,TrackingOffenseLevel(player));
		}
	    }
	}
	
	Vector2 GetGridLocation(string location)
	{
	    Vector3 loc = CurrentVector3(location);
	    Vector2 mapLocation  = new Vector2();
	    for (int i = 0; i < 3; i++)
	    {
		if (i == 0)
		{
		    mapLocation.x = ( (mapSize + loc.x) % gridSize ) +1;
		}
		else if (i == 2)
		{
		    mapLocation.y = ( (mapSize - loc.z) % gridSize ) +1;
		}
	    }
	    return mapLocation;
	}
	
        List<int> DerpTracking(List<int> current, int precision)
        {
            if (precision <= 1)
                return current;
            else
            {
                precision--;
                if (random.Next(0,1) == 1)
                {
                    current[0]--;
                }
                else 
                {
                    current[2]++;
                }
                if (random.Next(0,1) == 1)
                {
                    current[1]--;
                }
                else 
                {
                    current[3]++;
                }
                return DerpTracking(current, precision);
            }
        }
        

	string FillTrackingLocation(TrackingInformation tracking,BasePlayer player, int precision)
	{
//            if (precision == 0)
                return Players[player.userID].lastKnownLocation;
            /*	    Vector2 gridz = GetGridLocation(Players[player.userID].lastKnownLocation);
	    List<int> current = new List<int>();
            for (int i=0;i++;i<=1)
            {
                current.Add(Convert.ToInt32(gridz.x));
                current.Add(Convert.ToInt32(gridz.y));
            }
            List<int> derped = DerpTracking(current,precision);
            //TURN DERPED INTO STRING HERE*/
	}
	
	void WantedName(BasePlayer player)
	{
	    string prefix = configData.Settings.Prefix;
	    PlayerData playerData = Players[player.userID];

	    if (playerData.canBeKilled)
	    {
		if (!player.displayName.Contains(prefix))
		    player.displayName =  $"{prefix} {player.displayName}";
		if (!TrackingData.ContainsKey(player.userID))
		{
		    TrackingData[player.userID] = new TrackingInformation ();
		    TrackingData[player.userID].Trackers = new List<ulong>();
		}
	    }
	    else if (!playerData.canBeKilled)
	    {
		if (player.displayName.Contains(prefix))
		    player.displayName = player.displayName.Replace(prefix, "").Trim();
		if (TrackingData.ContainsKey(player.userID))
		    TrackingData.Remove(player.userID);
	    }
	}

	public override void Process()
	{
	    foreach (KeyValuePair<ulong,PlayerData> player in  Players)
	    {
		try
		{
		    Track(player.Value);
		    WantedName(player.Value.playa);
		    
		}
		catch{}
	    }
	    
	    
	}

	protected override void LoadDefaultConfig()
	{
	    configData = new ConfigData
	    {
		Version = Version,
		Settings = new SettingsData 
		{
		    Prefix = "[Wanted]",
		},
		Punishments = new Dictionary<string, Punishment>()
	    };

	    Punishment flat = new Punishment();
	    flat.Type = "Flat";
	    flat.Amount = 2f;
	    flat.Active = true;
	    configData.Punishments["Default"] = flat;
	    configData.Settings.ElevationBlocksize = 20;
	    Dictionary<int, TrackingLevel> tracking = configData.Settings.TrackingSettings = new Dictionary<int,TrackingLevel>();
	    for (int i = 0; i <6; i++)
	    {
		TrackingLevel newTrackingLevel = new TrackingLevel();
		newTrackingLevel.Precision = i;
		newTrackingLevel.CrimesRequired = new Dictionary<string, int>();
                newTrackingLevel.CrimesRequired["Total"] = 6-i;
		tracking[i] = newTrackingLevel;
	    }
	    Config.WriteObject(configData, true);
	}

	void LoadData()
	{
	    try
	    {
		configData = Config.ReadObject<ConfigData>();
		if (configData.Version != Version)
		{
		    Puts("Version changed, generating fresh config");
		    LoadDefaultConfig();
		}
	    }
	    catch
	    {
		Puts("Garbage Config, generating new");
		LoadDefaultConfig();
	    }
	    foreach( KeyValuePair<string,Punishment> pair in configData.Punishments)
	    {
		pair.Value.Amount = Convert.ToSingle(Math.Truncate(pair.Value.Amount));
	    }

	}
	
	void PlayerDeath(BasePlayer player, HitInfo info)
	{
	    if (player != null)		
	    {
		PlayerData playerData = Players[player.userID];
		if ( playerData.canBeKilled && (info != null) && (info.Initiator != null) )
		{
		    BasePlayer initiator = info.Initiator.ToPlayer();

		    if (initiator != null)
		    {
			if (!player.IsWounded() && (player.userID != initiator.userID))
			{
			    PlayerData criminal = Players[player.userID];
			    int crimes = 0;
			    foreach (string crime in criminal.crimes.Keys)
				crimes += criminal.crimes[crime][0];
			    int reward = crimes*100;
//			    ServerRewards?.Call("AddPoints", new object[] { initiator.userID,  reward});
			    PostDeathProcessing(playerData);
			    ChatToPlayer(initiator,"KilledWanted",UnWantedString(playerData.name),reward);
			}
		    }
		    
		}
	    }
	}
	
	void OnPlayerDie(BasePlayer player, HitInfo info)
	{
	    timer.Once(1f, () => PlayerDeath(player,info));
	}

	void ShowWantedPlayerDetailsTo(BasePlayer player, PlayerData criminal)
	{
	    int totalCrimes = 0;
	    foreach (string crime in criminal.crimes.Keys)
		totalCrimes += criminal.crimes[crime][0];
	    ChatToPlayer(player,"MultiCrimeDisplay",UnWantedString(criminal.name),StatusReport(criminal),criminal.deathsLeft,100*totalCrimes,totalCrimes);
	    foreach( KeyValuePair<string,Dictionary<ulong,int>> pair in criminal.crimes)
	    {
		string crime = pair.Key;
		Dictionary<ulong,int> victims = pair.Value;
		int totalCounts = victims[0];
		ChatToPlayer(player,"CrimeCount",totalCounts,crime);
		foreach(KeyValuePair<ulong,int> TP in victims)
		{
		    if (TP.Key != 0)
			ChatToPlayer(player,"CrimeVictims",Players[TP.Key].name,TP.Value);
		}
		
	    }
	}

	string StatusReport(PlayerData criminal)
	{
	    string status;
	    if (criminal.isAlive)
		if (criminal.isAwake)
		    return "alive, awake, and";
		else
		    return "alive, asleep, and";
	    else
		return "already dead but still";
		
	}

	void ShowWantedPlayerOverviewTo(BasePlayer player,PlayerData criminal)
	{
	    int crimes = 0;
	    foreach ( string crime in criminal.crimes.Keys)
	    {
		crimes += criminal.crimes[crime][0];
	    }
	    string status = StatusReport(criminal);
	    string crimesCount;
	    if (crimes == 1)
		crimesCount = "1 crime";
	    else
		crimesCount = crimes.ToString() + " crimes";
	    string victimCount;
	    if (criminal.victims.Count == 1)
		victimCount = "1 player";
	    else
		victimCount = criminal.victims.Count.ToString() + " players";
	    ChatToPlayer(player,"WantedPlayerOverview",UnWantedString(criminal.name),status,crimesCount,victimCount);
	}
	


	void ShowWantedListTo(BasePlayer player)
	{
	    ChatToPlayer(player, "WantedList");
	    foreach (KeyValuePair<ulong,PlayerData> pair in Players)
	    {
		if (pair.Value.canBeKilled)
		{
		    ShowWantedPlayerOverviewTo(player,pair.Value);
		}
	    }
	}
	List<PlayerData> FindWantedsByFragment(string fragment)
	{
	    List<PlayerData> foundPlayers = new List<PlayerData>();
	    foreach(PlayerData player in FindPlayersByFragment(fragment))
	    {
		if (player.canBeKilled)
		    foundPlayers.Add(player);
		
	    }
	    return foundPlayers;
	}

	List<PlayerData> WantedPlayersFromArgs(string[] args)
	{
	    List<PlayerData> foundList = new List<PlayerData>();
	    foreach( PlayerData player in PlayersFromArgs(args))
	    {
		if (player.canBeKilled)
		    foundList.Add(player);
	    }
	    return foundList;
	}

	[ChatCommand("wanted")]
	void ChatWanted(BasePlayer player, string command , string[] args)
	{
	    if (args.Count() == 0)
	    {
		ShowWantedListTo(player);
	    }
	    else 
	    {
		bool foundOne = false;
		List<PlayerData> playersToCheck = PlayersFromArgs(args);
		foreach (PlayerData wantedPlayer in playersToCheck)
		{
		    if (wantedPlayer.canBeKilled)
			ShowWantedPlayerDetailsTo(player,wantedPlayer);
		    else if (playersToCheck.Count() <=3)
			ChatToPlayer(player,"NotWanted",wantedPlayer.name);
		    foundOne = true;
		}
		if (!foundOne)
		    ChatToPlayer(player,"NotFound");
	    }
	}

	[ChatCommand("track")]
	void ChatCommandTrack(BasePlayer player, string command, string[] args)
	{
	    if (args.Count() == 0)
		ChatToPlayer(player,"TrackingArgs");
	    else if (args.Count() == 1 && args[0] == "all")
	    {
		foreach(KeyValuePair<ulong,TrackingInformation> pair in TrackingData)
		{
		    pair.Value.Trackers.Add(player.userID);
		}
		ChatToPlayer(player,"TrackingAll");

		Puts("tracking all!");
	    }
	    else if (args.Count() == 1 && args[0] == "stop")
	    {
		foreach(KeyValuePair<ulong,TrackingInformation> pair in TrackingData)
		{
		    pair.Value.Trackers.Remove(player.userID);
		}
		ChatToPlayer(player,"StopTrackingAll");
	    }
	    else
	    {
		List<PlayerData> foundPlayers = WantedPlayersFromArgs(args);
		Puts(TrackingData.Keys.Count().ToString());
		foreach(KeyValuePair<ulong,TrackingInformation> pair in TrackingData)
		{
		    Puts(foundPlayers.Count().ToString());
		    PlayerData trackedPlayer = Players[pair.Key];
		    if ( foundPlayers.Contains(trackedPlayer))
		    {
			Puts("WAKA");
			if (pair.Value.Trackers.Contains(player.userID))
			{
			    ChatToPlayer(player,"StopTrackingPlayer",trackedPlayer.name);
			    pair.Value.Trackers.Remove(player.userID);
			}
			else
			{
			    ChatToPlayer(player,"TrackingPlayer",trackedPlayer.name);
			    pair.Value.Trackers.Add(player.userID);

			}
		    }
		}
	    }
	}

        void SendTrackingData()
        {
            foreach (KeyValuePair<ulong,TrackingInformation> pair in TrackingData)
            {
                PlayerData Tracked = Players[pair.Key];
                foreach (ulong playerID in pair.Value.Trackers)
                {
                    BasePlayer player = Players[playerID].playa;
                    PrintToChat(player,String.Format(Messages["TrackingLocationExact"],Tracked.name,Tracked.lastKnownLocation));
                }
//                ChatToPlayer(player,Messages"TrackingLocationExact",Tracked.name,Tracked.lastKnownLocation);

            }

        }

	void SaveData()
	{
	    Config.WriteObject(configData);
	}

	void OnServerSave()
	{
	    SaveData();
	}

	void Init()
	{
	    LoadData();
	    Messages["WantedPlayerOverview"] = "{0} is {1} wanted for {2} against {3}!";
	    Messages["KilledWanted"] = "You just killed wanted player {0}! Congrats on your {1} point reward!";
	    Messages["WantedList"] = "The following players are wanted. Living players may be tracked via /track [name]";
	    Messages["DeathsLeft"] = "You were just killed while being wanted: you have {1} left.";
	    Messages["MultiCrimeDisplay"] = "{0} is {1} can be killed {2} more times for their crime(s). Killing them is worth {3} server reward points. They are wanted for {4} total crime(s). The details of their crime(s) are as follows:";
	    Messages["CrimeCount"] ="--{0} count(s) of {1} against the following player(s):";
	    Messages["CrimeVictims"] = "----{0}: {1} count(s)";
	    Messages["ComingSoon"] = "This functionality is coming soon™";
	    Messages["FullComingSoon"] = "This functionality is partially implemented; full functionality coming soon™";
	    Messages["NotWanted"] = "{0} is not currently wanted for any crimes.";
	    Messages["TrackingArgs"] ="Please provide a name to be tracked! If you wish to track all awake wanted players, type '/track all'.";
	    Messages["TrackingPlayer"] = "You are now tracking {0}.";
	    Messages["StopTrackingPlayer"] = "You are no longer tracking {0}.";
	    Messages["TrackingAll"] = "You are now tracking all players who can currently be tracked.";
	    Messages["NotFound"] = "No players were able to be found with the names or name fragments you provided!";
	    Messages["StopTrackingAll"] = "You are no longer tracking any players.";
            Messages["TrackingLocationExact"] = "Player {0} was last seen at {1}";
            Messages["TrackingLocationSingle"] = "Player {0} was last seen in the {1} grid.";
            Messages["TrackingLocationMultiple"] = "Player {0} was last seen in the square defined by top left corner {1} and bottom right corner {2}.";
		
	}

	void Loaded()	    
	{
            timer.Repeat(60,0,SendTrackingData);
	    CivilityLoad();
	}
    }
}
