using System;
using System.Text;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using UnityEngine;
using Facepunch;
using Rust;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Plugins;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.Linq;
using Oxide.Core.Configuration;

namespace Oxide.Plugins
{
    [Info( "Civility", "ichaleynbin", "1.2.0" )]
    [Description( "Back-end plugin for use in other plugins, for tracking useful data shared between plugins")]

    class Civility : RustPlugin
    {

	public static List<Civility> Laws = new List<Civility> ();
	public static List<Civility> Punishments = new List<Civility> ();
	bool DataChanged = false;
	public static Dictionary< ulong, PlayerData> Players;
	public static Oxide.Core.Libraries.Time realTime = new Oxide.Core.Libraries.Time();
	Timer updateTimer;
	Dictionary< string, ulong > nameToID = new Dictionary<string, ulong>();
	public Dictionary< string, string> Messages = new Dictionary< string, string>();
	private ConfigData configData;

	public static Storage stores;
	Storage murders;
	public float mapSize = TerrainMeta.Size.x/2;
	public float gridSize = TerrainMeta.Size.x/ 52;

	class ConfigData
	{
	    public VersionNumber Version { get; set; }
	    public SettingsData Settings { get; set; }
	}
	
	class SettingsData
	{
	    public bool AnnounceWhenLoaded {get; set; }
	    public bool AdminsAreSheriffs { get; set; } 
	    public bool ModsAreSheriffs { get; set; }
	    public float TrackingRefresh { get; set; }
	}

	public class Punishment
	{
	    public string Type { get; set; } 
	    public float Amount { get; set; }
	    public bool Active { get; set; } 
	}
	
	public class PlayerData
	{
	    public string name { get; set; }
	    public bool canBeKilled { get; set; }
	    public List<ulong> victims { get; set; }
	    public int deathsLeft { get; set; }
	    public bool isAlive { get; set; }	    
	    public bool isAwake { get; set; }
	    public uint lastTimeSeen { get; set; }
	    public string lastKnownLocation { get; set; }
	    public bool isSheriff { get; set; }
	    public Dictionary<string,Dictionary<ulong,int> > crimes { get; set; }
	    
	    [JsonIgnore]
	    public BasePlayer playa { get; set; }

	    public PlayerData (BasePlayer player)
	    {
		name = string.Copy(player.displayName);
		victims = new List<ulong>();
		deathsLeft = 0;
		canBeKilled = false;
		playa = player;
		lastKnownLocation = player.transform.position.ToString();  
	    }
	    public PlayerData () {}
	}
	
	public virtual string CivilityType() {return null;}

	public virtual string CivilityName() {return null;} 

	public virtual void PunishPlayer(PlayerData player, string crime) {}
	
	public virtual void Process() {}
	    
	public virtual void Forgive(PlayerData criminal, ulong victim_id, string crime) {}
	
	public virtual bool PunishmentComplete(PlayerData criminal) {return false;}

	public class Storage
	{
	    public Dictionary <ulong, PlayerData> Players= new Dictionary < ulong, PlayerData> ();
	    public Storage() {}
	}

	public void ChatToPlayer( BasePlayer player, string key, params object[] args )
	{
	    PrintToChat( player, string.Format( Messages[key], args ) );
	}
	
	public void ChatToAll( string key, params object[] args )
	{
	    foreach( BasePlayer player in BasePlayer.activePlayerList )
	    {
		ChatToPlayer( player, key, args );
	    }
	}
	
	public 	List<PlayerData> FindPlayersByFragment(string fragment)
	{
	    List<PlayerData> foundPlayers = new List<PlayerData>();
	    foreach (KeyValuePair<ulong,PlayerData> pair in Players)
	    {
		if (pair.Value.name.ToLower().Contains(fragment.ToLower()))
		    foundPlayers.Add(pair.Value);
	    }
	    return foundPlayers;
	}
	
