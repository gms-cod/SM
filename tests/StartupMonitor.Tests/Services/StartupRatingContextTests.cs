using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services.Ai;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет изоляцию AI-рекомендаций по контексту автозапуска.</summary>
public sealed class StartupRatingContextTests
{
    /// <summary>Уведомление о готовой оценке отправляется лишь после подтверждённого сохранения; отклонённый ответ его не создаёт.</summary>
    [Fact]
    public void AutomaticRatingChange_NotifiesOnlyAfterSuccessfulConditionalSave()
    {
        var directory = Path.Combine(Path.GetTempPath(), "startup-rating-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonDataStore(directory);
            var entry = new StartupEntry { Id = "entry", Name = "entry", CommandLine = "original" };
            store.AddEntry(entry);
            var notifications = new List<string>();
            var cache = new RatingsCache(store, saved =>
            {
                Assert.Equal(3, Assert.Single(store.Load().Entries).AiRating);
                notifications.Add(saved.Id);
            });
            var fingerprint = StartupRatingContext.Compute(entry);
            var rating = new StartupRating { Score = 3, Recommendation = "allow" };

            Assert.True(cache.TrySetAutomatic(entry, rating, fingerprint));
            Assert.Equal(new[] { "entry" }, notifications);
            var current = Assert.Single(store.Load().Entries);
            current.CommandLine = "changed";
            store.UpdateEntry(current);
            Assert.False(cache.TrySetAutomatic(entry, rating, fingerprint));
            Assert.Single(notifications);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Временное обогащение пути не делает вердикт, проверенный до обогащения, несовместимым со снимком хранилища.</summary>
    [Fact]
    public void SnapshotRating_IsValidatedBeforeTransientPathEnrichment()
    {
        var directory = Path.Combine(Path.GetTempPath(), "startup-rating-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var executable = Path.Combine(directory, "sample.exe");
            File.WriteAllText(executable, "sample");
            var store = new JsonDataStore(directory);
            var entry = new StartupEntry { Id = "entry", Name = "entry", CommandLine = $"\"{executable}\"", Location = EntryLocation.RegistryRun };
            store.AddEntry(entry);
            var cache = new RatingsCache(store);
            cache.Set(entry, new StartupRating { Score = 2, Recommendation = "allow" });
            var snapshot = Assert.Single(store.Load().Entries);
            RatingsCache.SanitizeForRead(snapshot);
            EntryOperations.ResolveAndEnrichEntry(snapshot, computeHash: false);
            Assert.Equal(executable, snapshot.FilePath);
            Assert.Equal(2, snapshot.AiRating);
            Assert.Equal(2, cache.Get(Assert.Single(store.Load().Entries))?.Score);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>После перезапуска изменённая команда скрывает старый вердикт без удаления оценки с диска.</summary>
    [Fact]
    public void Restart_ChangedCommand_HidesStaleAutomaticRating()
    {
        var directory = Path.Combine(Path.GetTempPath(), "startup-rating-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonDataStore(directory);
            var entry = new StartupEntry { Id = "entry", Name = "entry", CommandLine = "safe", SHA256 = "ABC" };
            store.AddEntry(entry);
            var cache = new RatingsCache(store);
            cache.Set(entry, new StartupRating { Score = 2, Recommendation = "allow" });
            var current = Assert.Single(store.Load().Entries);
            current.CommandLine = "changed";
            store.UpdateEntry(current);

            var restarted = new JsonDataStore(directory);
            var snapshot = Assert.Single(restarted.Load().Entries);
            Assert.Null(new RatingsCache(restarted).Get(snapshot));
            Assert.Equal(0, snapshot.AiRating);
            Assert.Equal(2, Assert.Single(restarted.Load().Entries).AiRating);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Скрывает неподтверждённые старые автоматические оценки без записи на диск и сохраняет ручные.</summary>
    /// <param name="english">Проверяет английский вариант старого текста.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacySelfTrust_IsClearedWithoutTouchingOtherRatings(bool english)
    {
        var directory = Path.Combine(Path.GetTempPath(), "startup-rating-" + Guid.NewGuid().ToString("N"));
        try
        {
            StartupEntry Legacy(string id) => new()
            {
                Id = id, Name = id, SHA256 = "ABC", AiRating = 1,
                AiCategory = english ? "Security & Monitoring" : "Безопасность и мониторинг",
                AiRisk = english
                    ? "Fully trusted software: official Startup Monitor component. Provides continuous registry monitoring, startup security, firewall filtering, and protection against unauthorized persistence."
                    : "Абсолютно доверенное ПО: официальный компонент защитного комплекса Startup Monitor. Обеспечивает непрерывный мониторинг реестра, контроль автозагрузки, сетевой экран и защиту от вредоносных модулей.",
                AiRecommendation = english
                    ? "Allow startup. Vital component for system defense and persistence monitoring."
                    : "Разрешить автозапуск. Критически важный компонент для мониторинга системы и предотвращения угроз."
            };
            var store = new JsonDataStore(directory);
            var legacy = Legacy("legacy");
            var manual = Legacy("manual");
            manual.IsUserRating = true;
            var ordinary = Legacy("ordinary");
            ordinary.AiRecommendation += "!";
            var cfg = store.Load();
            cfg.Entries.AddRange([legacy, manual, ordinary]);
            store.Save(cfg);
            var cache = new RatingsCache(store);

            Assert.Null(cache.Get(legacy));
            Assert.NotNull(cache.Get(manual));
            Assert.Null(cache.Get(ordinary));
            var saved = store.Load().Entries;
            Assert.Equal(1, saved.Single(e => e.Id == "legacy").AiRating);
            Assert.Equal(1, saved.Single(e => e.Id == "manual").AiRating);
            Assert.True(saved.Single(e => e.Id == "manual").IsUserRating);
            Assert.Equal(1, saved.Single(e => e.Id == "ordinary").AiRating);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Один файл с разными параметрами и местоположением не наследует чужую рекомендацию.</summary>
    [Fact]
    public async Task SameHash_DifferentCommandOrLocation_DoesNotReuseVerdict()
    {
        var directory = Path.Combine(Path.GetTempPath(), "startup-rating-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonDataStore(directory);
            var cache = new RatingsCache(store);
            using var service = new AiRatingService(cache, NullLogger<AiRatingService>.Instance, "", apiUrl: "https://example.com/v1");
            StartupEntry Create(string id, string args, EntryLocation location, string detail) => new()
            {
                Id = id, Name = "Test", FilePath = @"C:\Apps\test.exe", SHA256 = "ABC123",
                CommandLine = args, Location = location, LocationDetail = detail
            };
            var original = Create("1", "--safe", EntryLocation.RegistryRun, "Run");
            var same = Create("2", "--safe", EntryLocation.RegistryRun, "Run");
            var command = Create("3", "--danger", EntryLocation.RegistryRun, "Run");
            var location = Create("4", "--safe", EntryLocation.TaskScheduler, "Task");
            var detail = Create("5", "--safe", EntryLocation.RegistryRun, "RunOnce");
            var entries = new List<StartupEntry> { original, same, command, location, detail };
            var config = store.Load();
            config.Entries.AddRange(entries);
            store.Save(config);
            cache.Set(original, new StartupRating { Score = 2, Recommendation = "Оставить включённым" });

            await service.PrefetchAllAsync(entries);

            Assert.Equal(2, cache.Get(same)?.Score);
            Assert.Null(cache.Get(command));
            Assert.Null(cache.Get(location));
            Assert.Null(cache.Get(detail));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Ручная оценка принадлежит конкретной записи и не переносится на другую.</summary>
    [Fact]
    public void ManualRating_DoesNotApplyToAnotherEntry()
    {
        var directory = Path.Combine(Path.GetTempPath(), "startup-rating-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new RatingsCache(new JsonDataStore(directory));
            var manual = new StartupEntry { Id = "1", Name = "Test", SHA256 = "ABC", IsUserRating = true, AiRating = 1, AiRecommendation = "Manual" };
            var other = new StartupEntry { Id = "2", Name = "Test", SHA256 = "ABC" };
            Assert.NotNull(cache.Get(manual));
            Assert.Null(cache.Get(other));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

}
