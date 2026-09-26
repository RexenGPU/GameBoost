using LiteDB;

var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameBoost", "gameboost.db");
if (args.Length > 0 && args[0] == "--path") path = args[1];
Console.WriteLine("DB: " + path);

using var db = new LiteDatabase(path);
var clean = args.Contains("--clean");

if (clean)
{
    Console.WriteLine("--- NETTOYAGE ---");

    var sessions = db.GetCollection("sessions");
    var testSessions = sessions.FindAll().Where(d =>
        (d.ContainsKey("GameName") && (d["GameName"].AsString.StartsWith("Session Test") || d["GameName"].AsString.Contains("AgentR") || d["GameName"].AsString.Contains("Jeu factice")))
        || (d.ContainsKey("GameId") && d["GameId"].AsString.Contains("agent-r"))).ToList();
    foreach (var d in testSessions) { sessions.Delete(d["_id"]); Console.WriteLine($"DELETE session {d["_id"]} {d["GameName"].AsString}"); }

    var profiles = db.GetCollection("profiles");
    var testProfiles = profiles.FindAll().Where(d =>
        (d.ContainsKey("GameId") && d["GameId"].AsString.Contains("g2-"))
        || (d.ContainsKey("GameName") && d["GameName"].AsString == "FakeGameUe")).ToList();
    foreach (var d in testProfiles) { profiles.Delete(d["_id"]); Console.WriteLine($"DELETE profile {d["_id"]} {d["Name"].AsString}"); }

    var boosts = db.GetCollection("boosts");
    var testBoosts = boosts.FindAll().Where(d =>
        (d.ContainsKey("GameName") && d["GameName"].AsString.Contains("Jeu factice"))
        || (d.ContainsKey("GameId") && d["GameId"].AsString.Contains("agent-a"))).ToList();
    foreach (var d in testBoosts) { boosts.Delete(d["_id"]); Console.WriteLine($"DELETE boost {d["_id"]} {d["GameName"].AsString}"); }

    var overlay = db.GetCollection("overlay");
    var testOverlay = overlay.FindAll().Where(d => d["_id"].AsString.Contains("agent")).ToList();
    foreach (var d in testOverlay) { overlay.Delete(d["_id"]); Console.WriteLine($"DELETE overlay {d["_id"]}"); }

    db.Commit();
    Console.WriteLine("Nettoyage termine.");
}

foreach (var name in db.GetCollectionNames())
{
    var col = db.GetCollection(name);
    Console.WriteLine($"[{name}] count={col.Count()}");
}