	public List<PlayerData> PlayersFromArgs(string[] args)
	{
	    string checkNames = String.Join(" ",args).ToLower();
	    Char splitter = ",".ToCharArray()[0];
	    string[] checkNameList = checkNames.Split(splitter);
	    List<PlayerData> foundPlayers= new List<PlayerData>();
	    foreach (string checkName in checkNameList)
	    {
		foreach (PlayerData player in FindPlayersByFragment(checkName))
		{
		    foundPlayers.Add(player);
		}
	    }
	    return foundPlayers;
	}

	protected override void LoadDefaultConfig()
	{
	    configData = new ConfigData
	    {
		Version = Version,
		Settings = new SettingsData
		{
		    AnnounceWhenLoaded = false,
		    AdminsAreSheriffs = true,
		    ModsAreSheriffs = false,
		    TrackingRefresh = 30f
		}
	    };
	    Config.WriteObject(configData, true);
	}
	
	public void SaveData()
	{
	    Interface.GetMod().DataFileSystem.WriteObject( "Civility-Players",stores);
	    Config.WriteObject(configData,true);
	}
	
     	void OnServerSave()
	{
	    SaveData();
	}

	void LoadData()
	{
	    try
	    {
		stores = Interface.Oxide.DataFileSystem.ReadObject<Storage>("Civility-Players");
	    }
	    catch
	    {
		Puts ("Player data garbage-clearing database and starting fresh");
		stores = new Storage();
	    }
	    if (stores == null)
	    {
		Puts("Storage object found, but null. Instantiate new");
		stores = new Storage();
	    }
	    Players = stores.Players;
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
	}
	
	public void GivePlayer(PlayerData myPlayer, BasePlayer player)
	{
	    if ((myPlayer.playa == null) || (myPlayer.playa != player))
		myPlayer.playa = player;
	}
	
	public void ResetTickData(PlayerData player)
	{
	    player.isAlive = false;
	    player.isAwake = false;
	    player.lastKnownLocation = null;
	}
	
	public void UpdateData(PlayerData myPlayer, BasePlayer player, bool alive, bool awake)
	{
	    myPlayer.lastTimeSeen = realTime.GetUnixTimestamp();
	    myPlayer.isAlive = alive;
	    myPlayer.isAwake = awake;
	    if (myPlayer.playa == null)
		myPlayer.playa = player;
	    myPlayer.name = player.displayName;
	    myPlayer.lastKnownLocation = player.transform.position.ToString();
	}
	
	public void AddPlayer(ulong player_id,PlayerData player)
	{
	    if (!Players.ContainsKey(player_id))
		Players[player_id] = player;
	}

	public Vector3 CurrentVector3(string loc)
	{
	    char[] locTrim = "()".ToCharArray();
	    char[] splitter = " ".ToCharArray();
	    int count = 0;
	    float x,y,z;
	    x = y = z = 0f;
	    foreach (string coord in loc.Trim(locTrim).Replace(",","").Split(splitter))
	    {
		if (count == 0)
		    float.TryParse(coord, out x);
		else if (count == 1)
		    float.TryParse(coord, out y);
		else
		    float.TryParse(coord, out z);
		count++;
	    }
	    return new Vector3(x,y,z);	    
	}
	
	public void AddCrime(PlayerData criminal,string crime,ulong victim_id)
	{
	    if (criminal.crimes == null)
            {
		criminal.crimes = new Dictionary <string,Dictionary< ulong, int> >();
//                criminal.crimes["Total"] = 0;
            }
	    if (criminal.crimes.ContainsKey(crime))
	    {
		if (criminal.crimes[crime].ContainsKey(victim_id))
		    criminal.crimes[crime][victim_id] += 1;
		else
		    criminal.crimes[crime][victim_id] = 1;
		criminal.crimes[crime][0] += 1;
	    }
	    else
	    {
		criminal.crimes[crime] = new Dictionary<ulong,int>();
		criminal.crimes[crime][victim_id] = 1;
		criminal.crimes[crime][0] = 1;
	    }
//            criminal.crimes["Total"] += 1;
	    if (!criminal.victims.Contains(victim_id))
		criminal.victims.Add(victim_id);
	    foreach(Civility punishment in Punishments)
	    {
		punishment.PunishPlayer(criminal,crime);
	    }
	    SaveData();
	}

