using GameBoost.Core.Data;
using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using LiteDB;

namespace GameBoost.Core.Optimization;

internal static class BoostStore
{
    private const string CollectionName = "boosts";

    internal static void Upsert(BoostSessionState session)
    {
        if (session is null) return;
        try
        {
            var collection = Db.GetCollection<BsonDocument>(CollectionName);
            var document = BsonMapper.Global.ToDocument(session);
            document["_id"] = session.SessionId.ToString();
            collection.Upsert(document);
        }
        catch (Exception ex)
        {
            Log.Error("Boost", "Enregistrement de la session de boost impossible", ex);
        }
    }

    internal static BoostSessionState? FindActive()
    {
        try
        {
            var collection = Db.GetCollection<BsonDocument>(CollectionName);
            foreach (var document in collection.Find(Query.EQ("IsActive", true)))
            {
                try
                {
                    var session = BsonMapper.Global.ToObject<BoostSessionState>(document);
                    if (session is not null && session.IsActive) return session;
                }
                catch (Exception ex)
                {
                    Log.Warn("Boost", "Session de boost illisible : " + ex.Message);
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Boost", "Lecture des sessions de boost impossible", ex);
            return null;
        }
    }

    internal static bool HasActive()
    {
        try
        {
            return Db.GetCollection<BsonDocument>(CollectionName).Exists(Query.EQ("IsActive", true));
        }
        catch (Exception ex)
        {
            Log.Error("Boost", "Vérification des sessions de boost impossible", ex);
            return false;
        }
    }
}
