using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Models;

/// <summary>Проверяет отдельную версию правил ЧС, изоляцию снимка и откат секций при неуспешном сохранении.</summary>
public sealed class BlacklistSnapshotVersionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StartupMonitorBlacklistVersion_" + Guid.NewGuid().ToString("N"));

    /// <summary>Создаёт изолированное хранилище с одним правилом и существующими секционными файлами.</summary>
    /// <returns>Хранилище, принадлежащее текущему тесту.</returns>
    private JsonDataStore CreateStore()
    {
        var store = new JsonDataStore(_directory);
        var config = store.Load();
        config.Blacklist.Add(new BlacklistRule { Id = "initial", Value = @"C:\Apps\blocked.exe" });
        config.Entries.Add(new StartupEntry { Id = "initial-entry", Name = "Initial entry" });
        store.Save(config);
        return store;
    }

    /// <summary>Проверяет, что настройки, история и записи автозапуска не перезапускают поколение правил ЧС.</summary>
    [Fact]
    public void Save_UnrelatedChanges_DoNotChangeBlacklistVersionOrPublishEvent()
    {
        var store = CreateStore();
        var version = store.GetBlacklistSnapshot().Version;
        var events = 0;
        store.BlacklistChanged += () => events++;
        var config = store.Load();
        config.Theme = "light";
        config.Settings.NotifyOnNewEntry = !config.Settings.NotifyOnNewEntry;
        store.Save(config);
        store.AddEntry(new StartupEntry { Id = "other-entry", Name = "Other entry" });
        store.AddHistory(new HistoryEntry { Id = "history", ChangeType = "unrelated" });

        Assert.Equal(version, store.BlacklistRevision);
        Assert.Equal(version, store.GetBlacklistSnapshot().Version);
        Assert.Equal(0, events);
    }

    /// <summary>Проверяет обновление поколения при добавлении, изменении и удалении правил через общий Save.</summary>
    [Fact]
    public void Save_EachRuleMutation_PublishesNewVersionAndIsolatedSnapshot()
    {
        var store = CreateStore();
        var version = store.BlacklistRevision;
        var events = 0;
        store.BlacklistChanged += () => events++;
        var mutations = new Action<AppConfig>[]
        {
            config => config.Blacklist[0].Value = @"D:\Apps\changed.exe",
            config => config.Blacklist[0].Action = BlacklistAction.NotifyOnly,
            config => config.Blacklist[0].MatchType = BlacklistMatchType.PathWildcard,
            config => config.Blacklist.Add(new BlacklistRule { Id = "added", Value = "other.exe" }),
            config => config.Blacklist.RemoveAt(0)
        };
        foreach (var mutation in mutations)
        {
            var config = store.Load();
            mutation(config);
            store.Save(config);
            Assert.Equal(++version, store.GetBlacklistSnapshot().Version);
        }

        Assert.Equal(mutations.Length, events);
        var snapshot = store.GetBlacklistSnapshot();
        var snapshotRule = Assert.Single(snapshot.Rules);
        var originalValue = snapshotRule.Value;
        snapshotRule.Value = "changed-by-reader.exe";
        Assert.Equal(originalValue, Assert.Single(store.GetBlacklistSnapshot().Rules).Value);
        Assert.Equal(version, store.BlacklistRevision);
    }

    /// <summary>Проверяет отказ последней секции: кэш и версия не публикуются, ранее записанные секции возвращаются к старым данным.</summary>
    [Fact]
    public void Save_SettingsFileLocked_RollsBackEarlierSectionsAndDoesNotPublishBlacklist()
    {
        var store = CreateStore();
        var before = store.GetBlacklistSnapshot();
        var oldEntries = File.ReadAllBytes(Path.Combine(_directory, "startup-entries.json"));
        var oldBlacklist = File.ReadAllBytes(Path.Combine(_directory, "blacklist.json"));
        var events = 0;
        store.BlacklistChanged += () => events++;
        var pending = store.Load();
        pending.Theme = "light";
        pending.Blacklist[0].Value = @"D:\Changed\blocked.exe";
        pending.Entries[0].Name = "Changed entry";

        using (var locked = new FileStream(Path.Combine(_directory, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var failure = Record.Exception(() => store.Save(pending));
            Assert.True(failure is IOException or UnauthorizedAccessException,
                $"Ожидался отказ записи заблокированной секции, получено: {failure?.GetType().Name ?? "успех"}.");
        }

        Assert.Equal(before.Version, store.BlacklistRevision);
        Assert.Equal(before.Rules[0].Value, Assert.Single(store.GetBlacklistSnapshot().Rules).Value);
        Assert.Equal("Initial entry", Assert.Single(store.Load().Entries).Name);
        Assert.Equal("dark", store.Load().Theme);
        Assert.Equal(oldEntries, File.ReadAllBytes(Path.Combine(_directory, "startup-entries.json")));
        Assert.Equal(oldBlacklist, File.ReadAllBytes(Path.Combine(_directory, "blacklist.json")));
        Assert.Equal(0, events);

        store.Save(pending);
        Assert.Equal(before.Version + 1, store.BlacklistRevision);
        Assert.Equal(@"D:\Changed\blocked.exe", Assert.Single(new JsonDataStore(_directory).Load().Blacklist).Value);
        Assert.Equal(1, events);
    }

    /// <summary>Проверяет, что подписчик события может получить новый снимок из другого потока без блокировки Save.</summary>
    [Fact]
    public void BlacklistChanged_IsRaisedAfterStoreLockIsReleased()
    {
        var store = CreateStore();
        var readCompleted = false;
        long observedVersion = 0;
        store.BlacklistChanged += () =>
        {
            var read = Task.Run(() => store.GetBlacklistSnapshot());
            readCompleted = read.Wait(TimeSpan.FromSeconds(2));
            if (readCompleted) observedVersion = read.Result.Version;
        };
        var config = store.Load();
        config.Blacklist[0].Value = "changed.exe";

        store.Save(config);

        Assert.True(readCompleted, "Событие изменения правил не должно вызываться под блокировкой хранилища.");
        Assert.Equal(store.BlacklistRevision, observedVersion);
    }

    /// <summary>Удаляет только уникальный каталог текущего теста после закрытия всех файлов.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