	[ChatCommand("forgive")]
	void ChatCommandForgive(BasePlayer player, string command, string[] args)
	{
	    if (args.Count() != 2)
		ChatToPlayer(player,"syntax");
	    else
	    {
		List<PlayerData> foundPlayers = FindPlayersByFragment(args[0]);
		if (foundPlayers.Count() > 1)
		    ChatToPlayer(player,"toomany");
		else if (foundPlayers.Count() == 0)
		    ChatToPlayer(player,"noneFound");
		else
		{
		    PlayerData criminal = foundPlayers[0];
		    string crime = args[1];
		    if (criminal.crimes != null && criminal.crimes.ContainsKey(crime)&& criminal.crimes[crime].ContainsKey(player.userID))
		    foreach( Civility punishment in Punishments)
		    {
			punishment.Forgive(criminal,player.userID,crime);
		    }
		}
	    }
	}

	public bool Forgivable(PlayerData criminal, ulong victim_id, string crime)
	{
	    return ( ( criminal.crimes != null ) 
		     && criminal.crimes.ContainsKey(crime)
		     && criminal.crimes[crime].ContainsKey(victim_id) );
	}

	void CompileVictimsList(PlayerData criminal)
	{
	    criminal.victims.Clear();
	    foreach( KeyValuePair<string,Dictionary<ulong,int> > pair in criminal.crimes)
	    {
		foreach (ulong victim_id in pair.Value.Keys)
		{
		    if (!criminal.victims.Contains(victim_id))
			criminal.victims.Add(victim_id);
		}
	    }
	}
	
	public virtual bool Pardon(PlayerData criminal, string crime)
	{
	    if (criminal.crimes.ContainsKey(crime))
	    {
		criminal.crimes.Remove(crime);
		CompileVictimsList(criminal);	    
		foreach ( Civility punishment in Punishments)
		{
		    punishment.Pardon(criminal,crime);
		}
		if (criminal.crimes.Keys.Count == 0)
		    criminal.crimes = null;
		return true;
	    }
	    return false;
	}

        [ChatCommand("autopardon")]
        void ChatAutoPardon(BasePlayer player, string command, string[] args)
        {
            if (!Players[player.userID].isSheriff)
            { 
		ChatToPlayer(player,"fuckoff");
		return;

            }
            if (args.Count() != 1)
            {
		ChatToPlayer(player,"syntax");
		return;
	    }
            try
            {
                int LTE = Convert.ToInt32(args[0]);
                foreach ( KeyValuePair<ulong,PlayerData> pair in Players)
                {
                    int totalCrimes = 0;
                    foreach(KeyValuePair<string,Dictionary<ulong,int>> crime in pair.Value.crimes)
                    {
                        
                    }
                }
            }
            catch {}
        }


	[ChatCommand("makesheriff")]
	void ChatMakeSheriff(BasePlayer player, string command, string[] args)
	{
	    if (player.net.connection.authLevel < 2)
	    {
		ChatToPlayer(player,"fuckoff");
		return;
	    }
	    if (args.Count() != 1)
	    {
		ChatToPlayer(player,"syntax");
		return;
	    }
	    foreach (KeyValuePair<string,ulong> pair in nameToID)
	    {
		if (pair.Key.Contains(args[0]))
		{
		    ChatToPlayer(player,"sheriffSuccess",pair.Key);
		    Players[pair.Value].isSheriff = true;
		}
	    }
	}

	bool AuthSheriff(BasePlayer player)
	{
	    if ((player.net.connection.authLevel >= 1) && configData.Settings.ModsAreSheriffs)
		return true;
	    else if ((player.net.connection.authLevel == 2) && configData.Settings.AdminsAreSheriffs)
		return true;
	    else
		return false;
	}

