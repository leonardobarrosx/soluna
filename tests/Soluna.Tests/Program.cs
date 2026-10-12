using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LiteNetLib;
using LiteNetLib.Utils;
using Soluna.Shared;
using Soluna.Server;

var source = Path.GetFullPath("games/soluna");
if (!Directory.Exists(source)) throw new InvalidOperationException("Run from the repository root.");
var root = Path.Combine(Path.GetTempPath(), "soluna-tests-" + Guid.NewGuid().ToString("N"));
var gameRoot = Path.Combine(root, "games", "soluna");
foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
{
    var relative = Path.GetRelativePath(source, file);
    if (relative.Split(Path.DirectorySeparatorChar).Any(p => p is "accounts" or "private")) continue;
    var target = Path.Combine(gameRoot, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
}
Environment.SetEnvironmentVariable("SOLUNA_ROOT", root);
var passed = 0; var failed = 0;
void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
void Require(bool test, string message = "Assertion failed") { if (!test) throw new Exception(message); }
var rules = EvolutionRules.Load();
var content = AdventureContent.Load();
Check("new life has no selected form", () => { var j = new Journey(); rules.Apply(j); Require(j.Calling == "unformed" && j.Bloodline == "unformed" && j.Maturity == 0); });
Check("fishing makes a form without combat or a menu", () => { var j = new Journey(); j.Record("fish",8); rules.Apply(j); Require(j.Calling == "tidekeeper" && j.Kills == 0); });
Check("mixed fishing and combat create a hybrid", () => { var j = new Journey(); j.Record("fish",5); j.Record("melee",8); rules.Apply(j); Require(j.Calling == "marshblade"); });
Check("changing habits can change specialization", () => { var j = new Journey(); j.Record("fish",8); rules.Apply(j); j.Record("melee",100); j.Record("hunt",20); rules.Apply(j); Require(j.Calling == "reaper"); });
Check("food alone does not grant a bloodline", () => { var j = new Journey(); j.Record("moonfood",3); rules.Apply(j); Require(j.Bloodline == "unformed"); j.Record("magic",3); rules.Apply(j); Require(j.Bloodline == "moonbound"); });
Check("rebirth resets the life and retains account memories", () => { var legacy = new Legacy { InnateLuck = 2, Memories = ["tide"] }; legacy.Lives++; var j = content.NewJourney(legacy); rules.Apply(j); Require(j.Legacy == legacy && j.Legacy.Lives == 2 && j.Calling == "unformed" && j.Resources.Count == 0 && j.HpBonus == 2); });
Check("luck respects blessings, curses and bounded probabilities", () => { Require(Fortune.Total([new("blessing",6),new("curse",-9),new("innate",2)]) == -1); Require(Fortune.Total([new("huge",999)]) == 40); Require(Fortune.RareChance(-30) < Fortune.RareChance(40)); });
Check("recipes need every ingredient", () => { var r = content.Recipes.First(x=>x.Id=="smelt"); var j = new Journey(); j.Resources["ore"]=3; Require(!content.CanCraft(j,r)); j.Resources["wood"]=2; Require(content.CanCraft(j,r)); });
Check("shipped adventure content is consistent", () => { var items = ItemCatalog.Load(); var problems = content.Problems(id => items.Get(id) != null); Require(problems.Count == 0, string.Join("; ", problems)); });
Check("objective progress reads from data", () => { var j = content.NewJourney(); var first = content.Objectives[0]; Require(content.ObjectiveLine(j).Contains($"0/{first.Goal}")); j.Rewards.Add(first.Id); Require(content.CurrentObjective(j) != first); });
Check("supplies cannot be consumed below zero", () => { var j=content.NewJourney(); for(var i=0;i<4;i++)Require(j.Consume("Tonic")); Require(!j.Consume("Tonic")&&j.Count("Tonic")==0); });
Check("procedural rooms and exits are connected across 80 layouts", () =>
{
    foreach (var seed in Enumerable.Range(1,20)) foreach(var depth in new[]{1,3,40,10000})
    {
        var m=DungeonGenerator.Build(content.Dungeon!,seed,depth); var seen=new HashSet<(int,int)>(); var queue=new Queue<(int,int)>();queue.Enqueue(m.Spawn);
        while(queue.TryDequeue(out var pos))
        {
            if(!m.IsWalkable(pos.Item1,pos.Item2)||!seen.Add(pos))continue;
            foreach(var dir in Enum.GetValues<Direction>()){var d=dir.Delta();queue.Enqueue((pos.Item1+d.dx,pos.Item2+d.dy));}
        }
        Require(m.Landmarks.All(l=>seen.Contains((l.X,l.Y))),$"Unreachable landmark seed {seed} depth {depth}");
        Require(m.Npcs.All(n=>seen.Contains((n.X,n.Y))),"Unreachable encounter");
        Require(m.Npcs.Select(n=>(n.X,n.Y)).Distinct().Count()==m.Npcs.Count);
    }
});
Check("dungeons reproduce a seed and vary between seeds", () =>
{
    var a=DungeonGenerator.Build(content.Dungeon!,77,2).ToBytes();var b=DungeonGenerator.Build(content.Dungeon!,77,2).ToBytes();var c=DungeonGenerator.Build(content.Dungeon!,78,2).ToBytes();
    Require(a.SequenceEqual(b)&&!a.SequenceEqual(c));
});
Check("difficulty increases without mutating shared NPC definitions", () =>
{
    var original=NpcCatalog.Load().Get(10)!;var a=DungeonGenerator.Scale(original,1);var b=DungeonGenerator.Scale(original,20);
    Require(b.Hp>a.Hp&&b.Attack>a.Attack&&original.Hp==24);
});
Check("shipped world landmarks and portals are reachable", () =>
{
    foreach(var id in new[]{1,2})
    {
        var m=MapData.FromBytes(File.ReadAllBytes(Path.Combine(DataPaths.Data,"maps",$"{id}.json")));
        var seen=new HashSet<(int,int)>();var q=new Queue<(int,int)>();q.Enqueue(m.Spawn);
        while(q.TryDequeue(out var p)){if(!m.IsWalkable(p.Item1,p.Item2)||!seen.Add(p))continue;foreach(var d in Enum.GetValues<Direction>().Select(d=>d.Delta()))q.Enqueue((p.Item1+d.dx,p.Item2+d.dy));}
        Require(m.Landmarks.All(l=>seen.Contains((l.X,l.Y))),$"Map {id}: disconnected service");Require(m.Warps.All(w=>seen.Contains((w.X,w.Y))),$"Map {id}: disconnected portal");
        Require(m.Npcs.All(n=>seen.Contains((n.X,n.Y))), $"Map {id}: disconnected encounter");
        foreach(var warp in m.Warps)
        {
            var target=MapData.FromBytes(File.ReadAllBytes(Path.Combine(DataPaths.Data,"maps",$"{warp.Map}.json")));
            Require(target.IsWalkable(warp.ToX,warp.ToY), $"Map {id}: portal arrives inside scenery");
        }
    }
});
Check("username traversal is rejected",()=>Require(AccountStore.CheckUsername("../../other")!=null));
Check("authored art pages cover native frames with valid source rectangles", () =>
{
    var paths = Directory.GetFiles(DataPaths.Assets, "*.layout.json", SearchOption.AllDirectories);
    Require(paths.Length >= 12, "Missing authored texture layouts");
    foreach (var path in paths)
    {
        var image = path[..^".layout.json".Length];
        Require(File.Exists(image), "Missing fallback image");
        var layout = TextureLayout.Read(image)!;
        foreach (var patch in layout.Patches)
        {
            var full = AssetFiles.Resolve(patch.File);
            Require(full != null && File.Exists(full), $"Missing art page: {patch.File}");
            var dimensions = AssetFiles.PngSize(File.ReadAllBytes(full!));
            Require(dimensions.HasValue, "Art page must be PNG");
            foreach (var rect in patch.Rects)
                Require(rect[0] + rect[2] <= dimensions!.Value.w && rect[1] + rect[3] <= dimensions.Value.h, "Frame exceeds art page");
        }
        if (image.Contains(Path.Combine("characters", "echoes")))
            Require(layout.Columns == 3 && layout.Rows == 4 && layout.Patches.SelectMany(p => p.Target).Distinct().Count() == 12,
                "A character must supply every walk pose in all four directions");
    }
    var blank = TextureLayout.Read(Path.Combine(DataPaths.Assets, "characters", "echoes", "blank.png"))!;
    Require(blank.CellWidth == 32 && blank.CellHeight == 32, "The unformed character must retain its generic size");
});

Check("authored scenery survives map serialization and uses valid art crops", () =>
{
    foreach (var id in new[] { 1, 2 })
    {
        var original = MapData.FromBytes(File.ReadAllBytes(Path.Combine(DataPaths.Data, "maps", $"{id}.json")));
        var map = MapData.FromBytes(original.ToBytes());
        Require(map.Props.Count == original.Props.Count && map.Props.Any(p => p.Underfoot), "Scenery was lost in map transfer");
        for (var i = 0; i < map.Props.Count; i++)
        {
            var p = map.Props[i];
            Require(p.Source.SequenceEqual(original.Props[i].Source) && p.Underfoot == original.Props[i].Underfoot);
            Require(p.Width > 0 && p.Height > 0 && p.X >= 0 && p.Y >= 0 && p.X + p.Width <= map.Width && p.Y + p.Height <= map.Height,
                "Scenery extends beyond the world");
            var file = AssetFiles.Resolve(p.Sprite);
            Require(file != null && File.Exists(file), $"Missing scenery: {p.Sprite}");
            var size = AssetFiles.PngSize(File.ReadAllBytes(file!));
            Require(size.HasValue);
            if (p.Source.Length != 0)
                Require(p.Source is [var x, var y, var w, var h] && x >= 0 && y >= 0 && w > 0 && h > 0
                    && x + w <= size!.Value.w && y + h <= size.Value.h, "Scenery crop crosses its art page");
        }
        foreach (var tile in map.Layers.SelectMany(layer => layer).Where(tile => tile != TileRef.Empty))
        {
            Require(TileRef.Tileset(tile) < map.Tilesets.Count);
            var layout = TextureLayout.Read(Path.Combine(DataPaths.Assets, "tilesets", map.Tilesets[TileRef.Tileset(tile)]))!;
            Require(TileRef.Index(tile) < layout.Columns * layout.Rows, "Map references a tile outside the expanded atlas");
        }
    }
    var legacy = MapData.FromBytes("{\"Props\":[{\"Sprite\":\"props/cottage.png\",\"X\":1,\"Y\":2,\"Width\":5,\"Height\":4}]}"u8);
    Require(legacy.Props[0].Source.Length == 0 && !legacy.Props[0].Underfoot, "Older maps changed their prop rendering defaults");
});

// These fixtures only seed local test accounts; every action below goes through real UDP packets.
var accounts=new AccountStore(Path.Combine(DataPaths.Data,"accounts"));
const string password="local-integration-test";
Account Seed(string username,string name,int map=1,int x=22,int y=22,int hp=-1)
{
    var a=accounts.Create(username,password);
    a.Legacy.Memories.Add("tide");
    a.Characters[0]=new CharacterSave{Name=name,MapId=map,X=x,Y=y,Hp=hp,Journey=new Journey{Resources=new(){["ore"]=20,["wood"]=20,["herb"]=10,["fish"]=10,["echo"]=10}},Equipment=ItemCatalog.Load().StarterEquipment().Items.ToArray(),Inventory=ItemCatalog.Load().StarterItems.ToList()};
    accounts.Save(a);return a;
}
var fixture=Seed("fixture","Fixture");fixture.Characters[0]!.Level=50;accounts.Save(fixture);Seed("player","Player");
var doomed=Seed("doomed","Doomed",2,24,31,1);
doomed.Characters[0]!.Journey.Record("fish",8);doomed.Characters[0]!.Journey.Gold=99;accounts.Save(doomed);
Check("account persistence keeps legacy and verifies passwords",()=>{Require(accounts.Authenticate("player",password)?.Legacy.Memories.Contains("tide")==true);Require(accounts.Authenticate("player","incorrect")==null);});
var maps=new MapStore(Path.Combine(DataPaths.Data,"maps"));
var server=new GameServer(maps,ItemCatalog.Load(),accounts,NpcCatalog.Load());
using var socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);socket.Bind(new IPEndPoint(IPAddress.Loopback,0));var port=((IPEndPoint)socket.LocalEndPoint!).Port;socket.Close();
var clients=new List<Probe>();
void Pump(int ms=80)
{
    var until=Stopwatch.StartNew();while(until.ElapsedMilliseconds<ms){server.Poll();server.Tick();foreach(var c in clients)c.Poll();Thread.Sleep(5);}
}
void Until(Func<bool> predicate,int ms=4000)
{
    var timer=Stopwatch.StartNew();while(!predicate()&&timer.ElapsedMilliseconds<ms)Pump(15);Require(predicate(),"Timed out waiting for server behavior");
}
void Warp(Probe c,int map,int x,int y)
{
    var admin=clients[0];admin.Chat($"/ir {map} {x} {y}");Pump(120);if(c!=admin){admin.Chat($"/trazer {c.Name}");Pump(120);}Until(()=>c.Map==map);Pump(450);
}
try
{
    server.Start(port);
    var admin=new Probe("fixture","Fixture",password,port);clients.Add(admin);
    var player=new Probe("player","Player",password,port);clients.Add(player);
    Check("two clients authenticate and enter through UDP",()=>{Until(()=>admin.Journey!=null&&player.Journey!=null);Require(admin.Id!=player.Id&&player.Map==1&&player.FormCount>0);});
    Check("non-admin cannot teleport",()=>{player.Chat("/ir 2");Pump(150);Require(player.Map==1&&player.Messages.Any(m=>m.Contains("administrador")));});
    Check("crafting is rejected away from a workbench",()=>{var ore=player.Journey!.Resources["ore"];player.Action(AdventureAction.Craft,"smelt");Pump(550);Require(player.Journey.Resources["ore"]==ore);});
    Check("crafting consumes exact ingredients and grants output",()=>{Warp(player,1,17,24);player.Action(AdventureAction.Craft,"smelt");Until(()=>player.Journey!.Resources.GetValueOrDefault("ingot")==1);Require(player.Journey!.Resources["ore"]==17&&player.Journey.Resources["wood"]==18);});
    Check("fishing grants resources and server cooldown prevents spam",()=>{Warp(player,2,12,34);player.Action(AdventureAction.Interact);Until(()=>player.Journey!.Catches==1);var fish=player.Journey!.Resources["fish"];Pump(500);player.Action(AdventureAction.Interact);Pump(500);Require(player.Journey.Catches==1&&player.Journey.Resources["fish"]==fish);});
    Check("mining, woodcutting, gathering and extraction use data-defined yields",()=>
    {
        foreach(var a in new[]{(29,34,"ore"),(18,31,"wood"),(19,37,"herb"),(28,22,"echo")})
        {Warp(player,2,a.Item1,a.Item2);var before=player.Journey!.Resources[a.Item3];player.Action(AdventureAction.Interact);Until(()=>player.Journey!.Resources[a.Item3]>before);}
    });
    Check("blessings and purchases are spatially validated",()=>
    {
        Warp(player,1,22,17);player.Action(AdventureAction.Interact);Until(()=>player.Journey!.LuckSources.Any(s=>s.Name.Contains("Bênção")));
        var gold=player.Journey!.Gold;Pump(450);player.Action(AdventureAction.Buy,"0");Pump(450);Require(player.Journey.Gold==gold);
        Warp(player,1,15,22);var tonic=player.Journey.Count("Tonic");player.Action(AdventureAction.Buy,"0");Until(()=>player.Journey!.Gold==gold-8);Require(player.Journey!.Count("Tonic")==tonic+1);
    });
    Check("expedition generates a floor and seals descent while enemies live",()=>
    {
        Warp(player,2,24,9);player.Action(AdventureAction.Interact);Until(()=>player.Map>=1001);var map=maps.Get(player.Map);Require(map.Depth==1);
        var stairs=map.Landmarks.Single(l=>l.Kind==LandmarkKind.Descent);Warp(player,map.Id,stairs.X,stairs.Y);player.Action(AdventureAction.Interact);Pump(500);Require(player.Map==map.Id&&player.Messages.Any(m=>m.Contains("selada")));
    });
    Check("combat grants loot, enforces skill cooldown and opens the next procedural floor",()=>
    {
        var floor=player.Map;
        // Keep the profession fixture safe while the combat fixture clears the ruins.
        Warp(player,1,22,22);
        Warp(admin,floor,maps.Get(floor).SpawnX,maps.Get(floor).SpawnY);
        var kills=admin.Journey!.Kills;var spells=admin.Journey.Spells;
        var loops=0;
        while(admin.Npcs.Count>0 && loops++<15)
        {
            var target=admin.Npcs.First().Value;
            Warp(admin,floor,target.x,target.y);
            admin.Action(AdventureAction.Skill);Pump(100);
            var after=admin.Journey!.Spells;
            admin.Action(AdventureAction.Skill);Pump(100);
            Require(admin.Journey!.Spells==after,"Skill cooldown was bypassed");
            Pump(2850);
        }
        Require(admin.Npcs.Count==0,"Enemies remained after combat");
        Require(admin.Journey!.Kills>kills&&admin.Journey.Spells>spells&&admin.Journey.Gold>20,"Missing victory rewards");
        var stairs=maps.Get(floor).Landmarks.Single(l=>l.Kind==LandmarkKind.Descent);
        Warp(admin,floor,stairs.X,stairs.Y);admin.Action(AdventureAction.Interact);Until(()=>admin.Map==floor+1);
        Require(maps.Get(admin.Map).Depth==2);
    });
    Check("admins edit adventure and evolution live, players cannot",()=>{
        void Save(Probe who,ContentKind kind,string json)=>who.Send(PacketType.ContentSave,w=>{w.Put((byte)kind);w.PutBlob(System.Text.Encoding.UTF8.GetBytes(json));});
        Until(()=>admin.Adventure!=null&&player.Adventure!=null&&admin.Evolution!=null);
        Require(player.Evolution==null,"Evolution rules reached a player");
        var edited=AdventureContent.Parse(admin.Adventure!);var price=edited.Shop.Offers[0].Price;edited.Shop.Offers[0].Price=price+7;
        Save(player,ContentKind.Adventure,edited.ToJson());Pump(400);
        Require(AdventureContent.Parse(player.Adventure!).Shop.Offers[0].Price==price,"A player changed the shop");
        Save(admin,ContentKind.Adventure,edited.ToJson());
        Until(()=>AdventureContent.Parse(player.Adventure!).Shop.Offers[0].Price==price+7);
        Require(AdventureContent.Load().Shop.Offers[0].Price==price+7,"The shop change was not written");
        var broken=AdventureContent.Parse(admin.Adventure!);broken.Recipes[0].Costs["nada"]=1;
        Save(admin,ContentKind.Adventure,broken.ToJson());Pump(400);
        Require(AdventureContent.Load().Recipes[0].Costs.Count==edited.Recipes[0].Costs.Count,"Broken content was saved");
        var rules=EvolutionRules.Parse(admin.Evolution!);rules.Rules[0].Tint="vermelho";
        Save(admin,ContentKind.Evolution,rules.ToJson());Pump(400);
        Require(EvolutionRules.Load().Rules[0].Tint!="vermelho","A bad colour was saved");
        rules.Rules[0].Tint="#123456";Save(admin,ContentKind.Evolution,rules.ToJson());
        Until(()=>EvolutionRules.Parse(admin.Evolution!).Rules[0].Tint=="#123456");
        edited.Shop.Offers[0].Price=price;Save(admin,ContentKind.Adventure,edited.ToJson());
        Until(()=>AdventureContent.Parse(player.Adventure!).Shop.Offers[0].Price==price);
    });
    Check("map saves keep scenery to files players have",()=>{
        Warp(admin,1,22,22);
        var path=Path.Combine(DataPaths.Data,"maps","1.json");
        MapData Disk()=>MapData.FromBytes(File.ReadAllBytes(path));
        var map=Disk();var rev=map.Revision;var props=map.Props.Count;
        map.Props.Add(new MapProp{Sprite="../../segredo.png",X=2,Y=2});
        admin.Send(PacketType.MapSave,w=>w.PutBlob(map.ToBytes()));Pump(500);
        Require(Disk().Revision==rev&&admin.Messages.Any(m=>m.Contains("cenário")),"A prop outside the game's files was saved");
        map.Props[^1]=new MapProp{Sprite="props/pine.png",X=2,Y=2,Width=2,Height=3,Solid=[1,2,1,1]};
        map.Landmarks.Add(new Landmark{Kind=LandmarkKind.Forage,Name="Teste",X=3,Y=3});
        admin.Send(PacketType.MapSave,w=>w.PutBlob(map.ToBytes()));
        Until(()=>Disk().Revision==rev+1);
        Require(Disk().Props.Count==props+1&&Disk().Props[^1].Solid.Length==4&&Disk().Landmarks.Any(l=>l.Name=="Teste"));
    });
    Check("death resets form and possessions but preserves account memories",()=>
    {
        // Establish a nearby encounter after the long profession/combat checks.
        // Ambient enemies may have wandered away or been drawn into another clearing.
        Warp(admin,2,24,32);admin.Chat("/npc 10");Pump(80);Warp(admin,1,22,22);
        var victim=new Probe("doomed","Doomed",password,port);clients.Add(victim);
        Until(()=>victim.Journey?.Legacy.Lives==2,12000);
        Require(victim.Map==1&&victim.Journey!.Calling=="unformed"&&victim.Journey.Gold==20&&victim.Journey.Resources.Count==0&&victim.Journey.Legacy.Memories.Contains("tide"));
    });
    Check("server shutdown persists earned profession progress",()=>{server.Stop();Require(accounts.Authenticate("player",password)?.Characters[0]?.Journey.Catches==1);});
}
finally
{
    foreach(var client in clients)client.Dispose();server.Stop();
    Directory.Delete(root,true);
}
Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
return failed==0?0:1;

