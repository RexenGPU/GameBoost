using GameBoost.Core.Logging;
using GameBoost.Core.Models;
using LiteDB;

namespace GameBoost.Core.Data;

public static class Db
{
    private static readonly object Sync = new();
    private static LiteDatabase? _db;
    private static bool _failed;

    public static bool IsAvailable => !_failed;

    public static LiteDatabase Open()
    {
        lock (Sync)
        {
            if (_db is not null) return _db;
            if (_failed) throw new InvalidOperationException("La base locale GameBoost n'est pas disponible.");
            try
            {
                AppPaths.Ensure();
                _db = new LiteDatabase(new ConnectionString
                {
                    Filename = AppPaths.DatabasePath,
                    Connection = ConnectionType.Shared,
                    Upgrade = true
                });
                GetCollection<GameInfo>("games");
                GetCollection<GameProfile>("profiles");
                GetCollection<SessionRecord>("sessions");
                GetCollection<AnalysisReport>("analyses");
                GetCollection<BoostSessionState>("boosts");
                GetCollection<UserNotification>("notifications");
                Log.Info("Db", "Base locale ouverte : " + AppPaths.DatabasePath);
                return _db;
            }
            catch (Exception ex)
            {
                _failed = true;
                Log.Error("Db", "Impossible d'ouvrir la base locale", ex);
                throw;
            }
        }
    }

    public static ILiteCollection<T> GetCollection<T>(string name)
    {
        return Open().GetCollection<T>(name);
    }

    public static bool TryGetCollection<T>(string name, out ILiteCollection<T> collection)
    {
        try
        {
            collection = GetCollection<T>(name);
            return true;
        }
        catch
        {
            collection = null!;
            return false;
        }
    }

    public static void Close()
    {
        lock (Sync)
        {
            try
            {
                _db?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn("Db", "Fermeture imparfaite : " + ex.Message);
            }
            _db = null;
        }
    }
}