	[ChatCommand("pardon")]
	void ChatPardon(BasePlayer player, string command, string[] args)
	{
	    if (args.Count() != 2 && args.Count() != 1)
	    {
		ChatToPlayer(player,"syntax");
		return;
	    }
	    PlayerData pardoningPlayer = Players[player.userID];
	    PlayerData pardonedPlayer=null;
	    if (pardoningPlayer.isSheriff || AuthSheriff(player))
	    {
		if (!pardoningPlayer.isSheriff)
		    pardoningPlayer.isSheriff = true;
		bool foundOne = false;
		foreach (KeyValuePair<string,ulong> pair in nameToID)
		{
		    if (pair.Key.Contains(args[0]))
		    {
			if (foundOne)
			{
			    ChatToPlayer(player,"toomany");
			    return;
			}
			else
			{
			    pardonedPlayer = Players[pair.Value];
			    foundOne = true;
			}
		    }
		}
	    }
	    else 
	    {
		ChatToPlayer(player,"fuckoff");
		return;
	    }

	    if (pardonedPlayer == null)
	    {
		ChatToPlayer(player,"noneFound");
		return;
	    }
	    if (args.Count() == 2)
	    {
		if (Pardon(pardonedPlayer,args[1]))
		    ChatToPlayer(player,"pardonSuccess",pardonedPlayer.name,args[1]);
		else
		    ChatToPlayer(player,"pardonUnsuccess",pardonedPlayer.name,args[1]);
	    }
	    else
	    {
		if (pardonedPlayer.crimes != null)
		{
		    List<string> crimeKeys = new List<string>(pardonedPlayer.crimes.Keys);
		    foreach ( string crime in crimeKeys)
		    {
			if (Pardon(pardonedPlayer,crime))
			    ChatToPlayer(player,"pardonSuccess",pardonedPlayer.name,crime);
			else
			    ChatToPlayer(player,"pardonUnsuccess", pardonedPlayer.name,crime);
		    }
		}
	    }
	    if (pardonedPlayer.crimes != null && pardonedPlayer.crimes.Keys.Count == 0)
		pardonedPlayer.crimes = null;
	}


	/*	
	[ChatCommand("recover")]
	void Recover()
	{
	    stores = Interface.Oxide.DataFileSystem.ReadObject<Storage>("Civility-Players");
	    Players = stores.Players;
	    SaveData();
	}
	*/

	/*	
	void ProcessCrimes()
	{
	    Puts("Processing New Crime Data");
	    foreach( KeyValuePair<ulong,PlayerData> pair in Players)
	    {
		PlayerData criminal = pair.Value;
		try
		{
		    if (criminal.crimes != null){}
		}
		catch
		{
		    criminal.crimes = null;
		}		    
		
		Dictionary<string,int> crimesKnown;
		if (KnownCrimes.ContainsKey(pair.Key))
		    crimesKnown = KnownCrimes[pair.Key];
		else
		    crimesKnown = KnownCrimes[pair.Key] = new Dictionary<string,int>();
		foreach( KeyValuePair<string,Dictionary<ulong,int>> inPair in criminal.crimes)
		{
		    if (!crimesKnown.ContainsKey(inPair.Key))
		    {
			crimesKnown[inPair.Key] = 0;
		    }
		    if (crimesKnown[inPair.Key] != inPair.Value.Keys.Count)
		    {
		        crimesKnown[inPair.Key] = inPair.Value.Keys.Count;
			foreach( Civility punishment in Punishments)
			{
			    punishment.PunishPlayer(criminal,inPair.Key);
			}
		    }
		}
	    }
	}
	*/

	void ProcessWakers()
	{
	    foreach (BasePlayer waker in BasePlayer.activePlayerList)
	    {
		PlayerData wokeData;
		try
		{
		    wokeData = Players[waker.userID];
		    GivePlayer(wokeData,waker);
		}
		catch 
		{
		    wokeData = new PlayerData(waker);
		    Players[waker.userID] = wokeData;
		}	
		UpdateData(wokeData,waker,true,true);
	    }
	}