sealed class Probe : IDisposable
{
    readonly EventBasedNetListener listener=new(); readonly NetManager net; NetPeer? peer;
    public string Name{get;} public string? Adventure;public string? Evolution;public int Id;public int Map;public int FormCount;public Journey? Journey;public List<string> Messages=[];public Dictionary<int,(int x,int y)> Npcs=[];
    public Probe(string username,string name,string password,int port)
    {
        Name=name;net=new NetManager(listener){AutoRecycle=true};
        listener.PeerConnectedEvent+=p=>{peer=p;Send(PacketType.Login,w=>{w.Put(username);w.Put(password);});};
        listener.NetworkReceiveEvent+=(_,r,_,_)=>
        {
            var type=(PacketType)r.GetByte();
            switch(type)
            {
                case PacketType.CharacterList:Send(PacketType.PlayCharacter,w=>w.Put((byte)0));break;
                case PacketType.LoginOk:Id=r.GetPlayerInfo().Id;break;
                case PacketType.MapChange:Map=r.GetInt();Npcs.Clear();break;
                case PacketType.NpcSpawned:{var index=r.GetInt();r.GetInt();Npcs[index]=(r.GetInt(),r.GetInt());break;}
                case PacketType.NpcMoved:{var index=r.GetInt();Npcs[index]=(r.GetInt(),r.GetInt());break;}
                case PacketType.NpcRemoved:Npcs.Remove(r.GetInt());break;
                case PacketType.Journey:Journey=JsonSerializer.Deserialize<Journey>(r.GetString());break;
                case PacketType.FormChanged:FormCount++;break;
                case PacketType.AdventureCatalog:Adventure=r.GetString();break;
                case PacketType.EvolutionCatalog:Evolution=r.GetString();break;
                case PacketType.ChatMessage:r.GetString();Messages.Add(r.GetString());break;
                case PacketType.Refused:Messages.Add(r.GetString());break;
            }
        };
        net.Start();net.Connect("127.0.0.1",port,Constants.ConnectionKey);
    }
    public void Poll()=>net.PollEvents();
    public void Send(PacketType type,Action<NetDataWriter> payload){var w=PacketIO.Begin(type);payload(w);peer?.Send(w,DeliveryMethod.ReliableOrdered);}
    public void Chat(string text)=>Send(PacketType.ChatSend,w=>w.Put(text));
    public void Action(AdventureAction action,string? recipe=null)=>Send(PacketType.AdventureAction,w=>{w.Put((byte)action);if(recipe!=null)w.Put(recipe);});
    public void Dispose()=>net.Stop();
}