	void ProcessSleepers()
	{
	    foreach (BasePlayer sleeper in BasePlayer.sleepingPlayerList)
	    {
		PlayerData sleeperData;
		try
		{
		    sleeperData = Players[sleeper.userID];
		    GivePlayer(sleeperData,sleeper);
		}
		catch 
		{
		    sleeperData = new PlayerData(sleeper);
		    Players[sleeper.userID] = sleeperData;
		}	
		UpdateData(sleeperData,sleeper,true,false);
	    }
	}

	public void UpdateLocations()
	{
	    Puts("Updating Current Player Locations!");
	    nameToID.Clear();
	    foreach (KeyValuePair<ulong,PlayerData> pair in Players)
	    {
		ResetTickData(pair.Value);
		nameToID[pair.Value.name] = pair.Key;
	    }
	    ProcessWakers();
	    ProcessSleepers();
	    foreach (Civility plugin in Laws)
	    {
		plugin.Process();
	    }
	    foreach (Civility plugin in Punishments)
	    {
		plugin.Process();
	    }
	}

	
	/*
	[ChatCommand("readbackup")]
	void LoadOld()
	{
	    stores = Interface.GetMod().DataFileSystem.ReadObject<Storage>("MurderPlayers");
	    try
	    {
		Players = stores.Players;
	    }
	    catch
	    {
	    }
	}
	*/
	
	/*
	[ChatCommand("writebackup")]
	void WriteBackup()
	{
	    Interface.GetMod().DataFileSystem.WriteObject( "Civility-Backup",stores);
	}
	*/
	    	
	void CompileMessages()
	{
	    Messages.Clear();
	    Messages[ "Loaded" ] = "User Data Tracking plugin version {0} by ichaleynbin has loaded {1} law(s) and {2} punishment(s).";
	    Messages[ "syntax" ] = "You fucked up real good somehow.";
	    Messages[ "fuckoff" ] = "Lol you aren't allowed to do that. Fuck right off meow.";
	    Messages[ "toomany" ] = "Found too many players with that name. Try again.";
	    Messages[ "noneFound" ] = "No players found by that name.";
	    Messages[ "pardonSuccess" ] = "Successfully pardoned {0} for crime {1}";
	    Messages[ "pardonUnsuccess" ] = "Found player {0} but not crime {1}, please try again.";
	    Messages[ "sheriffSuccess" ] = "Made {0} into a sheriff!";
	}

	public virtual void RegisterLaw(string law) {}

	public void AddPlugin(Plugin plugin)
	{
	    Civility civilPlugin = (Civility)plugin;
	    if (civilPlugin.CivilityType() == "Law")
	    {
		if (!Laws.Contains(civilPlugin))
		{
		    Laws.Add(civilPlugin);
		    foreach (Civility punishment in Punishments)
		    {
			punishment.RegisterLaw(civilPlugin.CivilityName());
		    }
		}
	    }
	    else if (civilPlugin.CivilityType() == "Punishment")
	    {
		if(!Punishments.Contains(civilPlugin))
		{
		   Punishments.Add(civilPlugin);
		   foreach( Civility law in Laws)
		   {
		       civilPlugin.RegisterLaw(law.CivilityName());
		   }
		}
	    }
	}

	public void CivilityLoad()
	{
	    stores = Civility.stores;
	    Players = stores.Players;	    
	    Punishments = Civility.Punishments;
	    Laws = Civility.Laws;
	    AddPlugin(this);
	}

	void Loaded()
	{
	    LoadData();
	    CompileMessages();
	    UpdateLocations();
	    if( configData.Settings.AnnounceWhenLoaded)
		timer.Once( 5f, () => { ChatToAll( "Loaded", Version.ToString() , Laws.Count, Punishments.Count); } );
	    timer.Repeat(configData.Settings.TrackingRefresh,0,UpdateLocations);
	}
    }
}
